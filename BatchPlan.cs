using System;
using System.Collections.Generic;
using System.Linq;

namespace FM24FaceAssistant;

public record MatchIssue(Person Person,string Reason);
public record BatchCard(Person Person,string? Image,string Status,string Detail,string[]? TargetKeys=null)
{
    public string Name=>Person.Name;
    public string Meta=>$"ID {(TargetKeys?.Length==1?TargetKeys[0]:Person.Key)} · {CountryNames.English(Person.Nation)}";
    public string[] Tags=>new[]{"ID "+(TargetKeys?.Length==1?TargetKeys[0]:Person.Key),CountryNames.English(Person.Nation)};
    public string Color=>Status=="未完成"?"#F2B9AC":Status=="待应用"?"#FFE1A3":"#ABD5BF";
    public string Help=>$"{Person.Name}\n{Meta}\n{Person.Kind} / {Person.MatchGender} · {NameGender.Resolve(Person).Source}\n{Detail}";
}
public record BatchPlan(List<BatchCard> Cards,int Pending,int Preserved,int Missing,int Reused,int OutputCount=0,int MissingPlayers=0,int MissingStaff=0,int Compatibility=0)
{
    public static BatchPlan Create(IEnumerable<Person> people,List<Assignment> rows,List<Assignment> previous,IEnumerable<Mapping> mappings,List<MatchIssue> issues)
    {
        var old=previous.ToDictionary(a=>a.Person.Identity,a=>a.Face.UniqueKey);
        var cards=rows.Select(a=>new BatchCard(a.Person,a.Face.Path,old.GetValueOrDefault(a.Person.Identity)==a.Face.UniqueKey?"已保留":"待应用",a.Face.Group+"："+a.Reason.Replace(" · ","；"),a.OutputKeys.ToArray())).ToList();
        var included=cards.Select(c=>c.Person.Identity).ToHashSet();
        var external=mappings.Where(m=>m.Valid&&m.Target.EndsWith("/portrait",StringComparison.Ordinal)).GroupBy(m=>m.Target).ToDictionary(g=>g.Key,g=>g.First());
        foreach(var person in people){if(included.Contains(person.Identity))continue;var face=person.Keys.Select(k=>external.GetValueOrDefault($"graphics/pictures/person/{k}/portrait")).FirstOrDefault(m=>m!=null);if(face!=null)cards.Add(new(person,face.Image,"已保留","保留头像包中的有效头像"));}
        cards.AddRange(issues.Select(i=>new BatchCard(i.Person,null,"未完成",i.Reason)));
        cards=cards.OrderBy(c=>c.Status=="待应用"?0:c.Status=="未完成"?1:2).ThenBy(c=>c.Person.Id).ToList();
        var missingCards=cards.Where(c=>c.Status is "待应用" or "未完成").Where(c=>!old.ContainsKey(c.Person.Identity)&&!c.Person.Keys.Any(k=>external.ContainsKey($"graphics/pictures/person/{k}/portrait"))).ToList();
        return new(cards,cards.Count(c=>c.Status=="待应用"),cards.Count(c=>c.Status=="已保留"),issues.Count,rows.Count-rows.Select(a=>a.Face.UniqueKey).Distinct().Count(),rows.Count,missingCards.Count(c=>c.Person.Kind=="球员"),missingCards.Count(c=>c.Person.Kind=="职员"),cards.Count(c=>c.Status=="待应用")-missingCards.Count(c=>c.Status=="待应用"));
    }
    public List<BatchCard> Page(string status,int page,int size=9)
    {
        if(size<=0)throw new ArgumentOutOfRangeException(nameof(size));
        var filtered=Cards.Where(c=>status=="全部结果"||c.Status==status);return filtered.Skip(Math.Max(0,page)*size).Take(size).ToList();
    }
    public BatchPlan Applied()=>this with{Cards=Cards.Select(c=>c.Status=="待应用"?c with{Status="已应用"}:c).ToList(),Pending=0};
}
