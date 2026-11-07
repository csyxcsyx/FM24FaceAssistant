using FM24FaceAssistant;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

static class LibraryIndexBenchmark
{
    public static int Run(string sandbox)
    {
        sandbox=Path.GetFullPath(sandbox);var original=Path.Combine(sandbox,"下载 material","NG_Regens_MainPack.zip");
        var run=Guid.NewGuid().ToString("N");var fixture=Path.Combine(sandbox,"index-benchmark-"+run+".zip");
        using(var source=ZipFile.OpenRead(original))using(var output=ZipFile.Open(fixture,ZipArchiveMode.Create))
            foreach(var entry in source.Entries.Where(e=>e.FullName.EndsWith(".png",StringComparison.OrdinalIgnoreCase)&&LibraryDownloadService.Classify(e.FullName).Gender!="excluded").Take(1000))
            {using var input=entry.Open();using var target=output.CreateEntry(entry.FullName,CompressionLevel.NoCompression).Open();input.CopyTo(target);}
        var results=new List<object>();
        foreach(int workers in new[]{1,4})
        {
            var dir=Path.Combine(sandbox,"index-benchmark-"+workers+"-"+run);Directory.CreateDirectory(dir);File.Copy(fixture,Path.Combine(dir,"NG_Regens_MainPack.zip"),true);
            var service=new LibraryDownloadService(new(new Uri("http://127.0.0.1/unused"),new FileInfo(fixture).Length){ImageWorkers=workers});
            var watch=Stopwatch.StartNew();var result=service.RunAsync(dir,Path.Combine(dir,"state"),null,default).GetAwaiter().GetResult();
            if(result.Count!=1000)throw new Exception("Benchmark image count mismatch.");results.Add(new{Workers=workers,Images=result.Count,Seconds=watch.Elapsed.TotalSeconds});
        }
        Console.WriteLine(JsonSerializer.Serialize(results));return 0;
    }
}
