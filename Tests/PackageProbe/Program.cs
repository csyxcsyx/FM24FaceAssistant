using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class Program
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if(args.Length!=2)throw new Exception("Expected extracted release folder and writable sandbox.");
            var package=Path.GetFullPath(args[0]);var sandbox=Path.GetFullPath(args[1]);Directory.CreateDirectory(sandbox);
            AssemblyLoadContext.Default.Resolving+=(_,name)=>File.Exists(Path.Combine(package,name.Name+".dll"))?AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(package,name.Name+".dll")):null;
            var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(package,"FM24FaceAssistant.dll"));
            using(var tray=(IDisposable)assembly.GetType("FM24FaceAssistant.AppIconAssets")!.GetMethod("CreateTrayIcon")!.Invoke(null,null)!){}
            var bmp=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var png=new MemoryStream();encoder.Save(png);
            using var buffer=new MemoryStream();using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true)){using var output=zip.CreateEntry("NG_Regens/China/face.png").Open();output.Write(png.ToArray());}
            var bytes=buffer.ToArray();using var stop=new CancellationTokenSource();var server=new TcpListener(IPAddress.Loopback,0);server.Start();
            var url=new Uri($"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/pack.zip");
            var network=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){using var client=await server.AcceptTcpClientAsync(stop.Token);using var stream=client.GetStream();using var reader=new StreamReader(stream,leaveOpen:true);while(!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))){}await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"),stop.Token);await stream.WriteAsync(bytes,stop.Token);}}catch(OperationCanceledException){}});
            try
            {
                var assets=Path.Combine(sandbox,"素材 downloads");var state=Path.Combine(sandbox,"用户 data;缓存");Directory.CreateDirectory(assets);
                // Validate legacy prefix reuse, then immediate cache loading from the actual ZIP payload.
                File.WriteAllBytes(Path.Combine(assets,"NG_Regens_MainPack.zip.part"),bytes[..20]);
                var options=Activator.CreateInstance(assembly.GetType("FM24FaceAssistant.LibraryDownloadOptions")!,new object[]{url,(long)bytes.Length,2,0,5000});
                var serviceType=assembly.GetType("FM24FaceAssistant.LibraryDownloadService")!;var service=Activator.CreateInstance(serviceType,new object?[]{options,null,null});
                ((Task)serviceType.GetMethod("RunAsync")!.Invoke(service,new object?[]{assets,state,null,CancellationToken.None})!).GetAwaiter().GetResult();
                var files=assembly.GetType("FM24FaceAssistant.Files")!;files.GetField("State")!.SetValue(null,state);
                var store=assembly.GetType("FM24FaceAssistant.FaceLibraryStore")!;var rows=(System.Collections.ICollection)store.GetMethod("Load")!.Invoke(null,new object?[]{CancellationToken.None,(Action<string>)(_=>{}),null})!;
                if(rows.Count!=1||!File.Exists(Path.Combine(state,"library.db")))throw new Exception("Published catalog did not load.");
                using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(assets,"download-status.json")));if(doc.RootElement.GetProperty("stage").GetString()!="complete")throw new Exception("Download not complete.");
                Console.WriteLine(JsonSerializer.Serialize(new{Passed=true,Assembly=assembly.Location,State=state,Faces=rows.Count,PathEnvironment=Environment.GetEnvironmentVariable("PATH"),ExternalToolsRequired=false}));return 0;
            }
            finally{stop.Cancel();server.Stop();network.GetAwaiter().GetResult();}
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
