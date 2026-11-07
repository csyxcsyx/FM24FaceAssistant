using FM24FaceAssistant;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class LibraryDownloadChecks
{
    sealed class Progress(Action<LibraryProgress> action):IProgress<LibraryProgress>{public void Report(LibraryProgress p)=>action(p);}
    public sealed record Response(byte[] Body,string ETag="\"fixture-v1\"",int Code=200,string? ContentRange=null,long? Length=null,int Delay=0);
    public sealed class Server:IDisposable
    {
        readonly TcpListener listener=new(IPAddress.Loopback,0);
        readonly CancellationTokenSource stop=new();
        readonly Func<string,Response> respond;
        public List<string> Requests {get;}=new();
        public Uri Url {get;}
        public Server(Func<string,Response> response)
        {
            respond=response;listener.Start();Url=new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/pack.zip");_=Loop();
        }
        async Task Loop()
        {
            try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);_=Serve(client);}}
            catch(Exception) when(stop.IsCancellationRequested){ }
        }
        async Task Serve(TcpClient client)
        {
            using(client)
            try
            {
                using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,4096,true);
                var request=new StringBuilder();string? line;while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync(stop.Token)))request.AppendLine(line);
                lock(Requests)Requests.Add(request.ToString());var r=respond(request.ToString());
                var header=$"HTTP/1.1 {r.Code} OK\r\nContent-Length: {r.Length??r.Body.Length}\r\nETag: {r.ETag}\r\nConnection: close\r\n"+(r.ContentRange==null?"":"Content-Range: "+r.ContentRange+"\r\n")+"\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header),stop.Token);
                for(int i=0;i<r.Body.Length;i+=8192){if(r.Delay>0)await Task.Delay(r.Delay,stop.Token);await stream.WriteAsync(r.Body.AsMemory(i,Math.Min(8192,r.Body.Length-i)),stop.Token);}
            }
            catch(Exception ex) when(ex is IOException||ex is OperationCanceledException||ex is ObjectDisposedException){ }
        }
        public void Dispose(){stop.Cancel();listener.Stop();}
    }
    public static byte[] Png(bool metadata=false,byte color=255)
    {
        var frame=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,color,255,0,0,color,255,0,0,color,255,0,0,color,255},8);
        var encoder=new PngBitmapEncoder();var meta=new BitmapMetadata("png");if(metadata)meta.SetQuery("/tEXt/{str=Description}","identical pixels");encoder.Frames.Add(BitmapFrame.Create(frame,null,meta,null));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    public static byte[] Zip(bool unsafePath=false,int many=0)
    {
        var red=Png();var same=Png(true);var blue=Png(false,40);
        using var buffer=new MemoryStream();
        using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true))
        {
            void Add(string path,byte[] data){using var s=zip.CreateEntry(path,CompressionLevel.NoCompression).Open();s.Write(data);}
            Add("NG_Regens/China/a.png",red);Add("NG_Regens/China/b.png",same);Add("NG_Regens/WF/China/f.png",blue);Add("NG_Regens/Staff/Female/Korea/c.png",blue);
            for(int i=0;i<many;i++)Add($"NG_Regens/China/extra{i}.png",red);
            if(unsafePath)Add("../escape.png",red);
        }
        return buffer.ToArray();
    }
    static string Assets(string root,string name){var p=Path.Combine(root,name,"下载目录 with spaces");Directory.CreateDirectory(p);return p;}
    static LibraryDownloadService Service(Server server,long bytes,Action<string,long>? space=null,int stall=2000)=>new(new(server.Url,bytes,2,0,stall),spaceCheck:space);
    static Task<LibraryDownloadResult> Run(LibraryDownloadService svc,string assets,IProgress<LibraryProgress>? progress=null,CancellationToken ct=default)=>svc.RunAsync(assets,Path.Combine(assets,"state;中文"),progress,ct);
    static async Task<bool> Fails(Func<Task> work){try{await work();return false;}catch(IOException){return true;}}
    public static void Run(Action<bool,string> check,string root)=>RunChecks(check,root).GetAwaiter().GetResult();
    static async Task RunChecks(Action<bool,string> check,string root)
    {
        check((LibraryCrc32.Append(0xffffffff,Encoding.ASCII.GetBytes("123456789"))^0xffffffff)==0xcbf43926,"ZIP CRC32 使用标准校验值");
        check(LibraryDownloadService.Classify("NG_Regens/WF/China/a.png").Gender=="excluded"&&LibraryDownloadService.Classify("NG_Regens/Staff/Female/Korea/a.png")==("Korea","职员","女"),"内置图库分类排除女性球员并保留女性职员");
        var data=Zip();using var server=new Server(_=>new(data));var assets=Assets(root,"native-full");
        var result=await Run(Service(server,data.Length),assets);
        var rows=Files.Read<List<Face>>(Path.Combine(assets,"state;中文","catalog.json"))!;
        check(result.Count==3&&rows.Select(f=>f.UniqueKey).Distinct().Count()==2&&rows.All(f=>File.Exists(f.Path)),"本地 HTTP 下载解压索引与像素去重完整成功");
        check(Files.Read<LibraryProgress>(Path.Combine(assets,"download-status.json"))?.stage=="complete","下载成功状态包含当前任务标识");
        var dynamicAssets=Assets(root,"native-current-size");
        await Run(Service(server,0),dynamicAssets);
        check(new FileInfo(Path.Combine(dynamicAssets,"NG_Regens_MainPack.zip")).Length==data.Length&&Files.Read<LibraryProgress>(Path.Combine(dynamicAssets,"download-status.json"))?.total==data.Length,"官方包大小从响应读取，不固定旧版本字节数");
        var dynamicPrefix=Assets(root,"native-dynamic-prefix");File.WriteAllBytes(Path.Combine(dynamicPrefix,"NG_Regens_MainPack.zip.part"),data[..50]);await Run(Service(server,0),dynamicPrefix);
        check(File.ReadAllBytes(Path.Combine(dynamicPrefix,"NG_Regens_MainPack.zip")).SequenceEqual(data),"动态大小与旧版无元数据片段兼容，服务器忽略 Range 仍完整续传");
        using(var dynamicRange=new Server(req=>req.Contains("Range: bytes=50-",StringComparison.OrdinalIgnoreCase)?new(data[50..],Code:206,ContentRange:$"bytes 50-{data.Length-1}/{data.Length}"):new(data)))
        {
            var a=Assets(root,"native-dynamic-range");File.WriteAllBytes(Path.Combine(a,"NG_Regens_MainPack.zip.part"),data[..50]);Files.Save(Path.Combine(a,"transfer-identity.json"),new LibraryTransferIdentity(dynamicRange.Url.AbsoluteUri,"\"fixture-v1\"",data.Length));await Run(Service(dynamicRange,0),a);
            check(dynamicRange.Requests.Any(r=>r.Contains("Range: bytes=50-")),"动态大小仍用强 ETag 与总长度确认 Range 响应");
        }
        using(var updatedServer=new Server(_=>new(Zip(many:3),ETag:"\"new-release\"")))
        {
            var a=Assets(root,"native-updated-release");File.WriteAllBytes(Path.Combine(a,"NG_Regens_MainPack.zip.part"),data[..4]);Files.Save(Path.Combine(a,"transfer-identity.json"),new LibraryTransferIdentity(updatedServer.Url.AbsoluteUri,"\"old-release\"",data.Length));
            check((await Run(Service(updatedServer,0),a)).Count==6,"官方发布新版时接受新总长度，通过前缀校验复用安全数据");
        }
        var previousState=Files.State;try{Files.State=Path.Combine(assets,"state;中文");check(FaceLibraryStore.Load(default,_=>{}).Count==3,"下载结束后可立即加载图库，支持含分号的用户目录");using var store=new Storage(Files.State);check(store.Profiles().Count==0,"用户数据库路径使用连接串转义");}finally{Files.State=previousState;}
        // Completed legacy images without an archive can be reused without any network.
        File.Delete(Path.Combine(assets,"NG_Regens_MainPack.zip"));var requestCount=server.Requests.Count;await Run(Service(server,data.Length),assets);
        check(server.Requests.Count==requestCount,"旧图库完整图片无需重新下载");
        var unproven=Assets(root,"native-unproven-library");var unprovenLibrary=Path.Combine(unproven,"NG_Regens_Library");Directory.CreateDirectory(unprovenLibrary);Files.Save(Path.Combine(unprovenLibrary,"faces.json"),rows);
        var beforeUnproven=server.Requests.Count;await Run(Service(server,0),unproven);
        check(server.Requests.Count>beforeUnproven,"仅存在图片清单但没有下载完成报告时仍会下载官方包");
        File.Delete(Path.Combine(unproven,"NG_Regens_MainPack.zip"));File.WriteAllText(Path.Combine(unproven,"library-report.json"),"{\"source\":true,\"total_images\":\"3\",\"archive_bytes\":\"999\"}");
        beforeUnproven=server.Requests.Count;await Run(Service(server,0),unproven);check(server.Requests.Count>beforeUnproven,"损坏或字段类型错误的完成报告不会阻止重新下载");
        var damaged=Path.Combine(assets,"NG_Regens_Library","NG_Regens","China","a.png");File.WriteAllText(damaged,"missing pixels");var beforeRepair=server.Requests.Count;await Run(Service(server,0),assets);
        check(server.Requests.Count>beforeRepair&&File.ReadAllBytes(damaged).SequenceEqual(Png()),"旧图库损坏且 ZIP 已删除时自动下载修复，不反复提示本地已就绪");
        assets=Assets(root,"native-prefix");var prefix=data[..50];File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip.part"),prefix);await Run(Service(server,data.Length),assets);
        check(File.ReadAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip")).SequenceEqual(data),"旧版片段通过前缀校验继续写入且不丢进度");
        assets=Assets(root,"native-changed");File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip.part"),new byte[50]);
        check(await Fails(async()=>await Run(Service(server,data.Length),assets))&&File.ReadAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip.part")).SequenceEqual(new byte[50]),"来源内容变化时不覆盖原片段");
        assets=Assets(root,"native-range");
        using(var rangeServer=new Server(req=>req.Contains("Range: bytes=50-",StringComparison.OrdinalIgnoreCase)?new(data[50..],Code:206,ContentRange:$"bytes 50-{data.Length-1}/{data.Length}"):new(data)))
        {
            File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip.part"),data[..50]);Files.Save(Path.Combine(assets,"transfer-identity.json"),new LibraryTransferIdentity(rangeServer.Url.AbsoluteUri,"\"fixture-v1\"",data.Length));
            await Run(Service(rangeServer,data.Length),assets);check(rangeServer.Requests.Any(r=>r.Contains("If-Range:")&&r.Contains("Range: bytes=50-")),"强 ETag 验证后执行真实 Range 续传");
        }
        assets=Assets(root,"native-range-ignored");File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip.part"),data[..50]);Files.Save(Path.Combine(assets,"transfer-identity.json"),new LibraryTransferIdentity(server.Url.AbsoluteUri,"\"fixture-v1\"",data.Length));await Run(Service(server,data.Length),assets);
        check(File.ReadAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip")).SequenceEqual(data),"服务器忽略 Range 时回退前缀校验，不重复追加");
        assets=Assets(root,"native-range-wrong-source");
        using(var changed=new Server(_=>new(data[50..],ETag:"\"different-version\"",Code:206,ContentRange:$"bytes 50-{data.Length-1}/{data.Length}")))
        {
            File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip.part"),data[..50]);Files.Save(Path.Combine(assets,"transfer-identity.json"),new LibraryTransferIdentity(changed.Url.AbsoluteUri,"\"fixture-v1\"",data.Length));
            check(await Fails(async()=>await Run(Service(changed,data.Length),assets))&&File.ReadAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip.part")).SequenceEqual(data[..50]),"Range 响应版本不符时拒绝追加，旧片段保持原样");
        }
        assets=Assets(root,"native-jpeg");
        var jpeg=new JpegBitmapEncoder();jpeg.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgr24,null,new byte[12],6)));using var jpg=new MemoryStream();jpeg.Save(jpg);
        using(var jpegZip=new MemoryStream())
        {
            using(var zip=new ZipArchive(jpegZip,ZipArchiveMode.Create,true)){using var entry=zip.CreateEntry("NG_Regens/Japan/face.jpeg").Open();entry.Write(jpg.ToArray());}
            var jpegData=jpegZip.ToArray();File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip"),jpegData);var jpegResult=await Run(Service(server,jpegData.Length),assets);
            check(jpegResult.Count==1,"内置系统图片解码支持 JPEG，无需 Pillow");
        }
        assets=Assets(root,"native-cutoff");int calls=0;
        using(var retryServer=new Server(_=>Interlocked.Increment(ref calls)==1?new(data[..50],Length:data.Length):new(data)))
        {await Run(Service(retryServer,data.Length),assets);check(calls==2,"下载连接提前断开后自动重试并接续");}
        assets=Assets(root,"native-unsafe");var bad=Zip(true);File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip"),bad);
        Files.Save(Path.Combine(assets,"state;中文","catalog.json"),new[]{new Face("untouched","x","球员","男","hash")});
        check(await Fails(async()=>await Run(Service(server,bad.Length),assets))&&!File.Exists(Path.Combine(assets,"escape.png"))&&Files.Read<List<Face>>(Path.Combine(assets,"state;中文","catalog.json"))![0].Path=="untouched","越界 ZIP 被拒绝且旧索引不被覆盖");
        assets=Assets(root,"native-crc");var corrupt=(byte[])data.Clone();int at=Find(corrupt,Png());corrupt[at+30]^=1;File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip"),corrupt);
        check(await Fails(async()=>await Run(Service(server,corrupt.Length),assets))&&!File.Exists(Path.Combine(assets,"state;中文","catalog.json")),"损坏图片 CRC 校验失败时不提交索引");
        assets=Assets(root,"native-checkpoint");var many=Zip(many:128);File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip"),many);
        using(var cancel=new CancellationTokenSource())
        {
            bool stopped=false;try{await Run(Service(server,many.Length),assets,new Progress(p=>{if(p.stage=="indexing"&&p.processed==128)cancel.Cancel();}),cancel.Token);}catch(OperationCanceledException){stopped=true;}
            check(stopped&&Files.Read<LibraryProgress>(Path.Combine(assets,"download-status.json"))?.stage=="cancelled","索引中取消保存检查点并写入已取消状态");
        }
        var kept=Path.Combine(assets,"NG_Regens_Library","NG_Regens","China","a.png");var stamp=File.GetLastWriteTimeUtc(kept);await Run(Service(server,many.Length),assets);
        check(File.GetLastWriteTimeUtc(kept)==stamp,"重新启动复用已校验且未变的图片检查点");
        File.WriteAllText(kept,"damaged");await Run(Service(server,many.Length),assets);check(File.ReadAllBytes(kept).SequenceEqual(Png()),"检查点中的已修改图片自动重新解压修复");
        assets=Assets(root,"native-space");long required=0;
        check(await Fails(async()=>await Run(Service(server,5_000_000_000,(_,size)=>{required=size;throw new IOException("模拟空间不足");}),assets))&&required>int.MaxValue,"大于 4 GB 的磁盘空间计算使用 64 位数值");
        using(var bigServer=new Server(_=>new(data,Length:5_000_000_000)))
        {
            long dynamicRequired=0;check(await Fails(async()=>await Run(Service(bigServer,0,(_,size)=>{if(size>int.MaxValue){dynamicRequired=size;throw new IOException("模拟新包空间不足");}}),Assets(root,"native-dynamic-space")))&&dynamicRequired>5_000_000_000,"从服务器读取的大于 4 GB 长度也在写入前检查磁盘空间");
        }
        assets=Assets(root,"native-locked");using(var lease=new FileStream(Path.Combine(assets,"download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite))
        {lease.Lock(0,1);check(await Fails(async()=>await Run(Service(server,data.Length),assets)),"旧版下载字节锁存在时拒绝并行写入");lease.Unlock(0,1);}
        assets=Assets(root,"native-stall");using(var slow=new Server(_=>new(data,Delay:300)))check(await Fails(async()=>await Run(Service(slow,data.Length,stall:50),assets)),"接收数据停滞超时可重试并显示错误");
        assets=Assets(root,"native-cancel");using(var slow=new Server(_=>new(data,Delay:1000)))using(var cancellation=new CancellationTokenSource(100))
        {bool stopped=false;try{await Run(Service(slow,data.Length),assets,ct:cancellation.Token);}catch(OperationCanceledException){stopped=true;}check(stopped&&Files.Read<LibraryProgress>(Path.Combine(assets,"download-status.json"))?.stage=="cancelled","等待网络期间退出可及时取消并保存任务状态");}
        assets=Assets(root,"native-single-task");using(var slow=new Server(_=>new(data,Delay:1000)))using(var cancellation=new CancellationTokenSource())
        {
            var service=Service(slow,data.Length);var first=Run(service,assets,ct:cancellation.Token);bool blocked=false;
            try{await Run(service,assets);}catch(InvalidOperationException){blocked=true;}
            cancellation.Cancel();try{await first;}catch(OperationCanceledException){}
            check(blocked&&!service.IsRunning,"重复点击共用单任务保护，取消后释放运行状态");
        }
        var unusable=Path.Combine(root,"native-directory-is-file");File.WriteAllText(unusable,"fixture");
        check(await Fails(async()=>await Run(Service(server,data.Length),unusable)),"下载位置被同名文件占用时显示目录错误");
        foreach(var unsafeName in new[]{"/absolute.png","../escape.png","C:/escape.png","sub/../escape.png","sub/name. /a.png"})
        {bool rejected=false;try{LibraryDownloadService.SafeTarget(root,unsafeName);}catch(InvalidDataException){rejected=true;}check(rejected,"解压路径拒绝 "+unsafeName);}
        var link=Path.Combine(root,"native-link");var outside=Path.Combine(root,"native-outside");Directory.CreateDirectory(outside);
        try{JunctionFixture.Create(link,outside);bool rejected=false;try{LibraryDownloadService.SafeTarget(link,"face.png");}catch(InvalidDataException){rejected=true;}check(rejected,"解压拒绝经目录链接跳出目标位置");}
        finally{if(Directory.Exists(link))Directory.Delete(link);}
    }
    static int Find(byte[] bytes,byte[] pattern){for(int i=0;i<=bytes.Length-pattern.Length;i++)if(bytes.AsSpan(i,pattern.Length).SequenceEqual(pattern))return i;throw new Exception("PNG fixture not found");}
}
