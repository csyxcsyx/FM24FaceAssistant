using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace FM24FaceAssistant;

public record Change(string Path, string? Before, string After);
public record ActivePatch(string Profile, List<Change> External);
public class PatchWriter
{
    readonly string state;
    public PatchWriter(string state) { this.state = state; }
    public void Apply(string graphics, string profile, List<Assignment> rows, List<Mapping> mappings, System.Threading.CancellationToken ct = default, Action<string>? progress = null, IProgress<TaskProgress>? taskProgress = null, bool special=false)
    {
        if(rows.Any(a=>!Catalog.IsFm24Face(a.Face)))throw new InvalidOperationException("FM24 只使用男性球员及男女职员头像。");
        if(rows.Any(a=>!a.OutputKeys.Any()||a.OutputKeys.Any(key=>key!=a.Person.Id.ToString()&&key!="r-"+a.Person.Id)))throw new InvalidOperationException("头像映射 ID 与人物不一致，未写入。");
        var output = Path.Combine(graphics, "FM24FaceAssistant",special?"special":""); Directory.CreateDirectory(output);
        var targets = rows.SelectMany(a => a.OutputKeys.SelectMany(key => new[] { $"graphics/pictures/person/{key}/portrait", $"graphics/pictures/person/{key}/icon" })).ToHashSet();
        var changes = new List<Change>();
        var active = Path.Combine(state, special?"active-special.json":"active.json"); var previous = PatchJournal.LoadActive(active);
        if (previous != null && previous.Profile.Length > 0 && previous.Profile != profile) throw new IOException("其他存档补丁仍激活。请先停用当前补丁再切换。");
        var external = new List<Change>();
        var conflicts=mappings.Where(m => targets.Contains(m.Target)).Select(m => m.Config).Concat(previous?.External.Select(c => c.Path) ?? Array.Empty<string>()).Distinct().ToList();
        int conflictIndex=0;
        foreach(var file in conflicts)
        {
            ct.ThrowIfCancellationRequested();taskProgress?.Report(new("检查外部配置",conflictIndex++,conflicts.Count,Path.GetFileName(file)));
            var before = File.ReadAllText(file); var prior = previous?.External.FirstOrDefault(c => c.Path == file);
            if (prior != null && before != prior.After) throw new IOException("外部配置已变化，请先备份并处理冲突：" + file);
            var original = prior?.Before ?? before; var doc = Parse(original);
            foreach (var element in doc.Descendants("record").Where(e => targets.Contains((string?)e.Attribute("to") ?? "")).ToList()) element.Remove();
            var after = doc.ToString(); changes.Add(new(file, before, after)); external.Add(new(file, original, after));
        }
        var maps = new XElement("list", new XAttribute("id", "maps"));
        int prepared=0;
        foreach (var a in rows)
        {
            ct.ThrowIfCancellationRequested(); taskProgress?.Report(new("生成头像",prepared,rows.Count,a.Person.Name));
            var stem = a.Face.Hash; var portrait = Path.Combine(output, stem + ".png"); var icon = Path.Combine(output, stem + "-icon.png");
            SaveImage(a.Face.Path, portrait, 250); SaveImage(a.Face.Path, icon, 25);
            foreach(var key in a.OutputKeys) { maps.Add(new XElement("record", new XAttribute("from", stem), new XAttribute("to", $"graphics/pictures/person/{key}/portrait")));
            maps.Add(new XElement("record", new XAttribute("from", stem + "-icon"), new XAttribute("to", $"graphics/pictures/person/{key}/icon"))); }
            taskProgress?.Report(new("生成头像",++prepared,rows.Count,a.Person.Name));
        }
        var config = Path.Combine(output, "config.xml"); var text = new XDocument(new XElement("record", new XElement("boolean", new XAttribute("id", "preload"), new XAttribute("value", "false")), new XElement("boolean", new XAttribute("id", "amap"), new XAttribute("value", "false")), maps)).ToString();
        changes.Add(new(config, File.Exists(config) ? File.ReadAllText(config) : null, text));
        changes.Add(new(active, File.Exists(active) ? File.ReadAllText(active) : null, PatchJournal.ActiveText(state,profile,external,ct)));
        var record = Path.Combine(state, (special?"special-rows-":"rows-") + Files.Hash(profile) + ".json"); changes.Add(new(record, File.Exists(record) ? File.ReadAllText(record) : null, System.Text.Json.JsonSerializer.Serialize(rows, Files.Json)));
        ct.ThrowIfCancellationRequested(); progress?.Invoke("正在保存，请稍候…"); Commit(changes, special?"特殊角色头像":"头像补丁",ct,taskProgress);
    }
    public static XDocument Parse(string text) { using var sr = new StringReader(text); using var xr = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }); return XDocument.Load(xr, LoadOptions.PreserveWhitespace); }
    public void Deactivate(string graphics,bool special=false)
    {
        if(!special)Deactivate(graphics,true);
        var active = Path.Combine(state, special?"active-special.json":"active.json"); var previous = PatchJournal.LoadActive(active); if (previous == null || previous.Profile.Length == 0) return;
        var changes = new List<Change>(); foreach (var c in previous.External) { if (File.ReadAllText(c.Path) != c.After) throw new IOException("外部配置已变化，停止恢复：" + c.Path); changes.Add(new(c.Path, c.After, c.Before!)); }
        var config = Path.Combine(graphics, "FM24FaceAssistant", special?"special":"", "config.xml"); if (File.Exists(config)) changes.Add(new(config, File.ReadAllText(config), "<record><list id=\"maps\" /></record>"));
        changes.Add(new(active, File.ReadAllText(active), PatchJournal.ActiveText(state,"",new(),default))); Commit(changes, "停用补丁");
    }
    public void Commit(List<Change> changes, string kind, System.Threading.CancellationToken ct = default, IProgress<TaskProgress>? taskProgress = null)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var c in changes) if ((File.Exists(c.Path) ? File.ReadAllText(c.Path) : null) != c.Before) throw new IOException("文件已被其他程序修改，请重新扫描：" + c.Path);
        var folder = Path.Combine(state, "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")); Directory.CreateDirectory(folder); PatchJournal.Save(folder,changes,ct,taskProgress); Files.Atomic(Path.Combine(folder, "kind.txt"), kind);
        ct.ThrowIfCancellationRequested(); foreach(var change in changes)if((File.Exists(change.Path)?File.ReadAllText(change.Path):null)!=change.Before)throw new IOException("备份期间文件已变化："+change.Path); Files.Atomic(Path.Combine(folder,"applying"),"ok"); var done = new List<Change>(); try { foreach (var c in changes) { taskProgress?.Report(new("保存头像配置",done.Count,changes.Count,Path.GetFileName(c.Path),false)); Files.Atomic(c.Path, c.After); done.Add(c); } Files.Atomic(Path.Combine(folder, "complete"), "ok");taskProgress?.Report(new("保存头像配置",changes.Count,changes.Count,null,false)); } catch { foreach (var c in done.AsEnumerable().Reverse()) { if (c.Before == null) File.Delete(c.Path); else Files.Atomic(c.Path, c.Before); } throw; }
    }
    public void RecoverInterrupted()
    {
        var root=Path.Combine(state,"backups");if(!Directory.Exists(root))return;
        foreach(var directory in Directory.GetDirectories(root).Order()){
            if(File.Exists(Path.Combine(directory,"complete"))||File.Exists(Path.Combine(directory,"recovered")))continue;
            if(File.Exists(Path.Combine(directory,"transaction.json"))&&!File.Exists(Path.Combine(directory,"applying"))){Files.Atomic(Path.Combine(directory,"recovered"),"未开始写入");continue;}
            var changes=PatchJournal.Load(directory);if(changes==null)continue;
            foreach(var change in changes){var now=File.Exists(change.Path)?File.ReadAllText(change.Path):null;if(now!=change.Before&&now!=change.After)throw new IOException("中断任务的文件已被修改，请在备份目录检查："+change.Path);}
            foreach(var change in changes.AsEnumerable().Reverse()){var now=File.Exists(change.Path)?File.ReadAllText(change.Path):null;if(now==change.After){if(change.Before==null)File.Delete(change.Path);else Files.Atomic(change.Path,change.Before);}}
            Files.Atomic(Path.Combine(directory,"recovered"),"ok");
        }
    }
    public void Undo(string? kind = null)
    {
        var backups = Path.Combine(state, "backups"); if (!Directory.Exists(backups)) throw new IOException("没有可恢复的记录。");
        var dir = Directory.GetDirectories(backups).OrderDescending().FirstOrDefault(d => File.Exists(Path.Combine(d, "complete")) && !File.Exists(Path.Combine(d, "restored")) && (kind == null || File.ReadAllText(Path.Combine(d, "kind.txt")).Trim('\uFEFF') == kind));
        if (dir == null) throw new IOException("没有可恢复的记录。"); var changes = PatchJournal.Load(dir)??throw new IOException("备份清单缺失。");
        foreach (var c in changes) if (!File.Exists(c.Path) || File.ReadAllText(c.Path) != c.After) throw new IOException("备份后文件又被修改，未覆盖该文件：" + c.Path);
        foreach (var c in changes.AsEnumerable().Reverse()) { if (c.Before == null) File.Delete(c.Path); else Files.Atomic(c.Path, c.Before); }
        Files.Atomic(Path.Combine(dir, "restored"), "ok");
    }
    static void SaveImage(string input, string output, int size)
    {
        if (File.Exists(output)) return; var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(Path.GetFullPath(input)); bitmap.EndInit(); bitmap.Freeze();
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) { var scale = Math.Min((double)size / bitmap.PixelWidth, (double)size / bitmap.PixelHeight); var w = bitmap.PixelWidth * scale; var h = bitmap.PixelHeight * scale; context.DrawImage(bitmap, new System.Windows.Rect((size - w) / 2, size - h, w, h)); }
        var render = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); render.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render)); var temporary = output + ".tmp"; using (var stream = File.Create(temporary)) encoder.Save(stream); File.Move(temporary, output, true);
    }
}
