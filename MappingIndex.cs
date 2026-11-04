using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Microsoft.Data.Sqlite;
namespace FM24FaceAssistant;
public sealed class MappingIndex : IDisposable
{
    readonly string root; readonly FileSystemWatcher watcher; bool discover=true;
    readonly Dictionary<string,string> versions=new(StringComparer.OrdinalIgnoreCase); List<string> files=new(); List<Mapping>? snapshot;
    readonly Dictionary<string,(string Version,List<Mapping> Rows)> previews=new(StringComparer.OrdinalIgnoreCase);
    public MappingIndex(string root){this.root=root;Directory.CreateDirectory(root);watcher=new(root,"config.xml"){IncludeSubdirectories=true,EnableRaisingEvents=true};watcher.Created+=(_,e)=>{if(!Own(e.FullPath))discover=true;};watcher.Deleted+=(_,e)=>{if(!Own(e.FullPath))discover=true;};watcher.Renamed+=(_,e)=>{if(!Own(e.FullPath))discover=true;};watcher.Error+=(_,_)=>discover=true;}
    bool Own(string path)=>Path.GetRelativePath(root,path).Split(Path.DirectorySeparatorChar)[0].Equals("FM24FaceAssistant",StringComparison.OrdinalIgnoreCase);
    public List<Mapping> Read(CancellationToken ct,Action<string> progress,bool rebuild=false)
    {
        SQLitePCL.Batteries_V2.Init();using var db=new SqliteConnection(Files.Sqlite(Path.Combine(Files.State,"mapping-index.db")));db.Open();
        using(var c=db.CreateCommand()){c.CommandText="CREATE TABLE IF NOT EXISTS configs(path TEXT PRIMARY KEY,version TEXT,payload TEXT); CREATE TABLE IF NOT EXISTS targets(config TEXT,f TEXT,t TEXT,image TEXT,valid INTEGER); CREATE INDEX IF NOT EXISTS targets_id ON targets(t); CREATE TABLE IF NOT EXISTS prepared(config TEXT PRIMARY KEY,version TEXT);";c.ExecuteNonQuery();}
        bool changed=false;
        if(discover||rebuild){files=Discover(root,ct).ToList();discover=false;changed=true;}
        var result=new List<Mapping>();int index=0;
        var inventories=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        bool Exists(string path){var folder=Path.GetDirectoryName(path)!;if(!inventories.TryGetValue(folder,out var names)){names=new(StringComparer.OrdinalIgnoreCase);if(Directory.Exists(folder))foreach(var name in Directory.EnumerateFiles(folder)){ct.ThrowIfCancellationRequested();names.Add(Path.GetFileName(name));}inventories[folder]=names;}return names.Contains(Path.GetFileName(path));}
        foreach(var file in files){ct.ThrowIfCancellationRequested();var info=new FileInfo(file);if(!info.Exists){discover=true;continue;}var version=info.Length+":"+info.LastWriteTimeUtc.Ticks;
            if(snapshot!=null&&!rebuild&&versions.GetValueOrDefault(file)==version)continue;
            using var select=db.CreateCommand();select.CommandText="SELECT version,payload FROM configs WHERE path=$p";select.Parameters.AddWithValue("$p",file);string? oldVersion=null,payload=null;using(var r=select.ExecuteReader()){if(r.Read()){oldVersion=r.GetString(0);payload=r.GetString(1);}}
            if(rebuild||oldVersion!=version||payload==null){progress($"检查映射 {++index}/{files.Count}：{Path.GetFileName(Path.GetDirectoryName(file))}");var rows=new List<Mapping>();using var reader=XmlReader.Create(file,new XmlReaderSettings{DtdProcessing=DtdProcessing.Ignore,XmlResolver=null});int records=0;
                while(reader.Read()){ct.ThrowIfCancellationRequested();if(reader.NodeType!=XmlNodeType.Element||reader.Name!="record"||reader.GetAttribute("to") is not string to||!to.StartsWith("graphics/pictures/person/"))continue;
                    var from=reader.GetAttribute("from")??"";var stem=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,from.Replace('/',Path.DirectorySeparatorChar)));var image=new[]{stem+".png",stem+".jpg",stem+".jpeg",stem}.FirstOrDefault(Exists);rows.Add(new(file,from,to,image));if(++records%2000==0)progress($"检查映射：{records:N0} 条 · {Path.GetFileName(Path.GetDirectoryName(file))}");}
                payload=System.Text.Json.JsonSerializer.Serialize(rows,Files.Json);ct.ThrowIfCancellationRequested();using var save=db.CreateCommand();save.CommandText="INSERT OR REPLACE INTO configs VALUES($p,$v,$d)";save.Parameters.AddWithValue("$p",file);save.Parameters.AddWithValue("$v",version);save.Parameters.AddWithValue("$d",payload);save.ExecuteNonQuery();changed=true;result.AddRange(rows);
            }
            versions[file]=version;
        }
        // Re-read all cached payloads if any config changed so no earlier unchanged file is lost.
        if(changed||snapshot==null){result.Clear();foreach(var file in files){ct.ThrowIfCancellationRequested();using var c=db.CreateCommand();c.CommandText="SELECT payload FROM configs WHERE path=$p";c.Parameters.AddWithValue("$p",file);if(c.ExecuteScalar() is string json)result.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<Mapping>>(json,Files.Json)!);}snapshot=result;}
        using(var transaction=db.BeginTransaction()){
        foreach(var file in files){ct.ThrowIfCancellationRequested();using var probe=db.CreateCommand();probe.Transaction=transaction;probe.CommandText="SELECT version FROM prepared WHERE config=$p";probe.Parameters.AddWithValue("$p",file);var version=versions.GetValueOrDefault(file);if(probe.ExecuteScalar() as string==version)continue;
            progress("准备按 ID 查询的映射索引…");using(var clear=db.CreateCommand()){clear.Transaction=transaction;clear.CommandText="DELETE FROM targets WHERE config=$p";clear.Parameters.AddWithValue("$p",file);clear.ExecuteNonQuery();}
            using var insert=db.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT INTO targets VALUES($c,$f,$t,$i,$v)";foreach(var key in new[]{"$c","$f","$t","$i","$v"})insert.Parameters.AddWithValue(key,"");int indexed=0;foreach(var row in snapshot!.Where(m=>m.Config==file)){ct.ThrowIfCancellationRequested();if(++indexed%5000==0)progress($"建立 ID 索引：{indexed:N0} 条");insert.Parameters[0].Value=file;insert.Parameters[1].Value=row.From;insert.Parameters[2].Value=row.Target;insert.Parameters[3].Value=(object?)row.Image??DBNull.Value;insert.Parameters[4].Value=row.Valid?1:0;insert.ExecuteNonQuery();}
            using var mark=db.CreateCommand();mark.Transaction=transaction;mark.CommandText="INSERT OR REPLACE INTO prepared VALUES($p,$v)";mark.Parameters.AddWithValue("$p",file);mark.Parameters.AddWithValue("$v",version??"");mark.ExecuteNonQuery();
        }transaction.Commit();}
        return snapshot!;
    }
    public List<Mapping> Find(IEnumerable<string> targets,CancellationToken ct,Action<string> progress,bool allowBuild=true,IProgress<TaskProgress>? taskProgress=null)
    {
        SQLitePCL.Batteries_V2.Init();
        bool ready=true;using(var db=new SqliteConnection(Files.Sqlite(Path.Combine(Files.State,"mapping-index.db")))){db.Open();try{using var cmd=db.CreateCommand();cmd.CommandText="SELECT c.path,c.version,p.version FROM configs c LEFT JOIN prepared p ON c.path=p.config";using var reader=cmd.ExecuteReader();var known=new HashSet<string>(StringComparer.OrdinalIgnoreCase);while(reader.Read()){ct.ThrowIfCancellationRequested();var path=reader.GetString(0);if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;known.Add(path);var info=new FileInfo(path);if(!info.Exists||reader.IsDBNull(2)||reader.GetString(1)!=reader.GetString(2)||reader.GetString(1)!=info.Length+":"+info.LastWriteTimeUtc.Ticks)ready=false;}if(discover){files=Discover(root,ct).ToList();discover=false;if(!known.SetEquals(files))ready=false;}if(known.Count==0)ready=false;}catch(SqliteException){ready=false;}}
        if(!ready&&!allowBuild)return Preview(targets,ct);
        if(!ready)Read(ct,progress);
        var result=new List<Mapping>();using var connection=new SqliteConnection(Files.Sqlite(Path.Combine(Files.State,"mapping-index.db")));connection.Open();var targetArray=targets.Distinct().ToArray();int checkedTargets=0;foreach(var chunk in targetArray.Chunk(300)){taskProgress?.Report(new("检查头像映射",checkedTargets,targetArray.Length));checkedTargets+=chunk.Length;ct.ThrowIfCancellationRequested();using var cmd=connection.CreateCommand();var names=chunk.Select((_,i)=>"$p"+i).ToArray();cmd.CommandText="SELECT config,f,t,image,valid FROM targets WHERE t IN ("+string.Join(',',names)+")";for(int i=0;i<chunk.Length;i++)cmd.Parameters.AddWithValue(names[i],chunk[i]);using var reader=cmd.ExecuteReader();while(reader.Read()){var file=reader.GetString(0);if(file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){var from=reader.GetString(1);var stem=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,from.Replace('/',Path.DirectorySeparatorChar)));var image=new[]{stem+".png",stem+".jpg",stem+".jpeg",stem}.FirstOrDefault(File.Exists);result.Add(new(file,from,reader.GetString(2),image));}}}
        taskProgress?.Report(new("检查头像映射",targetArray.Length,targetArray.Length));return result;
    }
    List<Mapping> Preview(IEnumerable<string> targets,CancellationToken ct)
    {
        // A stale full index must not turn an existing portrait into a missing one.
        // Read only the requested records; keep this cache separate from the full index.
        if(discover){files=Discover(root,ct).ToList();discover=false;}
        var version=string.Join("|",files.Select(file=>{ct.ThrowIfCancellationRequested();var info=new FileInfo(file);return file+":"+info.Length+":"+info.LastWriteTimeUtc.Ticks;}));
        var requested=targets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missing=requested.Where(t=>!previews.TryGetValue(t,out var cached)||cached.Version!=version).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(missing.Count>0){
            var rows=missing.ToDictionary(t=>t,_=>new List<Mapping>(),StringComparer.OrdinalIgnoreCase);
            foreach(var file in files){
                ct.ThrowIfCancellationRequested();
                using var reader=XmlReader.Create(file,new XmlReaderSettings{DtdProcessing=DtdProcessing.Ignore,XmlResolver=null});
                while(reader.Read()){
                    ct.ThrowIfCancellationRequested();
                    if(reader.NodeType!=XmlNodeType.Element||reader.Name!="record"||reader.GetAttribute("to") is not string target||!missing.Contains(target))continue;
                    var from=reader.GetAttribute("from")??"";
                    rows[target].Add(new(file,from,target,null));
                }
            }
            ct.ThrowIfCancellationRequested();
            if(previews.Count+missing.Count>128)previews.Clear();
            foreach(var (target,matches) in rows)previews[target]=(version,matches);
        }
        return requested.SelectMany(t=>previews[t].Rows).Select(m=>{
            ct.ThrowIfCancellationRequested();
            var stem=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(m.Config)!,m.From.Replace('/',Path.DirectorySeparatorChar)));
            return new Mapping(m.Config,m.From,m.Target,new[]{stem+".png",stem+".jpg",stem+".jpeg",stem}.FirstOrDefault(File.Exists));
        }).ToList();
    }
    static IEnumerable<string> Discover(string root,CancellationToken ct){var pending=new Stack<string>();pending.Push(root);while(pending.Count>0){ct.ThrowIfCancellationRequested();var folder=pending.Pop();var config=Path.Combine(folder,"config.xml");if(File.Exists(config))yield return config;foreach(var sub in Directory.EnumerateDirectories(folder)){ct.ThrowIfCancellationRequested();if((File.GetAttributes(sub)&FileAttributes.ReparsePoint)==0&&!Path.GetRelativePath(root,sub).Split(Path.DirectorySeparatorChar)[0].Equals("FM24FaceAssistant",StringComparison.OrdinalIgnoreCase))pending.Push(sub);}}}
    public void Dispose()=>watcher.Dispose();
}
