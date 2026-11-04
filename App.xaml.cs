using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
namespace FM24FaceAssistant;
public partial class App : Application
{
    Mutex? singleton;
    bool ownsInstance;
    DownloadMonitor? monitor;
    protected override async void OnStartup(StartupEventArgs e)
    {
        DesktopRendering.Configure();base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException+=(_,args)=>{try{File.AppendAllText(Path.Combine(Files.State,"errors.log"),DateTimeOffset.Now+" "+args.Exception+Environment.NewLine);}catch{} MessageBox.Show("程序遇到异常，错误已记录。请退出后重开；如有未完成写入，下次启动会检查恢复。","头像助手");};
        try
        {
            singleton=new Mutex(false,@"Local\FM24FaceAssistant");ownsInstance=InstancePolicy.Acquire(singleton);
            if(!ownsInstance&&!await HandleExisting(Array.IndexOf(e.Args,"--monitor")>=0)){Shutdown();return;}
            WindowActivation.Record("cold-start");var window=new MainWindow();
            if(Array.IndexOf(e.Args,"--compact")>=0){window.Width=1120;window.Height=740;}
            MainWindow=window;window.Show();_=Listen(window);
            if(Array.IndexOf(e.Args,"--monitor")>=0)ShowMonitor(window);
        }
        catch(Exception ex){MessageBox.Show("无法启动头像助手："+ex.Message,"头像助手");Shutdown();}
    }
    async Task<bool> HandleExisting(bool showMonitor)
    {
        try
        {
            var current=typeof(App).Assembly.GetName().Version??new Version();
            // A write-only probe is compatible with the inbound pipe used by 1.0.2.
            using(var probe=new NamedPipeClientStream(".","FM24FaceAssistant",PipeDirection.Out,PipeOptions.Asynchronous))
            {
                await probe.ConnectAsync(5000);WindowActivation.Grant(probe);var running=WindowActivation.Server(probe);
                using var writer=new StreamWriter(probe){AutoFlush=true};
                if(!InstancePolicy.ShouldReplace(current,running.Version))
                {
                    await writer.WriteLineAsync(showMonitor&&running.Version>=new Version(1,0,3,0)?"monitor":"open");
                    if(current<running.Version)MessageBox.Show($"已在使用较新版本 {running.Version}，已唤出该窗口。位置：{running.Path}","头像助手");
                    return false;
                }
                if(running.Version<new Version(1,0,3,0))
                {
                    await writer.WriteLineAsync("open");
                    MessageBox.Show($"旧版本 {running.Version} 尚不支持自动切换。请从托盘退出旧版，再重新打开此新版。旧版位置：{running.Path}","切换头像助手版本");return false;
                }
            }
            using var client=new NamedPipeClientStream(".","FM24FaceAssistant",PipeDirection.InOut,PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);WindowActivation.Grant(client);
            using(var writer=new StreamWriter(client,System.Text.Encoding.UTF8,1024,true){AutoFlush=true})await writer.WriteLineAsync("switch-v1");
            using var reader=new StreamReader(client);
            var reply=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if(reply!="closing")
            {
                MessageBox.Show(reply=="busy"?"已有版本正在执行任务或打开设置窗口。请完成任务并退出后，再打开新版。":"已有版本未确认切换。请退出旧版后再打开新版。","切换头像助手版本");return false;
            }
            for(int i=0;i<100;i++){ownsInstance=InstancePolicy.Acquire(singleton!);if(ownsInstance)return true;await Task.Delay(100);}
            MessageBox.Show("旧版仍在退出，请稍后重新打开新版。","头像助手");return false;
        }
        catch(Exception ex){WindowActivation.Record("forward-failed",false);MessageBox.Show("已有头像助手正在运行或启动，暂时无法切换。请退出托盘中的旧版后重试。"+Environment.NewLine+ex.Message,"头像助手");return false;}
    }
    void ShowMonitor(MainWindow window)
    {
        if(monitor!=null){monitor.Show();monitor.Activate();return;}
        monitor=new DownloadMonitor{Owner=window};monitor.Closed+=(_,_)=>monitor=null;monitor.Show();
    }
    async Task Listen(MainWindow window)
    {
        while(true)
        {
            try
            {
                using var server=new NamedPipeServerStream("FM24FaceAssistant",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();using var reader=new StreamReader(server);
                var command=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                if(command=="open")await Dispatcher.InvokeAsync(window.Reveal);
                else if(command=="monitor")await Dispatcher.InvokeAsync(()=>ShowMonitor(window));
                else if(command=="switch-v1")
                {
                    using var writer=new StreamWriter(server){AutoFlush=true};
                    var closed=await Dispatcher.InvokeAsync(()=>{
                        if(!window.CanSwitchVersion){writer.WriteLine("busy");return false;}
                        writer.WriteLine("closing");window.Close();return true;
                    });
                    if(closed)return;
                }
            }
            catch(ObjectDisposedException){return;}
            catch(Exception){if(Dispatcher.HasShutdownStarted)return;await Task.Delay(100);}
        }
    }
    protected override void OnExit(ExitEventArgs e){if(ownsInstance)singleton?.ReleaseMutex();singleton?.Dispose();base.OnExit(e);}
}
