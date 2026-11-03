using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace FM24FaceAssistant;

public record GenderEstimate(string Gender, string Source, string Detail);

// Estimates are used only to choose artwork. Person.Gender remains the original data.
public static class NameGender
{
    record Entry(char Gender, string Frequencies);
    sealed class NameData
    {
        public readonly Dictionary<string,List<Entry>> Local = new();
        public readonly Dictionary<string,(double Male,double Female)> Global = new();
    }
    static readonly Lazy<NameData> data = new(Load);
    static readonly ConcurrentDictionary<(string,string),GenderEstimate> cache = new();
    static readonly string[] countryCodes = "GB IE US IT MT PT ES FR BE LU NL @frisia DE AT CH IS DK NO SE FI EE LV LT PL CZ SK HU RO BG BA HR XK MK ME RS SI AL GR RU BY MD UA AM AZ GE @stans TR @arabia IL CN IN JP KR VN @other".Split(' ');
    static readonly HashSet<string> titles = new("mr mrs ms miss dr prof coach sir dame".Split(' '));
    // These established country-dependent usages override global name probabilities.
    static readonly Dictionary<(string,string),char> localUsage = new()
    {
        [("andrea","IT")]='M',[("andrea","DE")]='F',[("andrea","AT")]='F',
        [("nicola","IT")]='M',[("nicola","GB")]='F',[("simone","IT")]='M',[("simone","FR")]='F',
        [("michele","IT")]='M',[("michele","FR")]='F',[("jean","FR")]='M',[("jean","GB")]='F',
        [("lilian","FR")]='M',[("jocelyn","FR")]='M',[("gert","SE")]='M'
    };
    static readonly Dictionary<string,HashSet<string>> surnames = new()
    {
        ["CN"]=new("li wang zhang liu chen yang huang zhao wu zhou xu sun ma zhu hu guo he gao lin luo zheng liang xie song tang han feng yu dong xiao cheng cao yuan deng fu shen zeng peng cai jiang du ding wei pan tian shi".Split(' ')),
        ["KR"]=new("kim lee li park pak choi jung jeong kang cho yoon yun jang lim han shin oh seo hwang an ahn".Split(' ')),
        ["VN"]=new("nguyen tran le pham hoang huynh phan vu vo dang bui do ho ngo duong ly".Split(' '))
    };
    public static GenderEstimate Resolve(Person person)
    {
        if(person.Kind=="球员")return new("男","球员规则","FM24 球员统一使用男性素材。");
        if(person.Gender is "男" or "女")return new(person.Gender,"已有资料","使用名单中的性别资料。");
        return cache.GetOrAdd((person.Name,CountryNames.Code(person.Nation)),key=>Infer(key.Item1,key.Item2));
    }
    public static string Normalize(string text)
    {
        var result=new StringBuilder();
        foreach(char c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if(CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark)continue;
            if(c is '\'' or '’' or '.')continue;
            result.Append(c switch {'ł'=>'l','đ'=>'d','ð'=>'d','ø'=>'o','ı'=>'i',_=>char.IsLetterOrDigit(c)?c:' '});
        }
        return Regex.Replace(result.ToString().Replace("ß","ss").Replace("æ","ae").Replace("œ","oe"),@"\s+"," ").Trim();
    }
    static GenderEstimate Infer(string fullName,string country)
    {
        var name=fullName.Contains(',')?fullName[(fullName.IndexOf(',')+1)..]:fullName;
        var parts=Normalize(name).Split(' ',StringSplitOptions.RemoveEmptyEntries).SkipWhile(x=>titles.Contains(x)).ToArray();
        if(parts.Length==0)return Default();
        if(parts.Length>1&&surnames.TryGetValue(country,out var family)&&family.Contains(parts[0]))parts=parts[1..];
        // Do not use an arbitrary surname as the given name when an initial is all we have.
        if(parts[0].Length<2)return Default();
        var names=data.Value;
        foreach(var candidate in Enumerable.Range(1,Math.Min(3,parts.Length)).Reverse().Select(n=>string.Join(' ',parts.Take(n))))
        {
            if(localUsage.TryGetValue((candidate,country),out var localGender))return Guess(localGender,candidate,"国家姓名规则");
            if(names.Local.TryGetValue(candidate,out var records))
            {
                var localCountry=country is "KZ" or "UZ" or "TJ" or "KG" or "TM"?"@stans":country is "SA" or "AE" or "QA" or "BH" or "OM" or "YE" or "KW" or "IQ" or "SY" or "JO" or "LB"?"@arabia":country;
                int countryIndex=Array.IndexOf(countryCodes,localCountry);
                var supported=countryIndex>=0?records.Where(e=>Weight(e,countryIndex)>0).ToList():new();
                if(supported.Count>0)
                {
                    int best=supported.Max(e=>Weight(e,countryIndex));var strongest=supported.Where(e=>Weight(e,countryIndex)==best).Select(e=>e.Gender).Distinct().ToArray();
                    if(strongest.Length==1&&strongest[0] is 'M' or 'F')return DictionaryGuess(strongest[0],candidate,"国家姓名词典",names);
                    return Default("此名字在对应国家的性别用法不明确。");
                }
                var genders=records.Select(e=>e.Gender).Distinct().ToArray();
                if(genders.Length==1&&genders[0] is 'M' or 'F')return DictionaryGuess(genders[0],candidate,"多语言姓名词典",names);
                // Conflicting country usage remains uncertain without local evidence.
                return Default("此名字有不同性别用法，缺少对应国家的明确记录。");
            }
            if(names.Global.TryGetValue(candidate,out var probability))
            {
                if(probability.Female>=0.9)return Guess('F',candidate,"姓名统计");
                if(probability.Male>=0.9)return Guess('M',candidate,"姓名统计");
                return Default("此名字的男女用法接近。");
            }
        }
        // Explicit language markers, never a generic '-a means female' rule.
        if((country is "MY" or "BN")&&Regex.IsMatch(Normalize(fullName),@"\bbinti\b"))return Guess('F',"binti","姓名结构");
        if((country is "RU" or "UA" or "BY" or "BG")&&Regex.IsMatch(Normalize(fullName),@"(?:ova|eva|yeva|skaya|ская|ова|ева|іна|ина)$"))return Guess('F',fullName,"姓氏结构");
        return Default();
    }
    static int Weight(Entry entry,int index)=>index<entry.Frequencies.Length&&Uri.IsHexDigit(entry.Frequencies[index])?Convert.ToInt32(entry.Frequencies[index].ToString(),16):0;
    static GenderEstimate DictionaryGuess(char gender,string candidate,string method,NameData names)
    {
        if(gender=='F'&&names.Global.TryGetValue(candidate,out var probability)&&probability.Female<0.9)return Default("姓名来源存在分歧或男女混用。");
        return Guess(gender,candidate,method);
    }
    static GenderEstimate Guess(char gender,string name,string method)=>new(gender=='F'?"女":"男","姓名推断",$"{method}：{name}。仅用于头像推荐，可能与实际人物不符；可勾选“全部素材”手动改选职员头像。");
    static GenderEstimate Default(string reason="姓名未命中明确规则。")=>new("男","默认男性",reason+" 按设置使用男性素材；可勾选“全部素材”手动改选。");
    static StreamReader Resource(string name)=>new(Assembly.GetExecutingAssembly().GetManifestResourceStream("FM24FaceAssistant.Data.Names."+name)??throw new IOException("姓名词典缺失："+name),Encoding.UTF8);
    static NameData Load()
    {
        var result=new NameData();using(var reader=Resource("nam_dict.txt"))while(reader.ReadLine() is string line)
        {
            if(line.Length<31||line[0] is '#' or '=')continue;
            var fields=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(fields.Length<2)continue;
            char gender=fields[0].Contains('F')?'F':fields[0].Contains('M')?'M':'?';
            foreach(var separator in new[]{""," ","-"}){var key=Normalize(fields[1].Replace("+",separator));if(!result.Local.TryGetValue(key,out var entries))result.Local[key]=entries=new();var item=new Entry(gender,line[30..].TrimEnd('$'));if(!entries.Contains(item))entries.Add(item);}
        }
        using(var reader=Resource("gender.csv")){reader.ReadLine();while(reader.ReadLine() is string line){var fields=line.Split(',');if(fields.Length!=4)continue;var key=Normalize(fields[0]);if(double.TryParse(fields[2],NumberStyles.Float,CultureInfo.InvariantCulture,out var male)&&double.TryParse(fields[3],NumberStyles.Float,CultureInfo.InvariantCulture,out var female)){if(result.Global.TryGetValue(key,out var existing))result.Global[key]=(Math.Min(existing.Male,male),Math.Min(existing.Female,female));else result.Global[key]=(male,female);}}}
        return result;
    }
}
