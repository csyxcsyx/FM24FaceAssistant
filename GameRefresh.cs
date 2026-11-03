using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FM24FaceAssistant;
public record RefreshStroke(ushort Scan,uint Flags);
public interface IRefreshTransport
{
    IntPtr Layout{get;}
    bool HasFocus{get;}
    Task PrepareAsync();
    void Send(IReadOnlyList<RefreshStroke> strokes);
    Task RestoreAsync();
}
public static class GameRefresh
{
    static readonly SemaphoreSlim gate=new(1,1);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyExW(uint code,uint type,IntPtr layout);
    public static (RefreshStroke[] Down,RefreshStroke[] Up) Plan(string key,IntPtr layout)
    {
        var(mods,vk)=Integration.ParseKey(key);var keys=new List<uint>();if((mods&1)!=0)keys.Add(0x12);if((mods&2)!=0)keys.Add(0x11);if((mods&4)!=0)keys.Add(0x10);keys.Add(vk);
        RefreshStroke Stroke(uint key,bool up){var scan=MapVirtualKeyExW(key,4,layout);if(scan==0)throw new IOException("无法转换刷新快捷键。");return new((ushort)(scan&255),8u|(up?2u:0u)|((scan&0xFF00)==0xE000?1u:0u));}
        return(keys.Select(k=>Stroke(k,false)).ToArray(),keys.AsEnumerable().Reverse().Select(k=>Stroke(k,true)).ToArray());
    }
    public static async Task ExecuteAsync(string key,IRefreshTransport transport,int holdMilliseconds=100)
    {
        try{await transport.PrepareAsync();var plan=Plan(key,transport.Layout);if(!transport.HasFocus)throw new IOException("游戏失去焦点，未发送刷新键。");try{transport.Send(plan.Down);await Task.Delay(holdMilliseconds);}finally{transport.Send(plan.Up);}}
        finally{await transport.RestoreAsync();}
    }
    public static async Task SendAsync(string key)
    {
        if(!await gate.WaitAsync(0))throw new IOException("刷新正在进行，请稍候。");
        try{using var process=Process.GetProcessesByName("fm").FirstOrDefault(p=>p.MainWindowHandle!=IntPtr.Zero)??throw new IOException("未找到游戏窗口。");await ExecuteAsync(key,new NativeTransport(process));}
        finally{gate.Release();}
    }
    sealed class NativeTransport : IRefreshTransport
    {
        readonly IntPtr window;readonly int pid;IntPtr original,ime;uint thread;bool? originalOpen;bool touchedLayout,closedIme,sent;
        public IntPtr Layout{get;private set;}
        public bool HasFocus{get{GetWindowThreadProcessId(GetForegroundWindow(),out var owner);return owner==pid;}}
        public NativeTransport(Process process){window=process.MainWindowHandle;pid=process.Id;}
        public async Task PrepareAsync()
        {
            ShowWindow(window,9);SetForegroundWindow(window);for(int i=0;i<20&&!HasFocus;i++)await Task.Delay(50);
            if(!HasFocus)throw new IOException("游戏未获得焦点。请确认游戏与助手权限相同。");
            thread=GetWindowThreadProcessId(window,out _);original=Layout=GetKeyboardLayout(thread);
            var info=new GUI{Size=(uint)Marshal.SizeOf<GUI>()};IntPtr focus=GetGUIThreadInfo(thread,ref info)&&info.Focus!=IntPtr.Zero?info.Focus:window;
            ime=ImmGetDefaultIMEWnd(window);if(ime==IntPtr.Zero)ime=ImmGetDefaultIMEWnd(focus);
            await Task.Run(()=>{if(Ime(5,0,out var open)){originalOpen=open!=0;if(originalOpen==true){if(!Ime(6,0,out _))throw new IOException("无法关闭游戏中的中文输入状态，请手动切换英文后重试。");closedIme=true;}}});
            var count=GetKeyboardLayoutList(0,null);var layouts=new IntPtr[Math.Min(128,Math.Max(0,count))];GetKeyboardLayoutList(layouts.Length,layouts);
            var english=layouts.OrderBy(h=>(h.ToInt64()&0xFFFF)==0x0409?0:1).FirstOrDefault(h=>((h.ToInt64()&0xFFFF)&0x3FF)==9);
            if(english!=IntPtr.Zero&&english!=original){touchedLayout=true;await Task.Run(()=>SendMessageTimeoutW(window,0x50,UIntPtr.Zero,english,2,600,out _));for(int i=0;i<12&&GetKeyboardLayout(thread)!=english;i++)await Task.Delay(40);Layout=GetKeyboardLayout(thread);if(Layout!=english)throw new IOException("游戏没有切换到英文输入，刷新键未发送。请手动切换英文后重试。");}
            else if(((Layout.ToInt64()&0xFFFF)&0x3FF)!=9){bool closed=false;for(int retry=0;retry<4&&!closed;retry++){closed=await Task.Run(()=>Ime(5,0,out var status)&&status==0);if(!closed)await Task.Delay(80);}if(!closed)throw new IOException("无法确认游戏已退出中文输入模式，刷新键未发送。");}
            Log($"input prepared layout={Layout.ToInt64():X} previous={original.ToInt64():X} ime={originalOpen}");
        }
        bool Ime(uint command,long value,out long result){result=0;if(ime==IntPtr.Zero)return false;var ok=SendMessageTimeoutW(ime,0x283,(UIntPtr)command,(IntPtr)value,2,500,out var response)!=IntPtr.Zero;result=response.ToInt64();return ok;}
        public void Send(IReadOnlyList<RefreshStroke> strokes)
        {
            var input=strokes.Select(s=>new INPUT{Type=1,Data=new UNION{Keyboard=new KEYBOARD{Scan=s.Scan,Flags=s.Flags}}}).ToArray();
            if(SendInput((uint)input.Length,input,Marshal.SizeOf<INPUT>())!=input.Length)throw new IOException("刷新按键未完整送达，请检查游戏权限。");sent=true;
        }
        public async Task RestoreAsync()
        {
            if(original==IntPtr.Zero)return;
            bool restored=true;
            if(touchedLayout){await Task.Delay(200);PostMessageW(window,0x50,UIntPtr.Zero,original);for(int i=0;i<60&&GetKeyboardLayout(thread)!=original;i++)await Task.Delay(100);restored=GetKeyboardLayout(thread)==original;}
            if(closedIme&&originalOpen==true){await Task.Run(()=>Ime(6,1,out _));}
            Log($"input restored={restored} sent={sent}");if(!restored)throw new IOException((sent?"刷新按键已发送，但":"")+"原输入法状态未能恢复，请手动切回所需输入法。");
        }
    }
    static void Log(string text){try{File.AppendAllText(Path.Combine(Files.State,"refresh.log"),DateTimeOffset.Now+" "+text+Environment.NewLine);}catch{}}
    [StructLayout(LayoutKind.Sequential)] struct GUI{public uint Size,Flags;public IntPtr Active,Focus,Capture,Menu,Move,Caret;public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct INPUT{public uint Type;public UNION Data;}
    [StructLayout(LayoutKind.Explicit)] struct UNION{[FieldOffset(0)]public KEYBOARD Keyboard;[FieldOffset(0)]public MOUSE Mouse;}
    [StructLayout(LayoutKind.Sequential)] struct KEYBOARD{public ushort Vk,Scan;public uint Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] struct MOUSE{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out int pid);
    [DllImport("user32.dll")]static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")]static extern int GetKeyboardLayoutList(int size,[Out]IntPtr[]? list);
    [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref GUI info);
    [DllImport("imm32.dll")]static extern IntPtr ImmGetDefaultIMEWnd(IntPtr window);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeoutW(IntPtr window,uint message,UIntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostMessageW(IntPtr window,uint message,UIntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint size,INPUT[] input,int cbSize);
}
