using System;
using System.IO;
using System.Linq;
namespace FM24FaceAssistant;
public record DataLocation(string Root);
public static class StateLocation
{
    public static string Find()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory!=null;directory=directory.Parent){
            var marker=Path.Combine(directory.FullName,"data-location.json");
            if(File.Exists(marker)){var location=System.Text.Json.JsonSerializer.Deserialize<DataLocation>(File.ReadAllText(marker));if(!string.IsNullOrWhiteSpace(location?.Root))return Path.GetFullPath(Path.Combine(directory.FullName,location.Root));}
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FM24FaceAssistant");
    }
    public static string Redirect(string path,string state)
    {
        var roots=Files.Read<string[]>(Path.Combine(state,"legacy-roots.json"))??Array.Empty<string>();
        foreach(var root in roots){var boundary=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(path.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))return Path.Combine(state,path[boundary.Length..]);}
        return path;
    }
}
