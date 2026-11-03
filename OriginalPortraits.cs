using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
namespace FM24FaceAssistant;
public static class OriginalPortraits
{
    public static void Restore(string state,string graphics,string profile,PatchWriter writer,CancellationToken ct=default,IProgress<TaskProgress>? progress=null)
    {
        var changes=new Dictionary<string,Change>(StringComparer.OrdinalIgnoreCase);
        var externalChanges=new List<Change>();
        void Add(string path,string text){var before=File.Exists(path)?Files.ReadTextShared(path):null;if(before!=text){if(changes.TryGetValue(path,out var existing)&&existing.After!=text)throw new IOException("两套头像输出的恢复记录冲突，未修改文件："+path);changes[path]=new(path,before,text);}}
        foreach(bool special in new[]{false,true}){
            ct.ThrowIfCancellationRequested();progress?.Report(new("核对原头像包",special?1:0,2));
            var active=Path.Combine(state,special?"active-special.json":"active.json");var patch=PatchJournal.LoadActive(active);
            if(patch!=null&&patch.Profile.Length>0&&patch.Profile!=profile)throw new IOException("正在使用另一份存档头像，请切换到对应记录后恢复。");
            externalChanges.AddRange(patch?.External??new());
            var config=Path.Combine(graphics,"FM24FaceAssistant",special?"special":"","config.xml");if(File.Exists(config))Add(config,"<record><list id=\"maps\" /></record>");
            if(File.Exists(active))Add(active,PatchJournal.ActiveText(state,"",new(),ct));
            var rows=Path.Combine(state,(special?"special-rows-":"rows-")+Files.Hash(profile)+".json");if(File.Exists(rows))Add(rows,"[]");
        }
        foreach(var group in externalChanges.GroupBy(x=>x.Path,StringComparer.OrdinalIgnoreCase)){
            ct.ThrowIfCancellationRequested();if(!File.Exists(group.Key))throw new IOException("外部头像配置已移走，未修改文件："+group.Key);
            var cursor=Files.ReadTextShared(group.Key);var remaining=group.Distinct().ToList();
            while(remaining.Count>0){
                var next=remaining.Where(c=>c.After==cursor).ToList();
                if(next.Select(c=>c.Before).Distinct().Count()>1)throw new IOException("外部头像恢复记录冲突："+group.Key);
                if(next.Count>0){cursor=next[0].Before??"<record><list id=\"maps\" /></record>";foreach(var change in next)remaining.Remove(change);continue;}
                var already=remaining.Where(c=>c.Before==cursor).ToList();if(already.Count>0){foreach(var change in already)remaining.Remove(change);continue;}
                throw new IOException("外部头像包有后续修改，未覆盖："+group.Key);
            }
            Add(group.Key,cursor);
        }
        ct.ThrowIfCancellationRequested();if(changes.Count>0)writer.Commit(changes.Values.ToList(),"恢复原头像包",ct,progress);
    }
}
