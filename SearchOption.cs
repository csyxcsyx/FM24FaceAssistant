using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FM24FaceAssistant;

public record PickerOption(string Value,string Label,string Keywords="")
{
    public static bool Contains(string value,string query)=>CultureInfo.InvariantCulture.CompareInfo.IndexOf(value,query.Trim(),CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0;
    public bool Matches(string query)=>Contains(Value,query)||Contains(Label,query)||Contains(Keywords,query);
    public static List<PickerOption> Filter(IEnumerable<PickerOption> source,string query)=>source.Where(x=>x.Matches(query)).ToList();
    static readonly Dictionary<string,string> groups=new(StringComparer.OrdinalIgnoreCase)
    {
        ["CentralEurope"]="中欧",["Anglosphere"]="英语圈",["China"]="中国",["Japan"]="日本",["Korea"]="韩国／朝鲜",["France"]="法国",["Italia"]="意大利",["Spain"]="西班牙",["Portugal"]="葡萄牙",["Netherlands"]="荷兰",["Ireland"]="爱尔兰",["Sweden"]="瑞典",["Norway"]="挪威",["Denmark"]="丹麦",["Iceland"]="冰岛",["Finstonia"]="芬兰／爱沙尼亚",["Baltics"]="波罗的海",["WestSlavic"]="西斯拉夫",["EastSlavic"]="东斯拉夫",["WestBalkan"]="西巴尔干",["EastBalkan"]="东巴尔干",["Albania"]="阿尔巴尼亚",["AlbanianGreek"]="阿尔巴尼亚／希腊",["Poland"]="波兰",["Hungary"]="匈牙利",["Romania"]="罗马尼亚",["Scandinavian"]="斯堪的纳维亚",["EasternEurope"]="东欧",["NorthCentralEurope"]="中北欧",["NorthWestEurope"]="西北欧",["SouthEurope"]="南欧",["SouthWestEurope"]="西南欧",["ArabGulf"]="海湾地区",["Mashriq"]="黎凡特",["MiddleEast"]="中东",["Turkish"]="土耳其",["Iran"]="伊朗",["Israel"]="以色列",["Armenian"]="亚美尼亚",["Azerbaijan"]="阿塞拜疆",["Caucasus"]="高加索",["CentralAsian"]="中亚",["Tajikistan"]="塔吉克斯坦",["Uzbekistan"]="乌兹别克斯坦",["Mongolia"]="蒙古",["SouthAsia"]="南亚",["SEA"]="东南亚",["MainlandSEA"]="中南半岛",["Vietnam"]="越南",["Indonesia"]="印度尼西亚",["Malaysia"]="马来西亚",["Singapore"]="新加坡",["Filipino"]="菲律宾",["African"]="非洲",["Maghreb"]="马格里布／北非",["CentralAfrica"]="中非",["EastAfrica"]="东非",["HornOfAfrica"]="非洲之角",["SahelianAfrica"]="萨赫勒",["SouthernAfrica"]="南部非洲",["WestAfrica"]="西非",["CaucasianNA"]="北美",["Afroamerican"]="非裔美洲",["Afrocaribbean"]="非裔加勒比",["Mestizo"]="拉美混合",["LatinAmerica"]="拉丁美洲",["Brazil"]="巴西",["BrazilMixed"]="巴西混合",["IndigenousSA"]="南美原住民",["NorthernSA"]="南美北部",["SouthConeSA"]="南美南锥",["PacificIslanders"]="太平洋岛屿"
    };
    public static PickerOption Nation(string value)=>new(value,CountryNames.English(value),CountryNames.Label(value)+" "+CountryNames.Code(value));
    public static PickerOption Group(string value)=>new(value,value,groups.GetValueOrDefault(value,""));
    public static bool GroupMatches(string value,string query)=>Group(value).Matches(query);
}
