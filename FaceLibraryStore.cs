using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
namespace FM24FaceAssistant;
public static class FaceLibraryStore
{
    public static List<Face> Load(CancellationToken ct,Action<string> progress,string? libraryRoot=null)
    {
        var path=Path.Combine(Files.State,"catalog.json");if(!File.Exists(path))return new();var info=new FileInfo(path);string version=info.Length+":"+info.LastWriteTimeUtc.Ticks;
        SQLitePCL.Batteries_V2.Init();using var db=new SqliteConnection(Files.Sqlite(Path.Combine(Files.State,"library.db")));db.Open();using(var schema=db.CreateCommand()){schema.CommandText="CREATE TABLE IF NOT EXISTS version(value TEXT); CREATE TABLE IF NOT EXISTS faces(path TEXT,g TEXT,k TEXT,sex TEXT,hash TEXT,visual TEXT);";schema.ExecuteNonQuery();}
        using var check=db.CreateCommand();check.CommandText="SELECT value FROM version LIMIT 1";
        if(check.ExecuteScalar() as string!=version){progress("首次建立快速图库缓存…");var rows=Files.Read<List<Face>>(path)??new();using var transaction=db.BeginTransaction();using(var clear=db.CreateCommand()){clear.Transaction=transaction;clear.CommandText="DELETE FROM faces; DELETE FROM version;";clear.ExecuteNonQuery();}using var insert=db.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT INTO faces VALUES($p,$g,$k,$s,$h,$v)";foreach(var key in new[]{"$p","$g","$k","$s","$h","$v"})insert.Parameters.Add(key,SqliteType.Text);int count=0;foreach(var face in rows){ct.ThrowIfCancellationRequested();if(!Catalog.IsFm24Face(face))continue;insert.Parameters[0].Value=face.Path;insert.Parameters[1].Value=face.Group;insert.Parameters[2].Value=face.Kind;insert.Parameters[3].Value=face.Gender;insert.Parameters[4].Value=face.Hash;insert.Parameters[5].Value=(object?)face.VisualHash??DBNull.Value;insert.ExecuteNonQuery();if(++count%5000==0)progress($"建立快速缓存：{count:N0}/{rows.Count:N0}");}using var mark=db.CreateCommand();mark.Transaction=transaction;mark.CommandText="INSERT INTO version VALUES($v)";mark.Parameters.AddWithValue("$v",version);mark.ExecuteNonQuery();ct.ThrowIfCancellationRequested();transaction.Commit();return rows.FindAll(f=>Catalog.IsLibraryFace(f,libraryRoot));}
        var result=new List<Face>();using var read=db.CreateCommand();read.CommandText="SELECT path,g,k,sex,hash,visual FROM faces";using var reader=read.ExecuteReader();while(reader.Read()){ct.ThrowIfCancellationRequested();result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.IsDBNull(5)?null:reader.GetString(5)));}return result.FindAll(f=>Catalog.IsLibraryFace(f,libraryRoot));
    }
}
