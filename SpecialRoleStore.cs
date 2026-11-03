using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace FM24FaceAssistant;
public static class SpecialRoleStore
{
    public static string RoleName(string name)=>name.Trim() is "主教练" or "主教练（我）"?"主教练（我）":name.Trim();
    public static string PathFor(string profile)=>Path.Combine(Files.State,"special-roles-"+Files.Hash(profile)+".json");
    public static List<Person> Load(string profile,IEnumerable<Assignment> legacy)
    {
        var rows=Files.Read<List<Person>>(PathFor(profile))??new();
        foreach(var row in legacy)if(!rows.Any(p=>p.Id==row.Person.Id))rows.Add(row.Person);
        rows=rows.Select(p=>p with{Club=RoleName(p.Club)}).ToList();return rows;
    }
    public static List<Person> Upsert(List<Person> rows,Person person,long? editing)
    {
        if(person.Id<=0||person.Id>uint.MaxValue||string.IsNullOrWhiteSpace(person.Name)||string.IsNullOrWhiteSpace(person.Club))throw new IOException("请填写有效 UID、英文姓名和职位。");
        person=person with{Club=RoleName(person.Club)};
        if(editing.HasValue&&editing!=person.Id)throw new IOException("已有角色的 UID 不可更改，请另建角色。");
        if(rows.Any(p=>p.Id==person.Id&&p.Id!=editing))throw new IOException("这个 UID 已存在，请从角色列表选择并编辑。");
        if(person.Club=="主教练（我）"&&rows.Any(p=>RoleName(p.Club)==person.Club&&p.Id!=editing))throw new IOException("每份存档只能保存一位主教练（我），请编辑已有角色。");
        return editing.HasValue?rows.Select(p=>p.Id==editing.Value?person:p).ToList():rows.Append(person).ToList();
    }
}
