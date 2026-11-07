using FM24FaceAssistant;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;

// Explicit opt-in integration check. All output is confined to the supplied sandbox.
static class OfficialDownloadProbe
{
    sealed class Progress(Action<LibraryProgress> action):IProgress<LibraryProgress>{public void Report(LibraryProgress value)=>action(value);}
    public static int Run(string sandbox)
    {
        sandbox=Path.GetFullPath(sandbox);Directory.CreateDirectory(sandbox);
        var assets=Path.Combine(sandbox,"下载 material");var state=Path.Combine(sandbox,"用户数据;state");
        var service=new LibraryDownloadService();bool cancelled=false;var watch=Stopwatch.StartNew();long last=-10000;
        using(var stop=new CancellationTokenSource())
        {
            if(!File.Exists(Path.Combine(assets,"NG_Regens_MainPack.zip")))
            {
                try{service.RunAsync(assets,state,new Progress(p=>{if(p.stage=="downloading"&&p.downloaded>=8*1024*1024)stop.Cancel();}),stop.Token).GetAwaiter().GetResult();}
                catch(OperationCanceledException){cancelled=true;}
                if(!cancelled)throw new Exception("Expected a real network cancellation checkpoint.");
                Console.WriteLine("LIVE cancellation saved; resuming the official stream.");
            }
        }
        var result=service.RunAsync(assets,state,new Progress(p=>{
            if(watch.ElapsedMilliseconds-last>=10000||p.stage=="complete"){
                Console.WriteLine(JsonSerializer.Serialize(new{p.stage,p.downloaded,p.total,p.processed,p.total_images,elapsed_s=watch.Elapsed.TotalSeconds}));last=watch.ElapsedMilliseconds;
            }
        }),default).GetAwaiter().GetResult();
        var original=Files.State;
        try
        {
            Files.State=state;var faces=FaceLibraryStore.Load(default,_=>{},result.Library);
            if(faces.Count<100000||faces.Any(f=>!File.Exists(f.Path)))throw new Exception("Official catalog is incomplete.");
            foreach(var f in faces.Where((_,i)=>i%5000==0).Append(faces[^1]))if(Catalog.PixelDigest(f.Path)!=f.VisualHash)throw new Exception("Pixel cache differs from decoded official image.");
            var persons=new[]{new Person(910000001,"China Test","球员","男","China","fixture","2000:1",true),new Person(910000002,"Germany Test","球员","男","Germany","fixture","2000:1",true),new Person(910000003,"Nigeria Test","球员","男","Nigeria","fixture","2000:1",true),new Person(910000004,"Brazil Test","球员","男","Brazil","fixture","2000:1",true),new Person(910000005,"Male Staff","职员","男","China","fixture","2000:1",false),new Person(910000006,"Female Staff","职员","女","Korea","fixture","2000:1",false)};
            var assignments=new Matcher().Assign(persons,faces,new(),Array.Empty<Mapping>(),out var missing);
            if(assignments.Count!=persons.Length||missing.Count!=0)throw new Exception("Downloaded images failed geographic/gender matching: "+string.Join(";",missing));
            var graphics=Path.Combine(sandbox,"测试FM资料","graphics");new PatchWriter(state).Apply(graphics,"live-official",assignments,new());
            var records=XDocument.Load(Path.Combine(graphics,"FM24FaceAssistant","config.xml")).Descendants("record").Where(e=>e.Attribute("to")!=null).ToArray();
            if(records.Length!=persons.Length*2)throw new Exception("Portrait/icon config output incomplete.");
            foreach(var r in records){var from=(string)r.Attribute("from")!;if(!File.Exists(Path.Combine(graphics,"FM24FaceAssistant",from+".png")))throw new Exception("Output portrait missing.");}
            using(var store=new Storage(state)){store.Save("live-official",assignments);if(store.Load("live-official").Count!=persons.Length)throw new Exception("Assignment persistence failed.");}
            var report=new{Passed=true,CancelledAndResumed=cancelled,ArchiveBytes=new FileInfo(Path.Combine(assets,"NG_Regens_MainPack.zip")).Length,Faces=faces.Count,UniquePixels=faces.Select(f=>f.UniqueKey).Distinct().Count(),UnknownGroups=faces.Where(f=>!RegionResolver.IsKnownGroup(f.Group)).Select(f=>f.Group).Distinct().ToArray(),Matched=assignments.Count,ConfigRecords=records.Length,PeakMiB=Process.GetCurrentProcess().PeakWorkingSet64/1024/1024,ElapsedSeconds=watch.Elapsed.TotalSeconds,Sandbox=sandbox};
            Files.Save(Path.Combine(sandbox,"verification-result.json"),report);Console.WriteLine(JsonSerializer.Serialize(report));return 0;
        }
        finally{Files.State=original;}
    }
}
