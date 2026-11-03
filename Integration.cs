using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32;

namespace FM24FaceAssistant;

public static class Integration
{
    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint change,uint flags,IntPtr first,IntPtr second);
    public static void Protocol(bool install) { const string key = @"Software\Classes\fm24face"; if (!install) { using var existing = Registry.CurrentUser.OpenSubKey(key); if (existing?.GetValue("Owner") as string != "FM24FaceAssistant") throw new IOException("协议不属于此工具，未移除。"); Registry.CurrentUser.DeleteSubKeyTree(key, false); SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero); return; } using var prior = Registry.CurrentUser.OpenSubKey(key); if (prior != null && prior.GetValue("Owner") as string != "FM24FaceAssistant") throw new IOException("fm24face 协议已被其他程序注册。"); using var root = Registry.CurrentUser.CreateSubKey(key); root.SetValue("", "URL:FM24FaceAssistant"); root.SetValue("URL Protocol", ""); root.SetValue("Owner", "FM24FaceAssistant"); using var command = root.CreateSubKey(@"shell\open\command"); command.SetValue("", $"\"{Environment.ProcessPath}\" \"%1\""); SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero); }
    public static void Skin(string skin,PatchWriter writer)=>SkinEntry.Install(skin,writer);
    public static (uint modifiers, uint key) ParseKey(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries); uint mods = 0; foreach (var p in parts[..^1]) mods |= p.ToLowerInvariant() switch { "alt" => 1u, "ctrl" => 2u, "shift" => 4u, _ => throw new ArgumentException("快捷键支持 Ctrl、Alt、Shift 加字母或 F1–F12。") };
        var last = parts[^1].ToUpperInvariant(); uint k; if (last.Length == 1 && char.IsAsciiLetterOrDigit(last[0])) k = last[0]; else if (last.StartsWith('F') && int.TryParse(last[1..], out int n) && n >= 1 && n <= 12) k = (uint)(111 + n); else throw new ArgumentException("不支持的快捷键。"); return (mods, k);
    }
    public static void Refresh(string key)=>RefreshAsync(key).GetAwaiter().GetResult();
    public static System.Threading.Tasks.Task RefreshAsync(string key)=>GameRefresh.SendAsync(key);
}
