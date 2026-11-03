using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace FM24FaceAssistant;
public record RoleIdentity(long Id,string Name,string Nation,bool? Newgen,string Source)
{
    public string Key=>Newgen==true?"r-"+Id:Id.ToString(CultureInfo.InvariantCulture);
}
public record RoleIdentityMatch(RoleIdentity? Identity,string Message);
public static class RoleIdentityResolver
{
    public static string NameKey(string name)=>new(name.Normalize(NormalizationForm.FormD).Where(c=>char.IsLetterOrDigit(c)).Select(char.ToUpperInvariant).ToArray());
    public static bool SameName(string a,string b)=>NameKey(a)==NameKey(b)&&NameKey(a).Length>0;
    public static RoleIdentityMatch ByName(string name,IEnumerable<RoleIdentity> known)
    {
        if(string.IsNullOrWhiteSpace(name))return new(null,"先填写角色姓名，再匹配已有资料。");
        var key=NameKey(name);var candidates=known.Where(p=>NameKey(p.Name)==key).GroupBy(p=>p.Id).Select(g=>g.First()).ToList();
        return candidates.Count switch{1=>new(candidates[0],"已匹配："+candidates[0].Name+" · "+candidates[0].Key+"（"+candidates[0].Source+"）"),0=>new(null,"已有资料中没有此人。可从游戏复制隐藏 UID 后自动填入。"),_=>new(null,"有多个同名人物，请复制游戏中的准确 UID，程序不会自动选错人。")};
    }
    public static RoleIdentityMatch FromText(string text,IEnumerable<RoleIdentity> known)
    {
        text=text.Trim();if(text.Length==0||text.Length>4096)return new(null,"复制的内容为空或过长，请只复制角色 UID 或姓名与 UID。");
        var pattern=@"(?<![\p{L}\p{N}])(?<key>r-\d{1,10})(?!\d)|\b(?:UID|ID)\s*[:：=]?\s*(?<key>(?:r-)?\d{1,10})(?!\d)";
        var keys=Regex.Matches(text,pattern,RegexOptions.IgnoreCase).Select(m=>m.Groups["key"].Value).ToList();
        if(Regex.IsMatch(text,@"^(?:r-)?\d{1,10}$",RegexOptions.IgnoreCase))keys.Add(text);
        var parsed=keys.Select(key=>{bool prefixed=key.StartsWith("r-",StringComparison.OrdinalIgnoreCase);var number=prefixed?key[2..]:key;return long.TryParse(number,out var id)&&id>0&&id<=uint.MaxValue?new RoleIdentity(id,"","",prefixed?true:null,"游戏复制信息"):null;}).Where(x=>x!=null).Cast<RoleIdentity>().GroupBy(x=>x.Id).Select(g=>g.OrderByDescending(x=>x.Newgen==true).First()).ToList();
        if(parsed.Count!=1)return new(null,parsed.Count>1?"复制内容中有多个不同 UID，请只复制一位角色的信息。":"没有识别到有效 UID。请复制数字、r-编号，或带 ID/UID 标签的信息。");
        var found=parsed[0];var identities=known.Where(x=>x.Id==found.Id).ToList();
        if(identities.Count>0)found=found with{Name=identities[0].Name,Nation=identities[0].Nation,Newgen=found.Newgen??identities[0].Newgen};
        else{var label=Regex.Match(text,@"(?:^|\n)\s*(?:Name|姓名)\s*[:：]\s*(?<name>[^\r\n]+)",RegexOptions.IgnoreCase);if(label.Success)found=found with{Name=label.Groups["name"].Value.Trim()};}
        return new(found,"已识别 UID "+found.Key+(found.Name.Length>0?" · "+found.Name:"；姓名可在角色页填写。"));
    }
}
