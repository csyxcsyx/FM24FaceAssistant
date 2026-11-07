using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace FM24FaceAssistant;

public sealed record LibraryDownloadOptions(Uri Source,long ExpectedBytes,int Attempts=8,int RetryDelayMilliseconds=5000,int StallMilliseconds=90000)
{
    // Zero means discover the current release length from the server, not a pinned old ZIP size.
    public static LibraryDownloadOptions Official {get;}=new(new Uri("https://download.ngregens.org/NG_Regens_MainPack.zip"),0);
}
public sealed record LibraryProgress(string stage,string task_id,int pid,string updated,long total=0,long downloaded=0,long network_received=0,long bytes_per_second=0,long eta_seconds=0,int attempt=0,long processed=0,long total_images=0,long unique_images=0,long unique_pixels=0,string? error=null,string? library=null);
public sealed record LibraryDownloadResult(string Library,int Count);
public sealed record LibraryTransferIdentity(string Source,string ETag,long Total);

/// <summary>In-process, bounded-memory download and image indexing. Never edits assignments or FM files.</summary>
public sealed class LibraryDownloadService
{
    public bool IsRunning=>Volatile.Read(ref running)!=0;
    public string? TaskId {get;private set;}
    int running;
    readonly LibraryDownloadOptions options;
    readonly HttpClient? injectedClient;
    readonly Action<string,long>? spaceCheck;
    public LibraryDownloadService(LibraryDownloadOptions? options=null,HttpClient? client=null,Action<string,long>? spaceCheck=null)
    {this.options=options??LibraryDownloadOptions.Official;injectedClient=client;this.spaceCheck=spaceCheck;}

    public async Task<LibraryDownloadResult> RunAsync(string assets,string state,IProgress<LibraryProgress>? progress,CancellationToken ct)
    {
        if(Interlocked.CompareExchange(ref running,1,0)!=0)throw new InvalidOperationException("图库任务正在进行，请查看当前进度。");
        TaskId=Guid.NewGuid().ToString("N");
        try{return await Task.Run(()=>RunCore(assets,state,progress,ct),CancellationToken.None).ConfigureAwait(false);}
        catch(UnauthorizedAccessException ex){throw new IOException("下载目录没有写入权限，请选择其他普通文件夹后重试。",ex);}
        finally{Volatile.Write(ref running,0);}
    }
    async Task<LibraryDownloadResult> RunCore(string assets,string state,IProgress<LibraryProgress>? progress,CancellationToken ct)
    {
        assets=Path.GetFullPath(assets);state=Path.GetFullPath(state);
        try{Directory.CreateDirectory(assets);Directory.CreateDirectory(state);}
        catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException){throw new IOException("无法创建图库下载或数据目录。请在设置中选择可写入的普通文件夹。",ex);}
        // Byte-range lock is compatible with the legacy Python msvcrt lock.
        using var lease=new FileStream(Path.Combine(assets,"download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);
        try{lease.Lock(0,1);}catch(IOException){throw new IOException("该目录已有图库任务。请先停止旧版下载，再点击接续；已有文件不会删除。");}
        if(lease.Length==0){lease.WriteByte(0);lease.Flush(true);}
        LibraryProgress status=new("preparing",TaskId!,Environment.ProcessId,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),options.ExpectedBytes);
        void Publish(LibraryProgress value)
        {
            status=value with{updated=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")};
            Files.Save(Path.Combine(assets,"download-status.json"),status);progress?.Report(status);
        }
        try
        {
            Publish(status);ct.ThrowIfCancellationRequested();
            var archive=Path.Combine(assets,"NG_Regens_MainPack.zip");
            var library=Path.Combine(assets,"NG_Regens_Library");
            // A complete legacy library may have had its ZIP removed to save disk space.
            if(!File.Exists(archive)&&!File.Exists(archive+".part")&&File.Exists(Path.Combine(library,"faces.json")))
            {
                List<Face> old;
                try{old=Files.Read<List<Face>>(Path.Combine(library,"faces.json"))??new();}catch(JsonException){old=new();}
                bool reusable=HasCompletionReport(assets,old.Count);
                var verified=new List<Face>();
                foreach(var face in reusable?old:new List<Face>())
                {
                    ct.ThrowIfCancellationRequested();var path=SafeTarget(library,Path.GetRelativePath(library,Path.GetFullPath(Path.Combine(library,face.Path))));
                    if(!Catalog.IsFm24Face(face))continue;
                    if(!File.Exists(path)||Digest(path)!=face.Hash){reusable=false;break;}
                    var visual=Catalog.PixelDigest(path);verified.Add(face with{Path=path,VisualHash=visual});
                    if(verified.Count%128==0)Publish(status with{stage="indexing",processed=verified.Count,total_images=old.Count});
                }
                if(reusable&&verified.Count>0){
                ct.ThrowIfCancellationRequested();Files.Save(Path.Combine(state,"catalog.json"),verified);
                Publish(status with{stage="complete",processed=verified.Count,total_images=verified.Count,unique_pixels=verified.Select(f=>f.UniqueKey).Distinct().Count(),library=library});
                return new(library,verified.Count);
                }
            }
            await Download(assets,archive,ct,s=>Publish(s),()=>status).ConfigureAwait(false);
            var rows=Extract(assets,archive,ct,s=>Publish(s),()=>status);
            // Commit only after every supported image has passed CRC and decoding.
            ct.ThrowIfCancellationRequested();Files.Save(Path.Combine(library,"faces.json"),rows);
            Files.Save(Path.Combine(assets,"library-report.json"),new{source=options.Source.AbsoluteUri,archive,archive_bytes=new FileInfo(archive).Length,total_images=rows.Count,unique_pixels=rows.Select(f=>f.UniqueKey).Distinct().Count(),library,state_directory=state});
            Files.Save(Path.Combine(state,"catalog.json"),rows);
            Publish(status with{stage="complete",processed=rows.Count,total_images=rows.Count,unique_pixels=rows.Select(f=>f.UniqueKey).Distinct().Count(),library=library});
            return new(library,rows.Count);
        }
        catch(Exception ex)
        {
            var message=ex switch{OperationCanceledException=>"已保存进度，下次可继续。",UnauthorizedAccessException=>"该目录无法写入，请选择有写入权限的下载位置。",InvalidDataException=>"图库校验失败："+ex.Message,HttpRequestException=>"网络连接失败，请检查网络后点击重试。",TimeoutException=>"连接长时间没有数据，请检查网络后重试。",_=>ex.Message};
            try{Publish(status with{stage=ex is OperationCanceledException?"cancelled":"failed",error=message});File.AppendAllText(Path.Combine(assets,"worker.log"),DateTimeOffset.Now+" "+ex+Environment.NewLine);}catch{ }
            if(ex is OperationCanceledException)throw;
            throw new IOException(message,ex);
        }
        finally{lease.Unlock(0,1);}
    }
    bool HasCompletionReport(string assets,int count)
    {
        if(count<=0)return false;
        try{
            using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(assets,"library-report.json")));var data=report.RootElement;
            return data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("source",out var source)&&source.ValueKind==JsonValueKind.String&&source.GetString()==options.Source.AbsoluteUri&&data.TryGetProperty("total_images",out var total)&&total.ValueKind==JsonValueKind.Number&&total.TryGetInt32(out var images)&&images==count&&data.TryGetProperty("archive_bytes",out var bytes)&&bytes.ValueKind==JsonValueKind.Number&&bytes.TryGetInt64(out var size)&&size>0;
        }catch(Exception ex) when(ex is IOException||ex is JsonException){return false;}
    }
    void CheckSpace(string directory,long bytes)
    {
        if(spaceCheck!=null){spaceCheck(directory,bytes);return;}
        var free=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
        if(free<bytes)throw new IOException($"磁盘空间不足，需要至少 {bytes/1e9:F2} GB，当前可用 {free/1e9:F2} GB。请在设置中选择空间充足的下载位置。");
    }
    async Task Download(string assets,string archive,CancellationToken ct,Action<LibraryProgress> publish,Func<LibraryProgress> current)
    {
        if(File.Exists(archive))
        {
            if(options.ExpectedBytes<=0||new FileInfo(archive).Length==options.ExpectedBytes)return;
            throw new InvalidDataException("原压缩包大小与当前图库不符，已保留文件。请选择其他下载目录或完整的当前图库。");
        }
        var part=archive+".part";var identityPath=Path.Combine(assets,"transfer-identity.json");
        var probe=Path.Combine(assets,"range-probe.bin");if(!File.Exists(part)&&File.Exists(probe))File.Move(probe,part);
        using var ownedClient=injectedClient==null?new HttpClient(new SocketsHttpHandler{ConnectTimeout=TimeSpan.FromSeconds(30),AutomaticDecompression=DecompressionMethods.None}){Timeout=Timeout.InfiniteTimeSpan}:null;
        var client=injectedClient??ownedClient!;
        for(int attempt=1;attempt<=options.Attempts;attempt++)
        {
            ct.ThrowIfCancellationRequested();var existing=File.Exists(part)?new FileInfo(part).Length:0;
            if(options.ExpectedBytes>0&&existing>options.ExpectedBytes)throw new InvalidDataException("下载片段大于当前图库，未覆盖旧文件。请改选下载位置。");
            if(options.ExpectedBytes>0&&existing==options.ExpectedBytes){File.Move(part,archive);return;}
            CheckSpace(assets,checked(Math.Max(0,options.ExpectedBytes-existing)+1024L*1024*1024));
            publish(current() with{stage="downloading",downloaded=existing,network_received=0,attempt=attempt});
            try
            {
                LibraryTransferIdentity? identity=null;
                try{identity=Files.Read<LibraryTransferIdentity>(identityPath);}catch(JsonException){ }
                bool range=existing>0&&identity?.Source==options.Source.AbsoluteUri&&identity.Total>existing&&(options.ExpectedBytes<=0||identity.Total==options.ExpectedBytes)&&EntityTagHeaderValue.TryParse(identity.ETag,out var tag)&&!tag.IsWeak;
                using var request=new HttpRequestMessage(HttpMethod.Get,options.Source);
                request.Headers.UserAgent.ParseAdd("FM24FaceAssistant/"+typeof(LibraryDownloadService).Assembly.GetName().Version);
                if(range){request.Headers.Range=new RangeHeaderValue(existing,null);request.Headers.IfRange=new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(identity!.ETag));}
                using var headersTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct);headersTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                HttpResponseMessage response;
                try{response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,headersTimeout.Token).ConfigureAwait(false);}
                catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new TimeoutException("连接图库服务器超时。");}
                using(response)
                {
                    response.EnsureSuccessStatusCode();
                    bool partial=response.StatusCode==HttpStatusCode.PartialContent;
                    var etag=response.Headers.ETag;
                    var total=partial?response.Content.Headers.ContentRange?.Length:response.Content.Headers.ContentLength;
                    if(total is not >0)throw new InvalidDataException("图库服务器未提供有效的文件大小，请稍后重试。");
                    if(partial&&(!range||response.Content.Headers.ContentRange?.From!=existing||response.Content.Headers.ContentRange?.To!=total-1||total!=identity!.Total||etag?.ToString()!=identity.ETag))throw new InvalidDataException("服务器返回了不一致的断点数据，已保留旧进度。");
                    if(options.ExpectedBytes>0&&total!=options.ExpectedBytes)throw new InvalidDataException("图库大小与指定版本不符，已保留原文件。");
                    if(existing>total)throw new InvalidDataException("已有片段大于服务器当前文件，已保留。请改选下载位置。");
                    if(response.Content.Headers.ContentLength is long size&&size!=total-(partial?existing:0))throw new InvalidDataException("图库响应长度不一致，已保留原文件。");
                    CheckSpace(assets,checked(total.Value-existing+1024L*1024*1024));
                    publish(current() with{total=total.Value});
                    await using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var output=new FileStream(part,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read,262144);
                    var buffer=ArrayPool<byte>.Shared.Rent(262144);var compare=ArrayPool<byte>.Shared.Rent(262144);
                    long received=partial?existing:0,network=0;var watch=Stopwatch.StartNew();long lastReport=0;bool recorded=false;
                    try
                    {
                        while(true)
                        {
                            int read;
                            using(var stall=CancellationTokenSource.CreateLinkedTokenSource(ct))
                            {
                                stall.CancelAfter(options.StallMilliseconds);
                                try{read=await input.ReadAtLeastAsync(buffer.AsMemory(0,262144),received==0?4:1,false,stall.Token).ConfigureAwait(false);}
                                catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new TimeoutException("传输长时间没有数据。");}
                                catch(IOException ex){throw new HttpRequestException("传输中断。",ex);}
                            }
                            if(read==0)break;
                            if(received+read>total.Value)throw new InvalidDataException("服务器数据超过预计大小，已停止写入。");
                            if(received==0&&(read<4||buffer[0]!=80||buffer[1]!=75||buffer[2]!=3||buffer[3]!=4))throw new InvalidDataException("下载响应不是有效的 ZIP 图库。");
                            int prefix=(int)Math.Min(read,Math.Max(0,existing-received));
                            output.Position=received;
                            if(prefix>0){output.ReadExactly(compare.AsSpan(0,prefix));if(!buffer.AsSpan(0,prefix).SequenceEqual(compare.AsSpan(0,prefix)))throw new InvalidDataException("服务器内容与已有下载片段不同，已保留旧文件。请改选下载位置。");}
                            output.Position=received+prefix;output.Write(buffer,prefix,read-prefix);received+=read;network+=read;
                            if(!recorded&&received>=existing&&etag!=null&&!etag.IsWeak){Files.Save(identityPath,new LibraryTransferIdentity(options.Source.AbsoluteUri,etag.ToString(),total.Value));recorded=true;}
                            if(watch.ElapsedMilliseconds-lastReport>=500)
                            {
                                output.Flush();var speed=(long)(network/Math.Max(.01,watch.Elapsed.TotalSeconds));
                                publish(current() with{stage="downloading",downloaded=Math.Max(existing,received),network_received=network,bytes_per_second=speed,eta_seconds=speed>0?(total.Value-received)/speed:0});lastReport=watch.ElapsedMilliseconds;
                            }
                        }
                        output.Flush(true);
                        if(received!=total.Value)throw new EndOfStreamException("连接提前结束，已保存下载片段。");
                    }
                    finally{output.Flush(true);ArrayPool<byte>.Shared.Return(buffer);ArrayPool<byte>.Shared.Return(compare);}
                }
                File.Move(part,archive);return;
            }
            catch(Exception ex) when((ex is HttpRequestException||ex is TimeoutException||ex is EndOfStreamException)&&attempt<options.Attempts)
            {
                publish(current() with{stage="retrying",downloaded=File.Exists(part)?new FileInfo(part).Length:0,error=null});
                await Task.Delay(Math.Min(60000,options.RetryDelayMilliseconds*attempt),ct).ConfigureAwait(false);
            }
        }
    }
    List<Face> Extract(string assets,string archive,CancellationToken ct,Action<LibraryProgress> publish,Func<LibraryProgress> current)
    {
        var destination=Path.Combine(assets,"NG_Regens_Library");SafeTarget(destination,".check");Directory.CreateDirectory(destination);
        using var zip=ZipFile.OpenRead(archive);
        var images=zip.Entries.Where(e=>!e.FullName.EndsWith('/')&&new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(e.FullName).ToLowerInvariant())&&Classify(e.FullName).Gender!="excluded").ToList();
        if(images.Count==0)throw new InvalidDataException("压缩包内没有 FM24 可用的头像图片。");
        var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long needed=1024L*1024*1024;
        foreach(var entry in images){var target=SafeTarget(destination,entry.FullName);if(!targets.Add(target))throw new InvalidDataException("图库包含重复或大小写冲突的图片路径。");if(!File.Exists(target)||new FileInfo(target).Length!=entry.Length)needed=checked(needed+entry.Length);}
        CheckSpace(assets,needed);
        SQLitePCL.Batteries_V2.Init();using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(assets,"native-index-checkpoint.sqlite")}.ToString());db.Open();
        using(var cmd=db.CreateCommand()){cmd.CommandText="CREATE TABLE IF NOT EXISTS images(name TEXT PRIMARY KEY,version TEXT,crc INTEGER,size INTEGER,mtime INTEGER,row_json TEXT)";cmd.ExecuteNonQuery();}
        var info=new FileInfo(archive);var version=$"native-v1:{info.Length}:{info.LastWriteTimeUtc.Ticks}";var rows=new List<Face>();var pixels=new HashSet<string>();
        publish(current() with{stage="indexing",total=info.Length,downloaded=info.Length,processed=0,total_images=images.Count,error=null});
        var checkpoint=db.BeginTransaction();
        try
        {
        foreach(var entry in images)
        {
            ct.ThrowIfCancellationRequested();var target=SafeTarget(destination,entry.FullName);Face? face=null;
            using(var cmd=db.CreateCommand())
            {
                cmd.Transaction=checkpoint;cmd.CommandText="SELECT row_json FROM images WHERE name=$n AND version=$v AND crc=$c AND size=$s AND mtime=$t";
                cmd.Parameters.AddWithValue("$n",entry.FullName);cmd.Parameters.AddWithValue("$v",version);cmd.Parameters.AddWithValue("$c",entry.Crc32);cmd.Parameters.AddWithValue("$s",entry.Length);cmd.Parameters.AddWithValue("$t",File.Exists(target)?File.GetLastWriteTimeUtc(target).Ticks:-1);
                if(cmd.ExecuteScalar() is string json&&File.Exists(target)&&new FileInfo(target).Length==entry.Length){try{face=JsonSerializer.Deserialize<Face>(json,Files.Json);if(face?.Path!=target||face.VisualHash==null)face=null;}catch(JsonException){ }}
            }
            if(face==null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);var temporary=SafeTarget(destination,entry.FullName+".new");
                try
                {
                    uint crc=0xffffffff;long written=0;
                    using(var input=entry.Open())using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write))
                    {
                        var buffer=new byte[65536];int n;while((n=input.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();written+=n;if(written>entry.Length)throw new InvalidDataException("图片解压大小超过声明值。");crc=LibraryCrc32.Append(crc,buffer.AsSpan(0,n));output.Write(buffer,0,n);}
                        output.Flush(true);
                    }
                    if(written!=entry.Length||(crc^0xffffffff)!=entry.Crc32)throw new InvalidDataException("图片 CRC 校验失败："+entry.FullName);
                    var hash=Digest(temporary);var visual=Catalog.PixelDigest(temporary);var c=Classify(entry.FullName);
                    // Reuse unchanged old images, without changing their timestamps.
                    if(!File.Exists(target)||Digest(target)!=hash)File.Move(temporary,target,true);else File.Delete(temporary);
                    face=new(target,c.Group,c.Kind,c.Gender,hash,visual);
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
                using var cmd=db.CreateCommand();cmd.Transaction=checkpoint;cmd.CommandText="INSERT OR REPLACE INTO images VALUES($n,$v,$c,$s,$t,$r)";
                cmd.Parameters.AddWithValue("$n",entry.FullName);cmd.Parameters.AddWithValue("$v",version);cmd.Parameters.AddWithValue("$c",entry.Crc32);cmd.Parameters.AddWithValue("$s",entry.Length);cmd.Parameters.AddWithValue("$t",File.GetLastWriteTimeUtc(target).Ticks);cmd.Parameters.AddWithValue("$r",JsonSerializer.Serialize(face,Files.Json));cmd.ExecuteNonQuery();
            }
            rows.Add(face);pixels.Add(face.UniqueKey);
            if(rows.Count%128==0||rows.Count==images.Count){checkpoint.Commit();checkpoint.Dispose();checkpoint=db.BeginTransaction();publish(current() with{stage="indexing",processed=rows.Count,total_images=images.Count,unique_images=pixels.Count});}
        }
            checkpoint.Commit();
        }
        finally{checkpoint.Dispose();}
        return rows;
    }
    static string Digest(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    public static (string Group,string Kind,string Gender) Classify(string name)
    {
        var dirs=name.Replace('\\','/').Split('/')[..^1];var lower=string.Join('/',dirs).ToLowerInvariant();var kind=lower.Contains("staff")?"职员":"球员";
        var gender=lower.Contains("female")||lower.Contains("women")||dirs.Any(d=>d.Equals("wf",StringComparison.OrdinalIgnoreCase))?"女":lower.Contains("male")||lower.Contains("men")||kind=="球员"?"男":"未知";
        var generic=new[]{"ng_regens","ngregens","players","player","staff","male","female","men","women","wf"};
        return(dirs.LastOrDefault(d=>!generic.Contains(d.ToLowerInvariant()))??"未分类",kind,kind=="球员"&&gender=="女"?"excluded":gender);
    }
    public static string SafeTarget(string root,string relative)
    {
        root=Path.GetFullPath(root);relative=relative.Replace('\\','/');
        if(Path.IsPathRooted(relative)||relative.Split('/').Any(p=>p==".."||p.Contains(':')||p.EndsWith(' ')||p.EndsWith('.')))throw new InvalidDataException("图库包含越界或无效路径："+relative);
        var target=Path.GetFullPath(Path.Combine(root,relative));
        if(!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("图库路径超出解压目录。");
        for(var path=target;path!=null&&path.Length>=root.Length;path=Path.GetDirectoryName(path))
            if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("图库目录中存在链接，请选择普通文件夹："+path);
        return target;
    }
}
public static class LibraryCrc32
{
    static readonly uint[] table=Build();
    static uint[] Build(){var t=new uint[256];for(uint i=0;i<256;i++){var c=i;for(int j=0;j<8;j++)c=(c&1)!=0?0xedb88320^(c>>1):c>>1;t[i]=c;}return t;}
    public static uint Append(uint crc,ReadOnlySpan<byte> data){foreach(var b in data)crc=table[(crc^b)&255]^(crc>>8);return crc;}
}
