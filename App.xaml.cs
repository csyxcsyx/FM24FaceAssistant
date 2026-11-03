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
    protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); DispatcherUnhandledException+=(_,args)=>{try{File.AppendAllText(Path.Combine(Files.State,"errors.log"),DateTimeOffset.Now+" "+args.Exception+Environment.NewLine);}catch{} MessageBox.Show("程序遇到异常，错误已记录。请退出后重开；如有未完成写入，下次启动会检查恢复。","头像助手");}; if(Array.IndexOf(e.Args,"--monitor")>=0){MainWindow=new DownloadMonitor();MainWindow.Show();return;} singleton = new Mutex(true, "Local\\FM24FaceAssistant", out bool owner); if (!owner) { try { using var client = new NamedPipeClientStream(".", "FM24FaceAssistant", PipeDirection.Out); client.Connect(3000); WindowActivation.Grant(client); using var writer = new StreamWriter(client); writer.WriteLine("open"); writer.Flush(); } catch { WindowActivation.Record("forward-failed",false); } Shutdown(); return; } WindowActivation.Record("cold-start"); var window = new MainWindow(); if(Array.IndexOf(e.Args,"--compact")>=0){window.Width=1120;window.Height=740;} MainWindow = window; window.Show(); _ = Listen(window); }
    async Task Listen(MainWindow window) { while (true) { try { using var server = new NamedPipeServerStream("FM24FaceAssistant", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly); await server.WaitForConnectionAsync(); using var reader = new StreamReader(server); if (await reader.ReadLineAsync() == "open") await Dispatcher.InvokeAsync(window.Reveal); } catch { return; } } }
    protected override void OnExit(ExitEventArgs e) { singleton?.Dispose(); base.OnExit(e); }
}
