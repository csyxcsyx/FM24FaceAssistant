using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FM24FaceAssistant;

public sealed class Storage : IDisposable
{
    readonly SqliteConnection db;
    public Storage(string root)
    {
        SQLitePCL.Batteries_V2.Init(); Directory.CreateDirectory(root); db = new(Files.Sqlite(Path.Combine(root, "state.db"))); db.Open();
        using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TABLE IF NOT EXISTS assignments(profile TEXT, identity TEXT, payload TEXT, PRIMARY KEY(profile,identity)); CREATE TABLE IF NOT EXISTS people(profile TEXT PRIMARY KEY,payload TEXT);"; cmd.ExecuteNonQuery();
    }
    public List<Assignment> Load(string profile) { using var c = db.CreateCommand(); c.CommandText = "SELECT payload FROM assignments WHERE profile=$p"; c.Parameters.AddWithValue("$p", profile); using var r = c.ExecuteReader(); var list = new List<Assignment>(); while (r.Read()) list.Add(JsonSerializer.Deserialize<Assignment>(r.GetString(0), Files.Json)!); return list; }
    public List<Person> People(string profile) { using var c = db.CreateCommand(); c.CommandText = "SELECT payload FROM people WHERE profile=$p"; c.Parameters.AddWithValue("$p", profile); return c.ExecuteScalar() is string s ? JsonSerializer.Deserialize<List<Person>>(s, Files.Json)! : new(); }
    public void SavePeople(string profile, List<Person> people) { using var c = db.CreateCommand(); c.CommandText = "INSERT OR REPLACE INTO people VALUES($p,$v)"; c.Parameters.AddWithValue("$p", profile); c.Parameters.AddWithValue("$v", JsonSerializer.Serialize(people, Files.Json)); c.ExecuteNonQuery(); }
    public void Save(string profile, IEnumerable<Assignment> rows,IProgress<TaskProgress>? progress=null) { var all=rows.ToList();int saved=0;using var tx = db.BeginTransaction(); using (var del = db.CreateCommand()) { del.Transaction = tx; del.CommandText = "DELETE FROM assignments WHERE profile=$p"; del.Parameters.AddWithValue("$p", profile); del.ExecuteNonQuery(); } foreach (var row in all) { progress?.Report(new("保存分配记录",saved++,all.Count,null,false));using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT INTO assignments VALUES($p,$i,$v)"; c.Parameters.AddWithValue("$p", profile); c.Parameters.AddWithValue("$i", row.Person.Identity); c.Parameters.AddWithValue("$v", JsonSerializer.Serialize(row, Files.Json)); c.ExecuteNonQuery(); } tx.Commit(); }
    public List<string> Profiles(){using var c=db.CreateCommand();c.CommandText="SELECT profile FROM people UNION SELECT profile FROM assignments ORDER BY profile";using var reader=c.ExecuteReader();var result=new List<string>();while(reader.Read())result.Add(reader.GetString(0));return result;}
    public void DeleteProfile(string profile){using var tx=db.BeginTransaction();foreach(var table in new[]{"people","assignments"}){using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="DELETE FROM "+table+" WHERE profile=$p";c.Parameters.AddWithValue("$p",profile);c.ExecuteNonQuery();}tx.Commit();}
    public void Dispose() => db.Dispose();
}
