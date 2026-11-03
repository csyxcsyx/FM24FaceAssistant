using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace FM24FaceAssistant;
public static class BackupCompaction
{
    public static long Run(string state,CancellationToken ct=default,IProgress<TaskProgress>? progress=null)
    {
        long reclaimed=0;var root=Path.Combine(state,"backups");if(!Directory.Exists(root))return 0;
        var folders=Directory.GetDirectories(root);int count=0;
        foreach(var folder in folders){
            ct.ThrowIfCancellationRequested();progress?.Report(new("压缩历史版本",count++,folders.Length,Path.GetFileName(folder)));
            var path=Path.Combine(folder,"transaction.json");var manifest=Files.Read<JournalManifest>(path);
            if(manifest?.Version!=2)continue;
            var original=Files.ReadTextShared(path);var changes=PatchJournal.Load(folder)!;
            PatchJournal.Save(folder,changes,ct,progress);
            try{var verified=PatchJournal.Load(folder)!;if(!changes.SequenceEqual(verified))throw new IOException("历史版本压缩校验失败。");}catch{Files.Atomic(path,original);throw;}
            foreach(var name in manifest.Entries.SelectMany(e=>new[]{e.BeforeFile,e.AfterFile}).Where(n=>n!=null).Distinct()){
                var full=Path.GetFullPath(Path.Combine(folder,name!));if(!full.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("历史快照路径无效。");
                if(File.Exists(full)){reclaimed+=new FileInfo(full).Length;File.Delete(full);}
            }
        }
        return reclaimed;
    }
}
