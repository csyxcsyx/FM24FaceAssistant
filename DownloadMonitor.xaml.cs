using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
namespace FM24FaceAssistant;
public partial class DownloadMonitor : Window
{
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    MainWindow? Assistant=>Owner as MainWindow;
    string Assets=>Assistant?.LibraryDownloadDirectory??LibraryPaths.DownloadDirectory(LibraryPaths.FindProgramRoot(),Files.State,Files.Read<Settings>(Path.Combine(Files.State,"settings.json"))?.LibraryDownloadPath);
    readonly System.Collections.Generic.HashSet<string> events=new();
    public DownloadMonitor(){InitializeComponent();timer.Tick+=(_,_)=>Refresh();Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>timer.Stop();timer.Start();}
    void Event(string key,string text){if(events.Add(key)){Log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");Log.ScrollToEnd();}}
    void Refresh()
    {
        try
        {
            var path=Path.Combine(Assets,"download-status.json");
            Recovery.IsEnabled=Assistant!=null&&!Assistant.IsLibraryDownloading;
            if(!File.Exists(path)){StageText.Text="尚未开始下载";ConnectionText.Text="点击继续处理即可下载，无需安装其他环境。";return;}
            using var doc=JsonDocument.Parse(Files.ReadTextShared(path));var d=doc.RootElement;
            long N(string key)=>d.TryGetProperty(key,out var n)&&n.TryGetInt64(out var value)?value:0;
            string S(string key)=>d.TryGetProperty(key,out var v)?v.ToString():"";
            var stage=S("stage");var alive=Assistant?.IsLibraryDownloading==true&&Assistant.LibraryTaskId==S("task_id");
            bool loaded=Assistant?.LoadedLibraryFaceCount>0;
            bool indexing=stage=="indexing";long total=N(indexing?"total_images":"total"),done=N(indexing?"processed":"downloaded");
            Overall.IsIndeterminate=alive&&stage=="preparing";Overall.Value=total>0?Math.Clamp(100d*done/total,0,100):0;
            Overall.IsIndeterminate=alive&&total<=0;
            Percentage.Text=total>0?$"{Overall.Value:F1}%":"—";
            SavedBytes.Text=indexing?$"已处理 {done:N0} / {total:N0} 张":total>0?$"已保存 {done/1e9:F2} / {total/1e9:F2} GB":"正在获取官方图库大小…";
            Rate.Text=alive&&!indexing?$"{N("bytes_per_second")/1e6:F2} MB/s":"—";
            Remaining.Text=alive&&N("eta_seconds")>0?$"约 {Math.Ceiling(N("eta_seconds")/60d):F0} 分钟":"—";Attempt.Text=S("updated");
            Transfer.Visibility=Visibility.Collapsed;
            StageText.Text=stage switch{"complete"=>loaded?"图库已就绪":"已有完成记录，待校验加载","failed"=>"处理失败，可重试","cancelled"=>"已保存进度，可继续",_ when !alive=>"任务已停止，可继续","preparing"=>"检查本地图库与空间","retrying"=>"连接中断，正在重试","indexing"=>"校验图片并建立索引",_=>"正在下载图库"};
            ConnectionText.Text=indexing?"正在解压、校验并建立图片索引。":stage=="complete"?(loaded?"图库已加载，可返回助手直接使用。":"点击继续处理，校验并加载头像；缺少或损坏的图片会重新下载修复。") :"关闭助手会保存进度；下次点击继续处理即可接续。";
            if(stage=="complete"){Overall.Value=loaded?100:0;Percentage.Text=loaded?"100%":"待校验";SavedBytes.Text=loaded?$"已加载头像 {Assistant!.LoadedLibraryFaceCount:N0} 张":$"完成记录 {N("total_images"):N0} 张；尚未加载有效头像";}
            Footer.Text="原目录的下载文件不会因更换位置而删除。";
            Event(S("task_id")+":"+StageText.Text,StageText.Text);if(stage=="failed")Event(S("task_id")+":error",S("error"));
        }
        catch(Exception ex){StageText.Text="状态暂不可读，可查看日志";Event("read:"+ex.Message,ex.Message);}
    }
    async void ResumeWorker(object sender,RoutedEventArgs e){if(Assistant!=null)await Assistant.StartLibraryDownload();Refresh();}
    void CancelDownload(object sender,RoutedEventArgs e){Assistant?.CancelLibraryDownload();Refresh();}
    void ChooseLocation(object sender,RoutedEventArgs e){Assistant?.SelectLibraryDownloadLocation();Refresh();}
    void OpenLogs(object sender,RoutedEventArgs e){try{Directory.CreateDirectory(Assets);Process.Start(new ProcessStartInfo(Assets){UseShellExecute=true});}catch(Exception ex){Event("folder-error",ex.Message);}}
    void OpenFolder(object sender,RoutedEventArgs e)=>OpenLogs(sender,e);
    void CloseMonitor(object sender,RoutedEventArgs e)=>Close();
}
