using System;
using System.IO;
using System.IO.Compression;
using System.Text;
namespace FM24FaceAssistant;

public static class SnapshotStore
{
    public static string Put(string state,string text,System.Threading.CancellationToken ct=default)
    {
        var folder=Path.Combine(state,"snapshot-blobs");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,Files.Hash(text)+".gz");
        if(File.Exists(path)){if(Read(path,ct)!=text)throw new IOException("恢复快照校验失败。");return path;}
        var temp=path+".tmp";
        try{using(var file=File.Create(temp))using(var gzip=new GZipStream(file,CompressionLevel.Optimal))using(var writer=new StreamWriter(gzip,new UTF8Encoding(false)))for(int offset=0;offset<text.Length;offset+=32768){ct.ThrowIfCancellationRequested();writer.Write(text.AsSpan(offset,Math.Min(32768,text.Length-offset)));}
        ct.ThrowIfCancellationRequested();File.Move(temp,path,true);return path;}catch{File.Delete(temp);throw;}
    }
    public static string Read(string path,System.Threading.CancellationToken ct=default)
    {
        if(!path.EndsWith(".gz",StringComparison.OrdinalIgnoreCase))return Files.ReadTextShared(path);
        try{using var file=File.OpenRead(path);using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);var buffer=new char[32768];var builder=new StringBuilder();int count;while((count=reader.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();builder.Append(buffer,0,count);}var text=builder.ToString();
        if(Path.GetFileNameWithoutExtension(path)!=Files.Hash(text))throw new IOException("恢复快照校验失败："+path);return text;}catch(InvalidDataException ex){throw new IOException("恢复快照损坏："+path,ex);}
    }
}
