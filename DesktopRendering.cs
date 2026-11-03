using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Media;

namespace FM24FaceAssistant;

public static class DesktopRendering
{
    public static void Configure()
    {
        using var current=Process.GetCurrentProcess();
        bool steam=new[]{"SteamAppId","SteamGameId","SteamOverlayGameId"}.Any(name=>!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            ||current.Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Equals("gameoverlayrenderer64.dll",StringComparison.OrdinalIgnoreCase)||m.ModuleName.Equals("gameoverlayrenderer.dll",StringComparison.OrdinalIgnoreCase));
        // Steam hooks Direct3D surfaces in tools opened from the game as well.
        // Use WPF's software path for that launch only, before creating any window.
        if(steam)RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
    }
}
