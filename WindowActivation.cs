using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FM24FaceAssistant;

internal static class WindowActivation
{
    // Transfer the click-launched process's foreground permission to the existing window.
    // No global hooks or input simulation are required.
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll", SetLastError=true)]
    static extern bool AllowSetForegroundWindow(uint processId);

    public static void Grant(NamedPipeClientStream pipe)
    {
        var granted=GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var pid)&&AllowSetForegroundWindow(pid);
        Record("forward",granted);
    }
    public static void Record(string action,bool? result=null)
    {
        try
        {
            Directory.CreateDirectory(Files.State);
            var path=Path.Combine(Files.State,"activation.log");
            if(File.Exists(path)&&new FileInfo(path).Length>65536)File.Move(path,path+".previous",true);
            File.AppendAllText(path,$"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {action} result={result}\n");
        }
        catch { /* Diagnostic logging must never prevent opening the window. */ }
    }
}
