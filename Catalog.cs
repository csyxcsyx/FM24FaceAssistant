using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FM24FaceAssistant;

public record Mapping(string Config, string From, string Target, string? Image) { public bool Valid { get; init; } = Image != null; }
public static class Catalog
{
    public static bool IsFm24Face(Face face) => face.Kind == "职员" || (face.Kind == "球员" && face.Gender == "男");
    public static bool IsLibraryFace(Face face,string? root=null)=>IsFm24Face(face)&&(RegionResolver.IsKnownGroup(face.Group)||(!string.IsNullOrWhiteSpace(root)&&File.Exists(Path.Combine(root,"faces.json"))));
    public static List<Mapping> Scan(string graphics, CancellationToken ct)
    {
        var list = new List<Mapping>(); if (!Directory.Exists(graphics)) return list;
        foreach (var file in Directory.EnumerateFiles(graphics, "config.xml", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
        {
            if (Path.GetRelativePath(graphics, file).Split(Path.DirectorySeparatorChar)[0].Equals("FM24FaceAssistant", StringComparison.OrdinalIgnoreCase)) continue;
            ct.ThrowIfCancellationRequested();
            using var r = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            while (r.Read()) { ct.ThrowIfCancellationRequested(); if (r.NodeType == XmlNodeType.Element && r.Name == "record" && r.GetAttribute("to") is string to && to.StartsWith("graphics/pictures/person/"))
            {
                var from = r.GetAttribute("from") ?? ""; var stem = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, from.Replace('/', Path.DirectorySeparatorChar)));
                var image = new[] { stem, stem + ".png", stem + ".jpg", stem + ".jpeg" }.FirstOrDefault(File.Exists);
                list.Add(new(file, from, to, image));
            }}
        }
        return list;
    }
    public static List<Face> Faces(string root, CancellationToken ct, IProgress<string>? progress = null)
    {
        if (!Directory.Exists(root)) throw new IOException("请选择已解压的 NG_Regens 图库文件夹。");
        // Explicit manifest supports any author layout without guessing staff gender or ethnicity from photos.
        var manifest = Path.Combine(root, "faces.json"); if (File.Exists(manifest))
        {
            var rows = Files.Read<List<Face>>(manifest) ?? new(); var verified = new List<Face>();
            foreach(var f in rows.Where(IsFm24Face)) { ct.ThrowIfCancellationRequested(); var path=Path.GetFullPath(Path.Combine(root,f.Path)); verified.Add(f with {Path=path, Hash=Digest(path), VisualHash=PixelDigest(path)}); if(verified.Count%500==0)progress?.Report($"校验图库：{verified.Count:N0} 张"); }
            if(verified.Count==0)throw new IOException("图库清单中没有 FM24 可用头像，请下载官方图库或选择完整素材。");
            return verified;
        }
        var list = new List<Face>(); int count = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            ct.ThrowIfCancellationRequested(); if (!new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
            var dirs = Path.GetRelativePath(root, path).Replace('\\', '/').Split('/'); var lower = string.Join('/', dirs[..^1]).ToLowerInvariant();
            string kind = lower.Contains("staff") ? "职员" : "球员";
            string gender = lower.Contains("female") || lower.Contains("women") || dirs[..^1].Any(d=>d.Equals("WF",StringComparison.OrdinalIgnoreCase)) ? "女" : lower.Contains("male") || lower.Contains("men") ? "男" : kind == "球员" ? "男" : "未知";
            if(kind=="球员" && gender=="女")continue;
            var group = dirs.Length > 1 ? dirs[^2] : "未分类";
            if(!RegionResolver.IsKnownGroup(group))continue;
            list.Add(new(path, group, kind, gender, Digest(path),PixelDigest(path))); if (++count % 500 == 0) progress?.Report($"索引图库：{count:N0} 张");
        }
        if(list.Count==0)throw new IOException("此目录没有可识别的 NG_Regens 头像分类。皮肤、图标和球衣图片不能用作头像图库；请点击“下载 / 接续图库”，或选择解压后的 NG_Regens 文件夹。");
        return list;
    }
    static string Digest(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static string PixelDigest(string path)
    {
        using var stream=File.OpenRead(path);
        var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
        var rgba=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);
        int stride=checked(rgba.PixelWidth*4);var pixels=new byte[checked(stride*rgba.PixelHeight)];rgba.CopyPixels(pixels,stride,0);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(BitConverter.GetBytes(rgba.PixelWidth));hash.AppendData(BitConverter.GetBytes(rgba.PixelHeight));hash.AppendData(pixels);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
public class Matcher
{
    public List<Assignment> Assign(IEnumerable<Person> persons, List<Face> faces, List<Assignment> old, IEnumerable<Mapping> mappings, out List<string> missing, CancellationToken ct = default, bool retainUnlisted = false, IProgress<TaskProgress>? progress = null, ICollection<MatchIssue>? issues=null)
    {
        faces=faces.Where(Catalog.IsFm24Face).ToList();
        var catalog=faces.DistinctBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);
        var current = persons.ToList(); var identities = current.ToDictionary(p => p.Identity); var result = old.Where(a => (retainUnlisted || identities.ContainsKey(a.Person.Identity)) && File.Exists(a.Face.Path) && Catalog.IsFm24Face(a.Face)).Select(a => a with { Person = identities.GetValueOrDefault(a.Person.Identity) ?? a.Person, Face=catalog.GetValueOrDefault(a.Face.Path)??a.Face }).ToList();
        var covered = mappings.Where(m => m.Valid).Select(m => m.Target).ToHashSet(); missing = new(); var done = result.Select(a => a.Person.Identity).ToHashSet();
        var usage = result.GroupBy(a => a.Face.UniqueKey).ToDictionary(g => g.Key, g => g.Count());
        var basePools = faces.GroupBy(f => (f.Kind, f.Gender)).ToDictionary(g => g.Key, g => g.ToList());
        var resolved=new Dictionary<string,List<List<Face>>>();
        var queues = new Dictionary<string, PriorityQueue<Face, (int, double)>>();
        int processed=0;
        foreach (var p in current)
        {
            ct.ThrowIfCancellationRequested(); progress?.Report(new("匹配缺失头像",++processed,current.Count,p.Name));
            if (done.Contains(p.Identity)) continue;
            
            var missingKeys=p.Keys.Where(key=>!covered.Contains($"graphics/pictures/person/{key}/portrait")).ToArray();
            if (missingKeys.Length==0) continue;
            var gender=p.MatchGender;
            var pool = basePools.GetValueOrDefault((p.Kind, gender)) ?? new List<Face>();
            if (pool.Count == 0) { var error=$"图库没有{(gender=="女"?"女性":"男性")}{p.Kind}素材，请添加对应素材。";missing.Add($"{p.Name} ({p.Id})：{error}");issues?.Add(new(p,error));continue; }
            var signature=p.Kind+"|"+gender+"|"+(p.Group??p.Nation);
            if(!resolved.TryGetValue(signature,out var tiers)){tiers=RegionResolver.Tiers(p,pool);resolved[signature]=tiers;}
            if(tiers.Count==0){var error="国籍未识别，或兼容分类没有素材；可手动选图。";missing.Add($"{p.Name}：{error}");issues?.Add(new(p,error));continue;}
            PriorityQueue<Face,(int,double)>? selectedQueue=null;
            for(int tier=0;tier<tiers.Count;tier++){
                var queueKey=signature+"|"+tier;
                if(!queues.TryGetValue(queueKey,out var queue)){queue=new();foreach(var face in tiers[tier])queue.Enqueue(face,(usage.GetValueOrDefault(face.UniqueKey),Random.Shared.NextDouble()));queues[queueKey]=queue;}
                while(queue.TryPeek(out var candidate,out var priority)&&usage.GetValueOrDefault(candidate.UniqueKey)!=priority.Item1){queue.Dequeue();queue.Enqueue(candidate,(usage.GetValueOrDefault(candidate.UniqueKey),Random.Shared.NextDouble()));}
                selectedQueue=queue;
                if(queue.TryPeek(out _,out var top)&&top.Item1==0)break;
            }
            var chosen=selectedQueue!.Dequeue();int used=usage.GetValueOrDefault(chosen.UniqueKey);usage[chosen.UniqueKey]=used+1;selectedQueue.Enqueue(chosen,(used+1,Random.Shared.NextDouble()));
            var reason=RegionResolver.Describe(p,chosen.Group);
            if(missingKeys.Length<p.Keys.Count())reason+=" · 保留已有映射，仅补 "+string.Join(", ",missingKeys);
            if(p.Kind=="职员"&&p.Gender is not ("男" or "女"))reason+=" · "+NameGender.Resolve(p).Source;
            result.Add(new(p, chosen, false, reason,missingKeys));
        }
        return result;
    }
}
