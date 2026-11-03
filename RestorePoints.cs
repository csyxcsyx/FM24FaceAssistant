using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
namespace FM24FaceAssistant;

public record RestorePoint(string Folder,string Label)
{
    public override string ToString()=>Label;
}
public static class RestorePoints
{
    public static List<RestorePoint> List(string state,string profile)
    {
        var root=Path.Combine(state,"backups");if(!Directory.Exists(root))return new();
        var rows=Path.Combine(state,"rows-"+Files.Hash(profile)+".json");
        var result=new List<RestorePoint>();
        foreach(var dir in Directory.GetDirectories(root).OrderDescending()){
            if(!File.Exists(Path.Combine(dir,"complete")))continue;
            var manifest=Files.Read<JournalManifest>(Path.Combine(dir,"transaction.json"));
            if(manifest==null||!manifest.Entries.Any(x=>StateLocation.Redirect(x.Path,state).Equals(rows,StringComparison.OrdinalIgnoreCase)))continue;
            var id=Path.GetFileName(dir);var date=DateTime.TryParseExact(id,"yyyyMMdd-HHmmss-fffffff",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var utc)?utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):id;
            var kind=File.Exists(Path.Combine(dir,"kind.txt"))?File.ReadAllText(Path.Combine(dir,"kind.txt")):"头像应用";
            result.Add(new(dir,date+" · "+kind+"之前"));
        }
        return result;
    }
    public static void Restore(string state,string profile,RestorePoint point,PatchWriter writer,CancellationToken ct=default,IProgress<TaskProgress>? progress=null)
    {
        var points=List(state,profile);if(!points.Any(x=>x.Folder==point.Folder))throw new IOException("此恢复点不属于当前存档记录。");
        var desired=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        var known=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var history=points.OrderBy(p=>p.Folder,StringComparer.Ordinal).ToList();int processed=0;
        foreach(var entry in history){
            ct.ThrowIfCancellationRequested();progress?.Report(new("核对历史快照",processed++,history.Count,entry.Label));
            foreach(var change in PatchJournal.Load(entry.Folder)??throw new IOException("恢复清单缺失。")){
                if(!known.TryGetValue(change.Path,out var hashes))known[change.Path]=hashes=new();
                hashes.Add(change.Before==null?"<absent>":Files.Hash(change.Before));hashes.Add(Files.Hash(change.After));
                if(string.CompareOrdinal(entry.Folder,point.Folder)>=0)desired.TryAdd(change.Path,change.Before);
            }
        }
        var changes=new List<Change>();
        foreach(var pair in desired){
            ct.ThrowIfCancellationRequested();var before=File.Exists(pair.Key)?Files.ReadTextShared(pair.Key):null;
            if(!known[pair.Key].Contains(before==null?"<absent>":Files.Hash(before)))throw new IOException("文件有历史记录以外的修改，未覆盖："+pair.Key);
            // An empty config is equivalent to an absent patch while retaining
            // the ordinary atomic replacement and interrupted-write recovery.
            var after=pair.Value??(Path.GetFileName(pair.Key).StartsWith("rows-")?"[]":pair.Key.EndsWith("active.json",StringComparison.OrdinalIgnoreCase)?PatchJournal.ActiveText(state,"",new(),ct):"<record><list id=\"maps\" /></record>");
            if(before!=after)changes.Add(new(pair.Key,before,after));
        }
        if(changes.Count==0)return;
        writer.Commit(changes,"时间点恢复",ct,progress);
    }
}
