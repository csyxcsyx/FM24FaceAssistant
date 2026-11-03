using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FM24FaceAssistant;

public static class CountryNames
{
    static readonly Dictionary<string,string> codes=new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,string> labels=new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,string> english=new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,string> subdivisions=new(StringComparer.OrdinalIgnoreCase){{"England","England"},{"英格兰","England"},{"Scotland","Scotland"},{"苏格兰","Scotland"},{"Wales","Wales"},{"威尔士","Wales"},{"Northern Ireland","Northern Ireland"},{"北爱尔兰","Northern Ireland"}};
    static readonly Dictionary<string,string> subdivisionLabels=new(StringComparer.OrdinalIgnoreCase){{"England","英格兰"},{"Scotland","苏格兰"},{"Wales","威尔士"},{"Northern Ireland","北爱尔兰"}};
    static CountryNames()
    {
        foreach(var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))try{var region=new RegionInfo(culture.Name);english.TryAdd(region.TwoLetterISORegionName,region.EnglishName);foreach(var key in new[]{region.EnglishName,region.NativeName,region.TwoLetterISORegionName,region.ThreeLetterISORegionName})codes.TryAdd(key,region.TwoLetterISORegionName);}catch(ArgumentException){}
        foreach(var entry in new[]{"GB|England|英格兰|Scotland|苏格兰|Wales|威尔士|Northern Ireland|北爱尔兰|United Kingdom|英国","IE|Republic of Ireland|Ireland|爱尔兰","CN|China PR|China|中国|中国大陆","HK|Hong Kong (China PR)|Hong Kong (China)|Hong Kong|香港","MO|Macau (China PR)|Macau|澳门","TW|Chinese Taipei (China PR)|Chinese Taipei|Taiwan|中国台北|台湾","KR|Korea Republic|South Korea|Korea|韩国","KP|North Korea|朝鲜","US|U.S.A.|USA|United States|美国","DE|Germany|德国","FR|France|法国","IT|Italy|意大利","ES|Spain|西班牙","PT|Portugal|葡萄牙","NL|Netherlands|荷兰","BE|Belgium|比利时","AT|Austria|奥地利","CH|Switzerland|瑞士","DK|Denmark|丹麦","SE|Sweden|瑞典","NO|Norway|挪威","FI|Finland|芬兰","IS|Iceland|冰岛","RU|Russia|俄罗斯","UA|Ukraine|乌克兰","BY|Belarus|白俄罗斯","CZ|Czech Republic|Czechia|捷克","SK|Slovakia|斯洛伐克","HU|Hungary|匈牙利","PL|Poland|波兰","RO|Romania|罗马尼亚","BG|Bulgaria|保加利亚","RS|Serbia|塞尔维亚","HR|Croatia|克罗地亚","BA|Bosnia & Herzegovina|Bosnia and Herzegovina|波黑","MK|North Macedonia|Macedonia|北马其顿","XK|Kosovo|科索沃","ME|Montenegro|黑山","SI|Slovenia|斯洛文尼亚","GR|Greece|希腊","AL|Albania|阿尔巴尼亚","TR|Türkiye|Turkey|土耳其","IL|Israel|以色列","JP|Japan|日本","VN|Vietnam|越南","MY|Malaysia|马来西亚","BN|Brunei Darussalam|Brunei|文莱","BR|Brazil|巴西","AR|Argentina|阿根廷","CA|Canada|加拿大","AU|Australia|澳大利亚","NZ|New Zealand|新西兰","IN|India|印度","IR|Iran|伊朗","SA|Saudi Arabia|沙特阿拉伯","AE|U.A.E.|United Arab Emirates|阿联酋","CD|DR Congo|Democratic Republic of Congo|刚果民主共和国","CI|Ivory Coast|Côte d’Ivoire|Cote d'Ivoire|科特迪瓦"})
        {var parts=entry.Split('|');foreach(var alias in parts.Skip(1))codes[alias]=parts[0];labels[parts[0]]=parts.Last();}
    }
    public static string Code(string nation)=>codes.GetValueOrDefault(nation.Trim(),nation.Trim().ToUpperInvariant());
    public static string English(string nation)=>subdivisions.TryGetValue(nation,out var subdivision)?subdivision:nation.Any(c=>c>='\u3400'&&c<='\u9fff')?english.GetValueOrDefault(Code(nation),"Unknown"):nation;
    public static string Label(string nation)=>subdivisions.TryGetValue(nation,out var subdivision)?subdivisionLabels[subdivision]:labels.GetValueOrDefault(Code(nation),nation);
    public static bool Matches(string nation,string query)=>subdivisions.TryGetValue(query.Trim(),out var subdivision)?subdivisions.GetValueOrDefault(nation)==subdivision:PickerOption.Contains(nation,query)||PickerOption.Contains(Label(nation),query)||(!string.IsNullOrWhiteSpace(query)&&Code(nation)==Code(query));
}
