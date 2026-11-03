using System;
using System.Runtime.InteropServices;
namespace FM24FaceAssistant;
public static class WindowTheme
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    public static void Apply(IntPtr window)
    {
        int enabled=1,background=0x00372124,foreground=0x00F9F0F4;
        DwmSetWindowAttribute(window,20,ref enabled,4);
        DwmSetWindowAttribute(window,35,ref background,4);
        DwmSetWindowAttribute(window,36,ref foreground,4);
    }
}
