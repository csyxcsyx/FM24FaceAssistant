using FM24FaceAssistant;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

static class LibraryDownloadUiProbe
{
    public static int Run(string sandbox)
    {
        sandbox=Path.GetFullPath(sandbox);Files.State=Path.Combine(sandbox,"用户数据;state");Directory.CreateDirectory(Files.State);
        var wrong=Path.Combine(sandbox,"皮肤 pictures");Directory.CreateDirectory(wrong);var icon=Path.Combine(wrong,"shirt.png");File.WriteAllBytes(icon,LibraryDownloadChecks.Png());
        var stale=Enumerable.Range(0,695).Select(_=>new Face(icon,"icons","球员","男","old-skin-index")).ToList();
        Files.Save(Path.Combine(Files.State,"catalog.json"),stale);Files.Save(Path.Combine(Files.State,"settings.json"),new Settings{Pack=wrong,Documents=Path.Combine(sandbox,"FM docs"),SkipSkinInstallPrompt=true});
        var data=LibraryDownloadChecks.Zip();using var server=new LibraryDownloadChecks.Server(_=>new(data));
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window=new MainWindow(new(new(server.Url,0,2,0,5000)),enableDesktopIntegration:false){Opacity=0,ShowActivated=false,ShowInTaskbar=false};
        bool rendered=false;window.ContentRendered+=(_,_)=>rendered=true;window.Show();
        var startupFrame=new DispatcherFrame();var startupTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};var deadline=DateTime.UtcNow.AddSeconds(30);
        startupTimer.Tick+=(_,_)=>{if(rendered&&typeof(MainWindow).GetField("cancel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)==null||DateTime.UtcNow>deadline)startupFrame.Continue=false;};startupTimer.Start();Dispatcher.PushFrame(startupFrame);startupTimer.Stop();
        if(!rendered||typeof(MainWindow).GetField("cancel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!=null)throw new Exception("UI startup timed out.");
        Files.Save(Path.Combine(window.LibraryDownloadDirectory,"download-status.json"),new{stage="complete",total_images=695});
        var staleMonitor=new DownloadMonitor{Owner=window,ShowActivated=false};staleMonitor.Show();
        if(!((TextBlock)staleMonitor.FindName("StageText")).Text.Contains("待校验"))throw new Exception("A stale completion record must not claim the missing library is loaded.");staleMonitor.Close();
        typeof(MainWindow).GetMethod("PreviewMatch",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
        var previewFrame=new DispatcherFrame();var previewTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};previewTimer.Tick+=(_,_)=>{if(typeof(MainWindow).GetField("cancel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)==null)previewFrame.Continue=false;};previewTimer.Start();Dispatcher.PushFrame(previewFrame);previewTimer.Stop();
        if(!(typeof(MainWindow).GetField("lastMessage",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window) as string)!.Contains("下载 / 接续图库"))throw new Exception("Missing library must be reported before touching the game process.");
        typeof(MainWindow).GetField("faces",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,stale);
        typeof(MainWindow).GetField("batch",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,new List<Assignment>());
        var selected=Path.Combine(sandbox,"新磁盘 downloads");((TextBox)window.FindName("LibraryDownloadPath")).Text=selected;
        var task=window.StartLibraryDownload();
        foreach(var monitor in app.Windows.OfType<DownloadMonitor>())monitor.Hide();
        var frame=new DispatcherFrame();task.ContinueWith(_=>window.Dispatcher.BeginInvoke(()=>frame.Continue=false));Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
        var faces=(List<Face>)typeof(MainWindow).GetField("faces",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var saved=Files.Read<Settings>(Path.Combine(Files.State,"settings.json"))!;
        if(server.Requests.Count==0||faces.Count!=3||saved.Pack==wrong||saved.LibraryDownloadPath!=selected||typeof(MainWindow).GetField("batch",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!=null)throw new Exception("Production UI failed to replace a 695-image skin index with the downloaded library.");
        Console.WriteLine(JsonSerializer.Serialize(new{Passed=true,OldImages=stale.Count,NetworkRequests=server.Requests.Count,LoadedFaces=faces.Count,SelectedDownloadPath=saved.LibraryDownloadPath,Library=saved.Pack,StaleBatchCleared=true,StaleCompletionNotTrusted=true,MissingLibraryPreventsGameScan=true}));
        foreach(var monitor in app.Windows.OfType<DownloadMonitor>().ToArray())monitor.Close();window.Close();return 0;
    }
}
