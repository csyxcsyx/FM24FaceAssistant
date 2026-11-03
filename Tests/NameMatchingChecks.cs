using FM24FaceAssistant;
using System.Text.Json;

static class NameMatchingChecks
{
    public static void Run(Action<bool,string> check,Person template,Face image)
    {
        Person Staff(string name,string nation="England",string gender="未知")=>template with{Kind="职员",Name=name,Nation=nation,Gender=gender,Group=null};
        var cases=new (string Name,string Nation,string Gender)[]{("Sarah Smith","England","女"),("John Smith","England","男"),("María García","Spain","女"),("José María García","Spain","男"),("Maria José Silva","Portugal","女"),("Andrea Rossi","Italy","男"),("Andrea Müller","Germany","女"),("Jean Dupont","France","男"),("Dr. Sarah Smith","England","女"),("Smith, Sarah","England","女"),("Jean-Marie Dupont","France","男"),("Sławomir Kowalski","Poland","男"),("Sophie Martin","France","女"),("Anna Ivanova","Russia","女")};
        foreach(var item in cases)check(NameGender.Resolve(Staff(item.Name,item.Nation)).Gender==item.Gender,"姓名推断 "+item.Name+" / "+item.Nation);
        check(new[]{Staff("Gert Andreas Alm","Sweden"),Staff("Lilian Nalis","France"),Staff("Jocelyn Gourvennec","France")}.All(p=>p.MatchGender=="男"),"修正常见国家差异与词典冲突");
        var fallback=Staff("Qzxvpt Rrrr","Unknown");check(NameGender.Resolve(fallback) is{Gender:"男",Source:"默认男性"},"未知姓名默认男性");
        check(NameGender.Resolve(Staff("J. Smith")) is{Gender:"男",Source:"默认男性"},"仅有首字母不拿姓氏猜性别");
        check(NameGender.Resolve(Staff("Sarah Smith",gender:"男")) is{Gender:"男",Source:"已有资料"},"明确的职员资料优先于姓名");
        check(NameGender.Resolve(template with{Gender="女",Name="Sarah Smith"}).Gender=="男","所有球员统一男性");
        check(NameGender.Resolve(Staff("Andrea Rossi","意大利")).Gender=="男"&&NameGender.Resolve(Staff("Andrea Müller","德国")).Gender=="女","国家中文别名参与姓名推断");
        var staff=Staff("Sarah Smith","China");var female=image with{Kind="职员",Gender="女",Group="China",Hash="female"};var male=female with{Gender="男",Hash="male"};var faces=new List<Face>{female,male};
        check(RegionResolver.Resolve(staff,faces).Single().Gender=="女","推荐图库使用推断性别");
        check(RegionResolver.Available(staff,faces,new Dictionary<string,int>()).Single().Gender=="女","随机重选共享推断规则");
        var matcher=new Matcher();var matched=matcher.Assign(new[]{staff},faces,new(),Array.Empty<Mapping>(),out var missing);
        check(matched.Count==1&&matched[0].Face.Gender=="女"&&missing.Count==0,"批量补全使用推断性别");
        check(matched[0].Reason.Contains("姓名推断")&&staff.Gender=="未知"&&!JsonSerializer.Serialize(staff,Files.Json).Contains("MatchGender"),"推断保留来源且不伪装成游戏数据");
        var next=staff with{Id=staff.Id+1};var locked=matched[0] with{Locked=true};var delta=matcher.Assign(new[]{next},faces,new(){locked},Array.Empty<Mapping>(),out _,default,true);
        check(delta.Count==2&&delta.All(a=>a.Face.Gender=="女")&&delta[0].Locked,"增量补图共享规则并保留锁定");
        check(matcher.Assign(new[]{fallback with{Nation="China"}},faces,new(),Array.Empty<Mapping>(),out _).Single().Face.Gender=="男","默认男性可自动补全");
        check(PickerOption.Filter(new[]{PickerOption.Nation("Germany"),PickerOption.Nation("France")},"德国").Single().Value=="Germany","国籍下拉支持中文搜索");
        check(PickerOption.Filter(new[]{PickerOption.Nation("Germany"),PickerOption.Nation("France")},"germ").Single().Value=="Germany","国籍下拉支持英文片段搜索");
        check(CountryNames.Matches("China PR","中国")&&CountryNames.Code("England")=="GB"&&CountryNames.Code("Argentina")=="AR","FM国籍别名和国家编码");
        check(CountryNames.Matches("England","英格兰")&&!CountryNames.Matches("Scotland","英格兰")&&PickerOption.Nation("England").Matches("英格兰"),"英国各足协国籍保持独立筛选");
        check(PickerOption.Filter(new[]{PickerOption.Group("CentralEurope"),PickerOption.Group("China")},"中欧").Single().Value=="CentralEurope","分类下拉支持中文搜索");
        check(PickerOption.Filter(new[]{PickerOption.Group("CentralEurope"),PickerOption.Group("China")},"europe").Single().Value=="CentralEurope","分类下拉支持英文片段搜索");
        check(PickerOption.Filter(new[]{new PickerOption("","全部分类"),PickerOption.Group("China")},"").Count==2&&PickerOption.Filter(new[]{PickerOption.Group("China")},"不存在").Count==0,"下拉清空和无匹配状态");
        var picker=new SearchPicker();picker.SetOptions(new[]{PickerOption.Group("China")});picker.Text="China";check(picker.Text=="China","搜索选择控件可加载和设置文本");
    }
}
