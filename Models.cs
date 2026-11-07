using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FM24FaceAssistant;

public record Person(long Id, string Name, string Kind, string Gender, string Nation, string Club, string Dob, bool? Newgen, string? Group = null)
{
    [System.Text.Json.Serialization.JsonIgnore] public string MatchGender => NameGender.Resolve(this).Gender;
    public string Identity => $"{Id}|{Dob}|{Name}";
    public IEnumerable<string> Keys => Newgen == null ? new[] { Id.ToString(), $"r-{Id}" } : new[] { Key };
    public string Key => Newgen == true ? $"r-{Id}" : Id.ToString();
    [System.Text.Json.Serialization.JsonIgnore]
    public string[] ListTags=>new[]{"ID "+Key,Kind,CountryNames.English(Nation)};
    public override string ToString() => $"{Name}\nID {Id}\n{Kind}\n{CountryNames.English(Nation)}";
}
public record Face(string Path, string Group, string Kind, string Gender, string Hash, string? VisualHash = null)
{
    public string UniqueKey => VisualHash ?? Hash;
}
public static class PersonSearch
{
    public static string[] PreviewKeys(Person person,string query) => RequestedKeys(person,query) ?? person.Keys.ToArray();
    public static string[]? RequestedKeys(Person person,string query)
    {
        query=query.Trim();return query.StartsWith("r-",StringComparison.OrdinalIgnoreCase)&&long.TryParse(query[2..],out var id)&&id==person.Id?new[]{"r-"+person.Id}:null;
    }
    public static string? Portrait(Person person,IEnumerable<string> keys,IEnumerable<Assignment> assignments,IEnumerable<Mapping> mappings)
    {
        var own=assignments.FirstOrDefault(a=>a.Person.Identity==person.Identity);
        foreach(var key in keys){if(own?.OutputKeys.Contains(key)==true)return own.Face.Path;var external=mappings.FirstOrDefault(m=>m.Valid&&m.Target==$"graphics/pictures/person/{key}/portrait");if(external!=null)return external.Image;}
        return null;
    }
    public static bool Matches(Person person, string query)
    {
        query = query.Trim();
        if (query.Length == 0 || person.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        var idQuery = query.StartsWith("r-", StringComparison.OrdinalIgnoreCase) ? query[2..] : query;
        return idQuery.Length > 0 && idQuery.All(c => c >= '0' && c <= '9') && person.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(idQuery);
    }
}
public record Assignment(Person Person, Face Face, bool Locked, string Reason, string[]? TargetKeys=null)
{
    [System.Text.Json.Serialization.JsonIgnore] public IEnumerable<string> OutputKeys=>TargetKeys??Person.Keys;
}
public class Settings
{
    public long LastPersonId {get;set;}
    public double WindowWidth {get;set;}=1280;
    public double WindowHeight {get;set;}=840;
    public string Documents { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 2024");
    public string Pack { get; set; } = "";
    public string LibraryDownloadPath { get; set; } = "";
    public string Profile { get; set; } = "主存档";
    public string Hotkey { get; set; } = "Ctrl+Alt+F";
    public string RefreshKey { get; set; } = "Shift+R";
    public bool Auto { get; set; }
    public string Skin { get; set; } = "";
    public bool ShowSkinEntry { get; set; } = true;
    public bool SkipSkinInstallPrompt { get; set; }
}
public static class Files
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string State = StateLocation.Find();
    public static string Sqlite(string path)=>new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=path}.ToString();
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static void Atomic(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + ".tmp"; File.WriteAllText(temp, text, Encoding.UTF8); for(int retry=0;;retry++){try{File.Move(temp,path,true);break;}catch(Exception e) when(retry<39 && (e is IOException || e is UnauthorizedAccessException)){System.Threading.Thread.Sleep(25);}} }
    public static string ReadTextShared(string path) { using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); using var reader=new StreamReader(stream);return reader.ReadToEnd(); }
    public static T? Read<T>(string path)
    {
        if(!File.Exists(path))return default;
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536);
        Span<byte> prefix=stackalloc byte[4];int count=stream.ReadAtLeast(prefix,4,false);stream.Position=0;
        // Preserve imports from older Windows tools that wrote UTF-16/UTF-32 JSON.
        bool unicode=count>=2&&((prefix[0]==255&&prefix[1]==254)||(prefix[0]==254&&prefix[1]==255))||count>=4&&prefix[0]==0&&prefix[1]==0&&prefix[2]==254&&prefix[3]==255;
        if(unicode){using var reader=new StreamReader(stream);return JsonSerializer.Deserialize<T>(reader.ReadToEnd(),Json);}
        return JsonSerializer.Deserialize<T>(stream,Json);
    }
    public static void Save<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,65536))JsonSerializer.Serialize(stream,value,Json);
        for(int retry=0;;retry++){try{File.Move(temp,path,true);break;}catch(Exception ex) when(retry<39&&(ex is IOException||ex is UnauthorizedAccessException)){System.Threading.Thread.Sleep(25);}}
    }
}
