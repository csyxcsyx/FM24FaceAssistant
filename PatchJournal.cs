using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace FM24FaceAssistant;
public record ExternalSnapshot(string Path,string? BeforeFile,string AfterFile);
public record ActivePatchManifest(int Version,string Profile,List<ExternalSnapshot> ExternalFiles);
public record JournalEntry(string Path,string? BeforeFile,string AfterFile,string? BeforeHash,string AfterHash);
public record JournalManifest(int Version,List<JournalEntry> Entries);
public record TaskProgress(string Stage,long Completed,long? Total,string? Current=null,bool CanCancel=true)
{
    public double? Percent=>Total>0?Math.Clamp(100d*Completed/Total.Value,0,100):null;
}
public sealed class ThrottledTaskProgress : IProgress<TaskProgress>
{
    readonly IProgress<TaskProgress> target; long last; string stage=""; bool cancellable=true;
    public ThrottledTaskProgress(IProgress<TaskProgress> target){this.target=target;}
    public void Report(TaskProgress value){lock(this){long now=Environment.TickCount64;if(now-last<100&&value.Stage==stage&&value.CanCancel==cancellable&&value.Completed!=value.Total)return;last=now;stage=value.Stage;cancellable=value.CanCancel;target.Report(value);}}
}
public static class PatchJournal
{
    public static ActivePatch? LoadActive(string path)
    {
        if(!File.Exists(path))return null;
        using var document=System.Text.Json.JsonDocument.Parse(Files.ReadTextShared(path));
        if(document.RootElement.TryGetProperty("Version",out var version)&&version.GetInt32() is 2 or 3){var manifest=Files.Read<ActivePatchManifest>(path)!;var state=Path.GetDirectoryName(path)!;return new(manifest.Profile,manifest.ExternalFiles.Select(x=>new Change(x.Path,x.BeforeFile==null?null:SnapshotStore.Read(StateLocation.Redirect(x.BeforeFile,state)),SnapshotStore.Read(StateLocation.Redirect(x.AfterFile,state)))).ToList());}
        return Files.Read<ActivePatch>(path);
    }
    public static string ActiveText(string state,string profile,List<Change> changes,System.Threading.CancellationToken ct)
    {
        var files=new List<ExternalSnapshot>();
        foreach(var change in changes){ct.ThrowIfCancellationRequested();files.Add(new(change.Path,change.Before==null?null:SnapshotStore.Put(state,change.Before,ct),SnapshotStore.Put(state,change.After,ct)));}
        return System.Text.Json.JsonSerializer.Serialize(new ActivePatchManifest(3,profile,files),Files.Json);
    }
    public static void Save(string folder,List<Change> changes,System.Threading.CancellationToken ct,IProgress<TaskProgress>? progress)
    {
        var entries=new List<JournalEntry>();int i=0;var state=Path.GetDirectoryName(Path.GetDirectoryName(folder))!;
        foreach(var change in changes){ct.ThrowIfCancellationRequested();progress?.Report(new("压缩恢复快照",i++,changes.Count,Path.GetFileName(change.Path)));var before=change.Before==null?null:Path.GetFileName(SnapshotStore.Put(state,change.Before,ct));var after=Path.GetFileName(SnapshotStore.Put(state,change.After,ct));entries.Add(new(change.Path,before,after,change.Before==null?null:Files.Hash(change.Before),Files.Hash(change.After)));}
        ct.ThrowIfCancellationRequested();Files.Save(Path.Combine(folder,"transaction.json"),new JournalManifest(3,entries));
    }
    public static List<Change>? Load(string folder)
    {
        var path=Path.Combine(folder,"transaction.json");if(!File.Exists(path)){var legacy=Path.Combine(folder,"changes.json");var root=Path.GetDirectoryName(Path.GetDirectoryName(folder))!;return File.Exists(legacy)?Files.Read<List<Change>>(legacy)?.Select(c=>c with{Path=StateLocation.Redirect(c.Path,root)}).ToList():null;}
        var manifest=Files.Read<JournalManifest>(path)??throw new IOException("备份清单为空。");if(manifest.Version is not (2 or 3))throw new IOException("不支持的备份版本。");
        string Read(string name){var root=manifest.Version==3?Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(folder))!,"snapshot-blobs"):folder;var full=Path.GetFullPath(Path.Combine(root,name));if(!full.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("备份路径无效。");return manifest.Version==3?SnapshotStore.Read(full):Files.ReadTextShared(full);}
        var state=Path.GetDirectoryName(Path.GetDirectoryName(folder))!;
        return manifest.Entries.Select(e=>{var before=e.BeforeFile==null?null:Read(e.BeforeFile);var after=Read(e.AfterFile);if((before==null?null:Files.Hash(before))!=e.BeforeHash||Files.Hash(after)!=e.AfterHash)throw new IOException("备份快照校验失败："+e.Path);return new Change(StateLocation.Redirect(e.Path,state),before,after);}).ToList();
    }
}
