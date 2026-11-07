using System;
using System.Collections.Generic;
using System.Linq;
namespace FM24FaceAssistant;
public static class RegionResolver
{
    static readonly Dictionary<string,(string Region,string[] Groups)> countries=new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,string[]> regions=new();
    static readonly Dictionary<string,HashSet<string>> adjacent=new(StringComparer.OrdinalIgnoreCase);
    static RegionResolver()
    {
        Add("Europe","CentralEurope|Anglosphere|Albania|AlbanianGreek|Baltics|EastBalkan|EastSlavic|Finstonia|France|Hungary|Iceland|Ireland|Italia|Netherlands|Poland|Portugal|Romania|Spain|WestBalkan|WestSlavic|Sweden|Denmark|Norway|EasternEurope|NorthCentralEurope|NorthWestEurope|SouthEurope|SouthWestEurope|Scandinavian");
        Add("Asia","China|Japan|Korea|ArabGulf|Armenian|Azerbaijan|Caucasus|CentralAsian|Filipino|Indonesia|Iran|Israel|MainlandSEA|Malaysia|Mashriq|Mongolia|Singapore|SouthAsia|Tajikistan|Turkish|Uzbekistan|Vietnam|MiddleEast|SEA");
        Add("Africa","African|CentralAfrica|EastAfrica|HornOfAfrica|Maghreb|SahelianAfrica|SouthernAfrica|WestAfrica");
        Add("NorthAmerica","CaucasianNA|Afroamerican|Afrocaribbean|Mestizo|LatinAmerica");
        Add("SouthAmerica","BrazilMixed|Brazil|IndigenousSA|Mestizo|NorthernSA|SouthConeSA|LatinAmerica");
        Add("Oceania","PacificIslanders|Anglosphere");
        Map("Asia","China","China PR|Chinese Taipei (China PR)|Hong Kong (China PR)|Macau (China PR)");
        Map("Europe","Ireland|NorthWestEurope","Republic of Ireland");Map("Europe","Denmark|Scandinavian","Faroe Islands");Map("Europe","Anglosphere|SouthWestEurope","Gibraltar");
        Map("Asia","Malaysia|SEA","Brunei Darussalam");Map("Asia","Indonesia|SEA","East Timor");
        Map("Africa","WestAfrica|African","The Gambia|Cape Verde Islands");Map("Africa","CentralAfrica|African","São Tomé and Príncipe");Map("Africa","EastAfrica|African","Zanzibar");Map("Africa","SouthernAfrica|African","Réunion|Mayotte");
        Map("NorthAmerica","Afrocaribbean|LatinAmerica","Anguilla|Antigua and Barbuda|Bonaire|British Virgin Islands|Curaçao|Guadeloupe|Martinique|Montserrat|Saint Kitts and Nevis|Saint Lucia|Saint Martin|Saint Vincent and the Grenadines|Sint Maarten|The Bahamas");
        Map("SouthAmerica","NorthernSA|LatinAmerica","French Guiana");Map("Oceania","PacificIslanders","Guam|Kiribati|Northern Mariana Islands|Tuvalu");
        Map("Europe","Anglosphere|NorthWestEurope","England|英格兰|Scotland|苏格兰|Wales|威尔士|Northern Ireland|北爱尔兰|United Kingdom");
        Map("Europe","CentralEurope|NorthCentralEurope","Germany|德国|Austria|奥地利|Switzerland|瑞士|Liechtenstein|Luxembourg");
        Map("Europe","France|NorthWestEurope","France|法国|Belgium|比利时|Monaco");
        Map("Europe","Italia|SouthEurope","Italy|意大利|San Marino|Malta");
        Map("Europe","Spain|SouthWestEurope","Spain|西班牙|Andorra");
        Map("Europe","Portugal|SouthWestEurope","Portugal|葡萄牙");
        Map("Europe","Netherlands|NorthWestEurope","Netherlands|荷兰");
        Map("Europe","Ireland|NorthWestEurope","Ireland|爱尔兰");
        Map("Europe","Sweden|Scandinavian","Sweden|瑞典"); Map("Europe","Norway|Scandinavian","Norway|挪威"); Map("Europe","Denmark|Scandinavian","Denmark|丹麦");
        Map("Europe","Iceland|Scandinavian","Iceland|冰岛"); Map("Europe","Finstonia|EasternEurope","Finland|芬兰|Estonia|爱沙尼亚");
        Map("Europe","EastSlavic|EasternEurope","Russia|俄罗斯|Ukraine|乌克兰|Belarus|白俄罗斯");
        Map("Europe","WestSlavic|EasternEurope","Czech Republic|Czechia|捷克|Slovakia|斯洛伐克");
        Map("Europe","WestBalkan|SouthEurope","Serbia|塞尔维亚|Croatia|克罗地亚|Bosnia & Herzegovina|Bosnia and Herzegovina|Montenegro|Slovenia|Kosovo");
        Map("Europe","EastBalkan|EasternEurope","Bulgaria|Moldova|North Macedonia");
        Map("Europe","AlbanianGreek|SouthEurope","Greece|希腊|Cyprus"); Map("Europe","Albania","Albania|阿尔巴尼亚");
        Map("Europe","Romania|EasternEurope","Romania|罗马尼亚"); Map("Europe","Hungary|EasternEurope","Hungary|匈牙利"); Map("Europe","Poland|EasternEurope","Poland|波兰"); Map("Europe","Baltics|EasternEurope","Latvia|Lithuania");
        Map("Asia","China","China|中国|Hong Kong (China)|Hong Kong|香港|Chinese Taipei|Taiwan|Macau");
        Map("Asia","Japan","Japan|日本"); Map("Asia","Korea","South Korea|Korea Republic|Korea|韩国|North Korea|朝鲜");
        Map("Asia","SouthAsia","India|Pakistan|Bangladesh|Nepal|Sri Lanka|Bhutan|Maldives|Afghanistan");
        Map("Asia","MainlandSEA|SEA","Thailand|Myanmar|Cambodia|Laos"); Map("Asia","Vietnam|SEA","Vietnam|越南"); Map("Asia","Indonesia|SEA","Indonesia|印度尼西亚|Timor-Leste");
        Map("Asia","Filipino|SEA","Philippines|菲律宾"); Map("Asia","Malaysia|SEA","Malaysia|马来西亚|Brunei"); Map("Asia","Singapore|SEA","Singapore|新加坡");
        Map("Asia","Iran|MiddleEast","Iran|伊朗"); Map("Asia","Israel|MiddleEast","Israel|以色列"); Map("Asia","Turkish|MiddleEast","Turkey|Türkiye|土耳其");
        Map("Asia","ArabGulf|MiddleEast","Saudi Arabia|沙特阿拉伯|Qatar|U.A.E.|United Arab Emirates|Oman|Yemen|Kuwait|Bahrain");
        Map("Asia","Mashriq|MiddleEast","Iraq|Syria|Jordan|Lebanon|Palestine"); Map("Asia","Armenian","Armenia"); Map("Asia","Azerbaijan","Azerbaijan"); Map("Asia","Caucasus","Georgia");
        Map("Asia","CentralAsian","Kazakhstan|Kyrgyzstan|Turkmenistan"); Map("Asia","Uzbekistan","Uzbekistan"); Map("Asia","Tajikistan","Tajikistan"); Map("Asia","Mongolia","Mongolia");
        Map("Africa","Maghreb|African","Morocco|摩洛哥|Algeria|阿尔及利亚|Tunisia|突尼斯|Libya|Egypt|埃及|Mauritania");
        Map("Africa","WestAfrica|African","Nigeria|尼日利亚|Ghana|加纳|Senegal|塞内加尔|Ivory Coast|Côte d’Ivoire|Cote d'Ivoire|科特迪瓦|Guinea|Guinea-Bissau|Sierra Leone|Liberia|Togo|Benin|Gambia|Cape Verde");
        Map("Africa","CentralAfrica|African","Cameroon|喀麦隆|DR Congo|Democratic Republic of Congo|Congo|Gabon|Equatorial Guinea|Central African Republic|Chad|Sao Tome & Principe");
        Map("Africa","EastAfrica|African","Kenya|Uganda|Tanzania|Rwanda|Burundi|South Sudan|Sudan"); Map("Africa","HornOfAfrica|African","Ethiopia|Eritrea|Somalia|Djibouti");
        Map("Africa","SahelianAfrica|African","Mali|Burkina Faso|Niger"); Map("Africa","SouthernAfrica|African","South Africa|南非|Angola|Zambia|Zimbabwe|Mozambique|Botswana|Namibia|Malawi|Lesotho|Eswatini|Madagascar|Mauritius|Seychelles|Comoros");
        Map("SouthAmerica","BrazilMixed|Brazil","Brazil|巴西"); Map("SouthAmerica","SouthConeSA|LatinAmerica","Argentina|阿根廷|Uruguay|乌拉圭|Chile|智利|Paraguay|巴拉圭");
        Map("SouthAmerica","NorthernSA|LatinAmerica","Colombia|哥伦比亚|Venezuela|委内瑞拉|Ecuador|厄瓜多尔|Peru|秘鲁|Bolivia|玻利维亚|Guyana|Suriname");
        Map("NorthAmerica","CaucasianNA","United States|USA|U.S.A.|美国|Canada|加拿大"); Map("NorthAmerica","Mestizo|LatinAmerica","Mexico|墨西哥|Guatemala|Honduras|El Salvador|Nicaragua|Costa Rica|Panama|Belize");
        Map("NorthAmerica","Afrocaribbean|LatinAmerica","Jamaica|牙买加|Haiti|Trinidad and Tobago|Trinidad & Tobago|Cuba|Dominican Republic|Barbados|Bermuda|Grenada|Saint Lucia|St. Lucia|St. Kitts and Nevis|Antigua & Barbuda|Dominica|Bahamas|Puerto Rico|Curacao|Aruba|Cayman Islands|Turks and Caicos Islands|St. Vincent and the Grenadines");
        Map("Oceania","Anglosphere","Australia|澳大利亚|New Zealand|新西兰"); Map("Oceania","PacificIslanders","Fiji|Samoa|Tonga|Vanuatu|Solomon Islands|Papua New Guinea|New Caledonia|Tahiti|Cook Islands|American Samoa");
        Link("CentralEurope|NorthCentralEurope|NorthWestEurope|Netherlands|France");
        Link("Anglosphere|Ireland|NorthWestEurope|CaucasianNA");
        Link("Italia|Spain|Portugal|SouthEurope|SouthWestEurope|France");
        Link("Albania|AlbanianGreek|EastBalkan|WestBalkan|Romania|SouthEurope|EasternEurope");
        Link("Baltics|EastSlavic|WestSlavic|Poland|Hungary|EasternEurope|NorthCentralEurope");
        Link("Sweden|Norway|Denmark|Iceland|Finstonia|Scandinavian|NorthCentralEurope|NorthWestEurope");
        Link("China|Japan|Korea|Mongolia");
        Link("Filipino|Indonesia|Malaysia|MainlandSEA|Singapore|Vietnam|SEA");
        Link("SouthAsia|SEA|MiddleEast");
        Link("CentralAsian|Tajikistan|Uzbekistan|Iran|MiddleEast");
        Link("Armenian|Azerbaijan|Caucasus|Turkish|EasternEurope|MiddleEast");
        Link("ArabGulf|Mashriq|Iran|Israel|Turkish|MiddleEast|Maghreb");
        Link("CentralAfrica|WestAfrica|EastAfrica|HornOfAfrica|SahelianAfrica|SouthernAfrica|African");
        Link("Maghreb|MiddleEast|African");
        Link("BrazilMixed|Brazil|Mestizo|NorthernSA|SouthConeSA|IndigenousSA|LatinAmerica");
        Link("Afroamerican|Afrocaribbean|African");
        Link("PacificIslanders|SEA");
    }
    static void Add(string region,string groups)=>regions[region]=groups.Split('|');
    public static bool IsKnownGroup(string group)=>string.Equals(group,"MixedRace",StringComparison.OrdinalIgnoreCase)||regions.Values.Any(groups=>groups.Contains(group,StringComparer.OrdinalIgnoreCase))||countries.Values.Any(country=>country.Groups.Contains(group,StringComparer.OrdinalIgnoreCase));
    static void Map(string region,string groups,string names){foreach(var n in names.Split('|'))countries[n]=(region,groups.Split('|'));}
    static void Link(string names){var groups=names.Split('|');foreach(var group in groups){if(!adjacent.TryGetValue(group,out var targets))adjacent[group]=targets=new(StringComparer.OrdinalIgnoreCase);targets.UnionWith(groups.Where(x=>x!=group));}}
    static (string Region,string[] Groups)? Country(string nation)
    {
        if(countries.TryGetValue(nation.Trim(),out var match)||countries.TryGetValue(CountryNames.English(nation),out match))return match;
        var code=CountryNames.Code(nation);if(code.Length==2)foreach(var pair in countries)if(CountryNames.Code(pair.Key)==code)return pair.Value;
        return null;
    }
    static HashSet<string> Neighbors(IEnumerable<string> groups){var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var group in groups)if(adjacent.TryGetValue(group,out var nearby))result.UnionWith(nearby);return result;}
    public static string Describe(Person person,string group)
    {
        var country=Country(person.Nation);var direct=string.IsNullOrWhiteSpace(person.Group)?country?.Groups??Array.Empty<string>():new[]{person.Group};
        if(direct.Contains(group,StringComparer.OrdinalIgnoreCase))return "本地区";
        if(Neighbors(direct).Contains(group))return "邻近分类";
        return country!=null&&regions[country.Value.Region].Contains(group,StringComparer.OrdinalIgnoreCase)?"同洲分类":"邻近区域";
    }
    public static List<Face> Available(Person person,IEnumerable<Face> faces,IReadOnlyDictionary<string,int> usage)
    {
        var tiers=Tiers(person,faces);
        foreach(var tier in tiers)if(tier.Any(f=>usage.GetValueOrDefault(f.UniqueKey)==0))return tier;
        return tiers.LastOrDefault()??new();
    }
    public static List<List<Face>> Tiers(Person p,IEnumerable<Face> faces)
    {
        var gender=p.MatchGender;var pool=faces.Where(f=>f.Kind==p.Kind&&f.Gender==gender).DistinctBy(f=>f.UniqueKey).ToList();
        var country=Country(p.Nation);
        var direct=string.IsNullOrWhiteSpace(p.Group)?country?.Groups??Array.Empty<string>():new[]{p.Group};
        if(direct.Length==0)return new();
        var groups=new HashSet<string>(direct,StringComparer.OrdinalIgnoreCase);
        var exact=pool.Where(f=>groups.Contains(f.Group)).ToList();
        groups.UnionWith(Neighbors(direct));
        var nearby=pool.Where(f=>groups.Contains(f.Group)).ToList();
        // Broaden within explicit compatible families. Never traverse the whole
        // neighbor graph: broad labels such as African/SEA connect unlike sets.
        string[] families={
            "CentralEurope|Anglosphere|Albania|AlbanianGreek|Baltics|EastBalkan|EastSlavic|Finstonia|France|Hungary|Iceland|Ireland|Italia|Netherlands|Poland|Portugal|Romania|Spain|WestBalkan|WestSlavic|Sweden|Denmark|Norway|EasternEurope|NorthCentralEurope|NorthWestEurope|SouthEurope|SouthWestEurope|Scandinavian|CaucasianNA",
            "China|Japan|Korea|Mongolia",
            "Filipino|Indonesia|MainlandSEA|Malaysia|Singapore|Vietnam|SEA",
            "ArabGulf|Mashriq|Iran|Israel|Turkish|MiddleEast|Maghreb|Armenian|Azerbaijan|Caucasus",
            "CentralAsian|Tajikistan|Uzbekistan|Iran",
            "CentralAfrica|WestAfrica|EastAfrica|HornOfAfrica|SahelianAfrica|SouthernAfrica|African|Afroamerican|Afrocaribbean",
            "BrazilMixed|Brazil|Mestizo|NorthernSA|SouthConeSA|IndigenousSA|LatinAmerica"};
        foreach(var family in families){var members=family.Split('|');if(direct.Any(g=>members.Contains(g,StringComparer.OrdinalIgnoreCase)))groups.UnionWith(members);}
        // Anglosphere spans countries: only explicitly applicable countries use it.
        if(!direct.Contains("Anglosphere")&&country?.Region!="Europe")groups.Remove("Anglosphere");
        var compatible=pool.Where(f=>groups.Contains(f.Group)).ToList();
        return new[]{exact,nearby,compatible}.Where(t=>t.Count>0).ToList();
    }
    public static List<Face> Resolve(Person p,IEnumerable<Face> faces,bool expand=false)
    {
        var gender=p.MatchGender;
        var pool=faces.Where(f=>f.Kind==p.Kind&&f.Gender==gender).ToList();
        var country=Country(p.Nation);
        var direct=string.IsNullOrWhiteSpace(p.Group)?country?.Groups??Array.Empty<string>():new[]{p.Group};
        var exact=pool.Where(f=>direct.Contains(f.Group,StringComparer.OrdinalIgnoreCase)).DistinctBy(f=>f.UniqueKey).ToList();
        if(exact.Count>0&&!expand)return exact;
        var neighbors=Neighbors(direct);var nearby=pool.Where(f=>neighbors.Contains(f.Group)).DistinctBy(f=>f.UniqueKey).ToList();
        if(nearby.Count>0)return exact.Concat(nearby).DistinctBy(f=>f.UniqueKey).ToList();
        if(country==null)return exact;
        return pool.Where(f=>regions[country.Value.Region].Contains(f.Group,StringComparer.OrdinalIgnoreCase)&&(f.Group!="Anglosphere"||direct.Contains("Anglosphere"))).DistinctBy(f=>f.UniqueKey).ToList();
    }
}
