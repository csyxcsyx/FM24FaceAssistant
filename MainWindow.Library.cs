using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    readonly LibraryDownloadService libraryDownload;
    public bool IsLibraryDownloading=>libraryDownload.IsRunning;
    public int LoadedLibraryFaceCount=>faces.Count;
    public string? LibraryTaskId=>libraryDownload.TaskId;
    public string LibraryDownloadDirectory=>LibraryPaths.DownloadDirectory(LibraryPaths.FindProgramRoot(),Files.State,settings.LibraryDownloadPath,settings.Pack);
    void ViewLibraryMonitor(object sender,RoutedEventArgs e)=>((App)Application.Current).ShowMonitor(this);
    public void CancelLibraryDownload(){if(IsLibraryDownloading)cancel?.Cancel();}
    async void DownloadLibrary(object sender,RoutedEventArgs e)=>await StartLibraryDownload();
    public async Task StartLibraryDownload()
    {
        if(cancel!=null){if(IsLibraryDownloading)((App)Application.Current).ShowMonitor(this);Report(IsLibraryDownloading?"图库任务正在进行，请查看当前进度。":"请等待当前任务结束。");return;}
        // This button always invokes the official downloader. Arbitrary local pictures are not proof of a complete pack.
        ((App)Application.Current).ShowMonitor(this);
        await Run(async ct=>{
            var preferred=LibraryDownloadPath.Text.Trim();
            settings.LibraryDownloadPath=string.IsNullOrWhiteSpace(preferred)?"":Path.GetFullPath(preferred);
            var assets=LibraryDownloadDirectory;settings.LibraryDownloadPath=assets;LibraryDownloadPath.Text=assets;Persist();
            var progress=new Progress<LibraryProgress>(p=>{
                var indexing=p.stage=="indexing";var done=indexing?p.processed:p.downloaded;var total=indexing?p.total_images:p.total;
                taskProgress?.Report(new(indexing?"校验图片并建立索引":p.stage=="retrying"?"连接中断，准备重试":p.stage=="preparing"?"检查本地图库":"下载 NG_Regens 图库",done,total>0?total:null));
                LibraryDownloadStatus.Text=p.stage=="failed"?"下载未完成："+p.error:p.stage=="cancelled"?"已保存进度，点击下载 / 接续图库可继续。":indexing?$"已处理 {done:N0} / {total:N0} 张":total>0?$"已保存 {done/1e9:F2} / {total/1e9:F2} GB":"正在连接官方图库服务器…";
            });
            var result=await libraryDownload.RunAsync(assets,Files.State,progress,ct);
            faces=await Task.Run(()=>FaceLibraryStore.Load(ct,t=>taskProgress?.Report(new(t,0,null)),result.Library),ct);
            if(faces.Count==0)throw new IOException("图库下载完成但没有加载到可用头像，请查看下载日志并重试。");
            settings.Pack=result.Library;PackPath.Text=result.Library;Persist();CanonicalizeAssignments();recommended=null;InvalidateBatch();LoadSpecialPortraits();UpdateGallery();UpdateDownloadStatus();Report($"图库已就绪，共 {faces.Count:N0} 张，可直接选用；请重新扫描缺图生成方案。");
        },true);
    }
    public void SelectLibraryDownloadLocation()
    {
        if(cancel!=null){Report("请先取消当前任务，再选择下载位置。");return;}
        var dialog=new Microsoft.Win32.OpenFolderDialog{Title="选择空间充足的图库下载位置"};
        if(dialog.ShowDialog()!=true)return;
        settings.LibraryDownloadPath=dialog.FolderName;LibraryDownloadPath.Text=dialog.FolderName;Persist();UpdateDownloadStatus();
        Report("已更换下载位置。原目录的文件和进度仍保留；选择原目录可继续原任务。");
    }
}
