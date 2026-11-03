using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
namespace FM24FaceAssistant;

public record SkinBundleManifest(string Sha256,int Files,long Bytes);
public static class SkinPackage
{
    public const string Name="FLAYUS skin 24 YUXIANde";
    public const string ArchiveName=Name+".zip";
    public static string DefaultPath(string documents)=>Path.Combine(documents,"skins",Name);
    public static string? BundlePath()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent){var file=Path.Combine(dir.FullName,"Bundled",ArchiveName);if(File.Exists(file))return file;}
        return null;
    }
    public static bool IsInstalled(string? folder)
    {
        if(string.IsNullOrWhiteSpace(folder))return false;
        try{
            if(!Directory.Exists(Path.Combine(folder,"panels"))||!File.Exists(Path.Combine(folder,"panels","human","inbox item.xml")))return false;
            var doc=PatchWriter.Parse(File.ReadAllText(Path.Combine(folder,"skin_config.xml")));
            return doc.Descendants().Any(e=>((string?)e.Attribute("id") is "name" or "skin_name")&&string.Equals((string?)e.Attribute("value"),Name,StringComparison.OrdinalIgnoreCase));
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException){return false;}
    }
    public static string? FindInstalled(string documents,string? configured)
    {
        if(IsInstalled(configured))return Path.GetFullPath(configured!);
        var preferred=DefaultPath(documents);if(IsInstalled(preferred))return preferred;
        var skins=Path.Combine(documents,"skins");
        try{return Directory.Exists(skins)?Directory.EnumerateDirectories(skins).FirstOrDefault(IsInstalled):null;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return null;}
    }
    public static string Install(string archive,string destination,CancellationToken ct,IProgress<TaskProgress>? progress=null)
    {
        var target=Path.GetFullPath(destination.Trim());
        if(string.IsNullOrWhiteSpace(Path.GetFileName(target))||Path.GetDirectoryName(target) is not string parent)throw new IOException("请选择皮肤文件夹，不能安装到磁盘根目录。");
        if(IsInstalled(target)){progress?.Report(new("已找到现有皮肤",1,1));return target;}
        EnsureEmpty(target);
        var manifest=Files.Read<SkinBundleManifest>(Path.ChangeExtension(archive,".json"))??throw new IOException("随附皮肤清单缺失，请保留完整的程序目录。");
        if(manifest.Files<=0||manifest.Bytes<=0)throw new IOException("皮肤清单无效。");
        using(var stream=File.OpenRead(archive))using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256)){
            var buffer=new byte[128*1024];long done=0;int read;
            while((read=stream.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();hash.AppendData(buffer,0,read);done+=read;progress?.Report(new("校验安装包",done,stream.Length));}
            if(!Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("皮肤安装包校验失败，请重新获取完整程序。");
        }
        ct.ThrowIfCancellationRequested();Directory.CreateDirectory(parent);
        var stage=Path.Combine(parent,".fm24-skin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{
            using var zip=ZipFile.OpenRead(archive);long bytes=0;int files=0;
            foreach(var entry in zip.Entries){
                ct.ThrowIfCancellationRequested();var name=entry.FullName.Replace('\\','/');
                if(!name.StartsWith(Name+"/",StringComparison.Ordinal)||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("皮肤安装包包含不支持的路径。");
                var relative=name[(Name.Length+1)..];if(relative.Length==0)continue;
                if(relative.Contains(':')||relative.Split('/').Any(s=>s is "." or ".."||s.TrimEnd(' ','.')!=s))throw new IOException("皮肤安装包路径无效。");
                var output=Path.GetFullPath(Path.Combine(stage,relative.Replace('/',Path.DirectorySeparatorChar)));
                if(!output.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("皮肤安装包路径越界。");
                if(name.EndsWith('/')){Directory.CreateDirectory(output);continue;}
                bytes=checked(bytes+entry.Length);if(bytes>manifest.Bytes||++files>manifest.Files)throw new IOException("皮肤安装包内容与清单不符。");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using(var input=entry.Open())using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    var buffer=new byte[128*1024];int count;long written=0;
                    while((count=input.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();written+=count;if(written>entry.Length)throw new IOException("皮肤文件大小异常。");file.Write(buffer,0,count);}
                    if(written!=entry.Length)throw new IOException("皮肤文件未完整解压。");
                }
                progress?.Report(new("解压皮肤",files,manifest.Files,relative));
            }
            if(files!=manifest.Files||bytes!=manifest.Bytes||!IsInstalled(stage))throw new IOException("皮肤文件不完整，未安装。");
            ct.ThrowIfCancellationRequested();EnsureEmpty(target);progress?.Report(new("正在保存皮肤",0,1,null,false));
            if(Directory.Exists(target))Directory.Delete(target,false);
            Directory.Move(stage,target);progress?.Report(new("皮肤已安装",1,1,null,false));return target;
        }finally{
            var full=Path.GetFullPath(stage);if(full.StartsWith(Path.GetFullPath(parent)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith(".fm24-skin-",StringComparison.Ordinal)&&Directory.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)==0)Directory.Delete(full,true);
        }
    }
    static void EnsureEmpty(string target)
    {
        if(File.Exists(target)||Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any())throw new IOException("此位置已有文件。请选择空目录，或使用“选择已有皮肤”；不会覆盖原文件。");
    }
}
