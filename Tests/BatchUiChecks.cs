using FM24FaceAssistant;

static class BatchUiChecks
{
    public static void Run(Action<bool,string> check,Person p,Face f)
    {
        var india=p with{Kind="职员",Nation="India",Gender="未知",Name="Unlisted Staff",Group=null};var sea=f with{Kind="职员",Gender="男",Group="SEA",Hash="sea"};var east=sea with{Group="China",Hash="china"};var middle=sea with{Group="MiddleEast",Hash="middle"};
        check(RegionResolver.Resolve(india,new[]{sea,east}).Single().Group=="SEA","缺少南亚职员素材优先邻近分类");
        check(RegionResolver.Resolve(india,new[]{middle,east}).Single().Group=="MiddleEast","南亚支持邻近中东素材回退");
        var pacific=india with{Nation="Fiji",Name="Sarah Smith",Gender="女"};var femaleSea=sea with{Gender="女"};check(RegionResolver.Resolve(pacific,new[]{femaleSea,sea}).Single().Gender=="女","太平洋地区回退仍保持职员性别");
        var japan=p with{Nation="Japan",Group=null};var japanese=f with{Group="Japan",Hash="japan"};check(RegionResolver.Resolve(japan,new[]{japanese,f}).Single().Group=="Japan","有本地区素材时优先原分类");
        check(RegionResolver.Resolve(japan,new[]{f}).Single().Group=="China"&&RegionResolver.Describe(japan,"China")=="邻近分类","东亚邻近分类及结果来源");
        check(RegionResolver.Resolve(japan,new[]{f with{Gender="女"}}).Count==0,"回退不会给男性球员分配女性素材");
        check(RegionResolver.Resolve(p with{Nation="Unknown",Group=null},new[]{f}).Count==0,"无法确定国籍时不伪造邻近地区");
        var issues=new List<MatchIssue>();new Matcher().Assign(new[]{india},new(){sea},new(),Array.Empty<Mapping>(),out var missing,default,false,null,issues);check(missing.Count==0&&issues.Count==0,"默认男性与地域回退用于批量入口");
        var absent=p with{Id=88001,Nation="Unknown",Group=null};new Matcher().Assign(new[]{absent},new(){f},new(),Array.Empty<Mapping>(),out missing,default,false,null,issues);check(issues.Count==1&&issues[0].Person.Id==absent.Id&&missing.Count==1,"未完成原因关联原人物并可分页查看");
        var old=new Assignment(p,f,true,"locked");var pending=Enumerable.Range(1,23).Select(i=>new Assignment(p with{Id=10000+i},f,false,"邻近分类")).ToList();var rows=pending.Prepend(old).ToList();var extPerson=p with{Id=50000};var external=new Mapping("test","img",$"graphics/pictures/person/{extPerson.Key}/portrait",f.Path);
        var plan=BatchPlan.Create(rows.Select(a=>a.Person).Append(extPerson).Append(absent),rows,new(){old},new[]{external},issues);
        check(plan.Pending==23&&plan.Preserved==2&&plan.Missing==1&&plan.OutputCount==24,"批量方案区分新增保留外部头像与未完成");
        check(plan.Page("全部结果",0).Count==9&&plan.Page("全部结果",1).Count==9&&plan.Page("全部结果",2).Count==8,"固定九张卡片分页含尾页");
        check(plan.Page("未完成",0).Single().Person.Id==absent.Id&&plan.Page("未完成",1).Count==0,"分页筛选不会混入其他状态");
        var applied=plan.Applied();check(applied.Pending==0&&applied.Cards.Count(c=>c.Status=="已应用")==23&&applied.Preserved==2&&applied.Missing==1,"应用后状态更新保留未完成项");
        check(PickerOption.Nation("Germany").Label=="Germany"&&PickerOption.Nation("德国").Label=="Germany"&&PickerOption.Group("CentralEurope").Label=="CentralEurope","下拉选项仅显示英文名称");
        check(CountryNames.English("英格兰")=="England"&&CountryNames.English("中国")=="China","国籍中文数据仅在显示层转换");
        var picker=new SearchPicker{Placeholder="All nations",PopupWidth=280};picker.SetOptions(new[]{new PickerOption("","All nations"),new PickerOption("Long","A very long English region name")});picker.Text="Long";check(picker.Height==32&&picker.PopupWidth==280&&picker.Placeholder=="All nations","选择框紧凑高度固定弹出宽度及默认提示");
    }
}
