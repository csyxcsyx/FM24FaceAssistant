using FM24FaceAssistant;
using System.Diagnostics;
using System.IO;
using System.Threading;
static class InstanceChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        check(InstancePolicy.ShouldReplace(new(1,0,4,0),new(1,0,3,0)),"新版可请求空闲旧版正常退出");
        check(!InstancePolicy.ShouldReplace(new(1,0,2,0),new(1,0,3,0))&&!InstancePolicy.ShouldReplace(new(1,0,3,0),new(1,0,3,0)),"同版及旧版唤出已有实例而不降级");
        var name="Local\\FM24-test-"+Guid.NewGuid().ToString("N");var ready=Path.Combine(root,"instance-ready");var release=Path.Combine(root,"instance-release");
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetTempPath()};
        foreach(var arg in new[]{"--instance-holder",name,ready,release})start.ArgumentList.Add(arg);
        using var child=Process.Start(start)!;
        try
        {
            var watch=Stopwatch.StartNew();while(!File.Exists(ready)&&!child.HasExited&&watch.ElapsedMilliseconds<10000)Thread.Sleep(20);
            check(File.Exists(ready),"异地工作目录的独立进程取得单实例锁");
            using var mutex=new Mutex(false,name);var acquired=InstancePolicy.Acquire(mutex);if(acquired)mutex.ReleaseMutex();check(!acquired,"不同进程无法同时持有头像助手实例锁");
            File.WriteAllText(release,"exit");check(child.WaitForExit(5000)&&child.ExitCode==0,"旧实例正常释放互斥锁");
            acquired=InstancePolicy.Acquire(mutex);check(acquired,"旧实例退出后新实例可以接管");if(acquired)mutex.ReleaseMutex();
        }
        finally{if(!child.HasExited){File.WriteAllText(release,"exit");if(!child.WaitForExit(3000))child.Kill(true);}}
    }
}
