using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace FM24FaceAssistant;
public partial class DownloadMonitor : Window
{
    readonly DispatcherTimer timer = new(){Interval=TimeSpan.FromSeconds(1)};
    readonly string assets;
    readonly string? programRoot;
    bool starting;
    readonly System.Collections.Generic.HashSet<string> loggedEvents=new(); string lastConnection=""; long lastNetwork; DateTime lastSample=DateTime.UtcNow; double recentSpeed;
    public DownloadMonitor(){InitializeComponent();programRoot=LibraryPaths.FindProgramRoot();var settings=Files.Read<Settings>(Path.Combine(Files.State,"settings.json"));assets=LibraryPaths.DownloadDirectory(programRoot,Files.State,settings?.LibraryDownloadPath);Directory.CreateDirectory(assets);timer.Tick+=(_,_)=>Refresh();Closed+=(_,_)=>timer.Stop();Refresh();timer.Start();}
    void Event(string key,string message){if(!loggedEvents.Add(key))return;Log.AppendText($"[{DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)):HH:mm:ss}] {message}\n");Log.ScrollToEnd();}
    void Refresh()
    {
        try{
            var path=Path.Combine(assets,"download-status.json");if(!File.Exists(path)){StageText.Text="等待下载任务";return;}
            using var document=JsonDocument.Parse(Files.ReadTextShared(path));var d=document.RootElement;
            long N(string key)=>d.TryGetProperty(key,out var v)&&v.TryGetInt64(out long n)?n:0;
            string S(string key)=>d.TryGetProperty(key,out var v)?v.ToString():"";
            long total=N("total"),saved=N("downloaded"),network=N("network_received");var stage=S("stage");var connection=S("pid")+":"+S("attempt");
            if(connection!=lastConnection){lastConnection=connection;lastNetwork=network;lastSample=DateTime.UtcNow;recentSpeed=N("bytes_per_second");}
            else if(network!=lastNetwork){var elapsed=(DateTime.UtcNow-lastSample).TotalSeconds;recentSpeed=Math.Max(0,network-lastNetwork)/Math.Max(0.1,elapsed);lastNetwork=network;lastSample=DateTime.UtcNow;}
            bool alive;try{using var p=Process.GetProcessById((int)N("pid"));alive=!p.HasExited;}catch{alive=false;}
            if(total>0){Overall.Value=100d*saved/total;Transfer.Value=100d*network/total;Percentage.Text=$"{Overall.Value:F1}%";SavedBytes.Text=$"已保留 {saved/1e9:F3} GB / {total/1e9:F3} GB";}
            Rate.Text=DateTime.UtcNow-lastSample>TimeSpan.FromSeconds(12)?"等待数据":$"{recentSpeed/1e6:F2} MB/s";
            Remaining.Text=N("eta_seconds")>0?$"约 {Math.Ceiling(N("eta_seconds")/60d):F0} 分钟":"—";Attempt.Text=$"#{N("attempt")}  {S("updated")}";
            var label=stage switch{"downloading"=>network<saved?"直连下载 · 正在重传并校验已有前缀":"直连下载 · 正在取得新数据","preparing"=>"准备图库 · 检查压缩包与可用空间","indexing"=>"校验、解压与去重索引","retrying"=>"等待重新连接","failed"=>N("processed")>0?"图库处理失败 · 可从断点重试":"任务失败 · 已保留下载文件","complete"=>"图库已就绪",_=>stage};
            if(!alive&&stage!="complete"&&stage!="failed")label=stage=="indexing"?"图库处理已停止 · 文件与校验断点已保留":"下载已停止 · 已保留下载文件";
            Recovery.IsEnabled=!starting&&!alive&&stage!="complete";
            RateLabel.Text="最近传输速度";RemainingLabel.Text="本次连接预计剩余";
            Footer.Text="仅加载男性球员、男性职员和女性职员素材。";
            Transfer.Visibility=Visibility.Visible;
            Overall.IsIndeterminate=false;
            StageText.Text=label;ConnectionText.Text=$"本次连接已传输 {network/1e9:F3} GB；有效进度以绿色进度条为准。";
            if(stage=="indexing"||N("processed")>0){Transfer.Visibility=Visibility.Hidden;ConnectionText.Text=alive?"压缩包已下载完成，正在校验图片、解压并建立去重索引。":"压缩包和已解压图片仍在；点击“继续处理”恢复，无需重新下载。";RateLabel.Text="下载状态";RemainingLabel.Text="图库任务";Rate.Text="下载已完成";Remaining.Text=alive?"处理中":"已停止";Attempt.Text=S("updated");Overall.Value=N("total_images")>0?100d*N("processed")/N("total_images"):0;Percentage.Text=$"{Overall.Value:F1}%";SavedBytes.Text=$"已处理 {N("processed"):N0} / {N("total_images"):N0} 张；唯一像素内容 {N("unique_images"):N0} 张";}
            if(stage=="preparing"){Transfer.Visibility=Visibility.Hidden;Overall.IsIndeterminate=alive;Percentage.Text="—";SavedBytes.Text="正在读取图片目录与校验断点";ConnectionText.Text="本地压缩包已下载完成。";RateLabel.Text="下载状态";Rate.Text="下载已完成";RemainingLabel.Text="图库任务";Remaining.Text=alive?"准备中":"已停止";Attempt.Text=S("updated");}
            if(!alive&&stage!="complete")Remaining.Text=stage=="failed"?"失败，可重试":"已停止";
            if(stage=="complete"){Transfer.Visibility=Visibility.Hidden;ConnectionText.Text="下载、图片校验、解压与索引已完成。";Rate.Text="下载已结束";Attempt.Text=S("updated");Overall.Value=100;Percentage.Text="100%";SavedBytes.Text=$"FM24 适用头像 {N("total_images"):N0} 张；唯一像素内容 {N("unique_pixels"):N0} 张";Remaining.Text="完成";Footer.Text="退出并重新打开头像助手，即可加载完整图库。";}
            Event(connection+":"+label,label);if(stage=="failed")Event(connection+":error",S("error"));
        }catch(Exception ex){StageText.Text="等待状态文件更新";Event("read-error",ex.Message);}
    }
    async void ResumeWorker(object sender,RoutedEventArgs e)
    {
        starting=true;Recovery.IsEnabled=false;
        try{
            if(programRoot==null)throw new IOException("未找到图库下载组件，请从完整程序目录启动。");
            var script=Path.Combine(programRoot,"start_library_worker.ps1");
            var info=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",script})info.ArgumentList.Add(arg);
            using var process=Process.Start(info)??throw new IOException("无法启动图库任务。");
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if(process.ExitCode!=0)throw new IOException(await error);
            Event("resume:"+DateTime.UtcNow.Ticks,(await output).Trim());
            await System.Threading.Tasks.Task.Delay(2500);
        }catch(Exception ex){Event("resume-error:"+DateTime.UtcNow.Ticks,"恢复失败："+ex.Message);}
        finally{starting=false;Refresh();}
    }
    void OpenFolder(object sender,RoutedEventArgs e){Directory.CreateDirectory(assets);Process.Start(new ProcessStartInfo(assets){UseShellExecute=true});}
    void CloseMonitor(object sender,RoutedEventArgs e)=>Close();
}
