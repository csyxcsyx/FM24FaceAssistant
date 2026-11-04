using System.IO;
using System.Linq;

namespace FM24FaceAssistant;

public static class LibraryPaths
{
    static readonly string[] LegacyFiles={"download-status.json","NG_Regens_MainPack.zip","NG_Regens_MainPack.zip.part","library-report.json"};

    public static string? FindProgramRoot()
    {
        return System.AppContext.BaseDirectory;
    }

    public static string DownloadDirectory(string? programRoot,string state,string? preferredDirectory=null)
    {
        if(!string.IsNullOrWhiteSpace(preferredDirectory))return Path.GetFullPath(preferredDirectory);
        if(!string.IsNullOrWhiteSpace(programRoot))
        {
            var legacy=Path.Combine(programRoot,"assets");
            if(LegacyFiles.Any(name=>File.Exists(Path.Combine(legacy,name)))||Directory.Exists(Path.Combine(legacy,"NG_Regens_Library")))return legacy;
        }
        return Path.Combine(state,"library-download");
    }

    public static bool HasDownloadData(string directory)
    {
        return new[]{"NG_Regens_MainPack.zip","NG_Regens_MainPack.zip.part"}.Any(name=>File.Exists(Path.Combine(directory,name)))||Directory.Exists(Path.Combine(directory,"NG_Regens_Library"));
    }
}
