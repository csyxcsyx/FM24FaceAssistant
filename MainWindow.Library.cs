using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FM24FaceAssistant;
public partial class MainWindow
{
    readonly GameLifetimeGate gameLifetime=new();
    bool CheckGameLifetime()
    {
        var games=Process.GetProcessesByName("fm");bool running=games.Length>0;foreach(var game in games)game.Dispose();
        if(!gameLifetime.Observe(running))return false;
        exitAfterTask=true;
        if(cancel!=null){if(!committing)cancel.Cancel();}else Close();
        return true;
    }
    string LibraryRoot()
    {
        return LibraryPaths.FindProgramRoot()??throw new IOException("缺少图库下载组件 download_ngregens.py，请使用完整程序目录。");
    }
    async void DownloadLibrary(object sender,RoutedEventArgs e)=>await Run(async ct=>
    {
        if(faces.Count>0&&File.Exists(faces[0].Path)){Report($"本地图库已就绪，共 {faces.Count:N0} 张，无需重复下载。");return;}
        var root=LibraryRoot();var assets=LibraryPaths.DownloadDirectory(root,Files.State,settings.LibraryDownloadPath);Directory.CreateDirectory(assets);var statusPath=Path.Combine(assets,"download-status.json");
        var python=await Task.Run(()=>PythonRuntime.Find(),ct);
        var start=python.Start(Path.Combine(root,"download_ngregens.py"),root);
        start.ArgumentList.Add("--managed");start.ArgumentList.Add("--state-dir");start.ArgumentList.Add(Files.State);start.ArgumentList.Add("--assets-dir");start.ArgumentList.Add(assets);
        start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        using var worker=Process.Start(start)??throw new IOException("无法启动 Python 图库下载组件。");
        var output=worker.StandardOutput.ReadToEndAsync();var errorOutput=worker.StandardError.ReadToEndAsync();
        try
        {
            while(!worker.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                if(File.Exists(statusPath))
                {
                    using var doc=JsonDocument.Parse(Files.ReadTextShared(statusPath));var data=doc.RootElement;
                    long Number(string key)=>data.TryGetProperty(key,out var n)&&n.TryGetInt64(out var value)?value:0;
                    var stage=data.GetProperty("stage").GetString();
                    // Ignore a status snapshot left by a previous worker.
                    if(Number("pid")==worker.Id){var indexing=stage=="indexing";var total=Number(indexing?"total_images":"total");var done=Number(indexing?"processed":"downloaded");taskProgress?.Report(new(indexing?"校验图片并建立索引":stage=="preparing"?"检查压缩包":"下载 NG_Regens 图库",done,total>0?total:null,indexing?$"{done:N0} 张":$"{done/1_000_000d:F0} MB · {Number("bytes_per_second")/1_000_000d:F1} MB/s"));LibraryDownloadStatus.Text=indexing?$"已处理 {done:N0} / {total:N0} 张":$"已下载 {done/1_000_000d:F0} / {total/1_000_000d:F0} MB";}
                }
                await Task.Delay(500,ct);
            }
            if(!File.Exists(statusPath)){var detail=(await errorOutput).Trim();if(detail.Length==0)detail=(await output).Trim();throw new IOException(worker.ExitCode!=0?$"图库下载器未能写入状态文件（退出码 {worker.ExitCode}）。{(detail.Length>0?Environment.NewLine+detail:" 请检查数据目录权限。")}":"图库下载器已退出，但没有生成状态文件；请检查数据目录权限后重试。");}
            using(var doc=JsonDocument.Parse(Files.ReadTextShared(statusPath))){var data=doc.RootElement;if(worker.ExitCode!=0||data.GetProperty("stage").GetString()!="complete")throw new IOException(data.TryGetProperty("error",out var error)?error.GetString():"下载未完成，可点击接续下载。");}
            faces=await Task.Run(()=>FaceLibraryStore.Load(ct,t=>taskProgress?.Report(new(t,0,null))),ct);
            settings.Pack=Path.Combine(assets,"NG_Regens_Library");PackPath.Text=settings.Pack;Persist();CanonicalizeAssignments();recommended=null;UpdateGallery();UpdateDownloadStatus();Report($"图库已就绪，共 {faces.Count:N0} 张。");
        }
        finally{if(!worker.HasExited){await Task.Run(()=>worker.Kill(true));await worker.WaitForExitAsync();} }
    });
}
