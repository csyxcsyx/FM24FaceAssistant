using FM24FaceAssistant;
using System.IO;

static class PortraitPreviewChecks
{
    public static void Run(Action<bool,string> check,Person person,Face face,string root)
    {
        var p=person with{Newgen=null};
        var normal=p.Id.ToString();var regen="r-"+p.Id;
        check(PersonSearch.PreviewKeys(p,"").SequenceEqual(new[]{normal,regen})&&PersonSearch.PreviewKeys(p,p.Name)[0]==normal&&PersonSearch.PreviewKeys(p,"R-"+p.Id).Single()==regen&&PersonSearch.PreviewKeys(p with{Newgen=true},"").Single()==regen&&PersonSearch.PreviewKeys(p with{Newgen=false},"").Single()==normal,"预览按人物类型和明确搜索区分普通ID与r-ID");
        var assignments=new[]{new Assignment(p,face,false,"兼容补图",new[]{regen})};
        check(PersonSearch.Portrait(p,PersonSearch.PreviewKeys(p,"").Take(1),assignments,Array.Empty<Mapping>())==null,"未核对外部映射前不冒用兼容补图");
        check(PersonSearch.Portrait(p,PersonSearch.PreviewKeys(p,""),assignments,Array.Empty<Mapping>())==face.Path,"确认普通映射缺失后保留未知类型人物的r头像兼容回退");
        var oldState=Files.State;
        Files.State=Path.Combine(root,"preview-state");Directory.CreateDirectory(Files.State);
        try{
            var graphics=Path.Combine(root,"preview-graphics");Directory.CreateDirectory(graphics);
            File.Copy(face.Path,Path.Combine(graphics,"original.png"));
            File.Copy(face.Path,Path.Combine(graphics,"updated.png"));
            var config=Path.Combine(graphics,"config.xml");
            string Xml(string from)=>$"<record><list id=\"maps\"><record from=\"{from}\" to=\"graphics/pictures/person/{normal}/portrait\"/></list></record>";
            var target="graphics/pictures/person/"+normal+"/portrait";
            File.WriteAllText(config,Xml("original"));
            using var index=new MappingIndex(graphics);
            var cold=index.Find(new[]{target},default,_=>{},false);
            check(cold.Single().Image==Path.Combine(graphics,"original.png"),"首次预览无需全量索引即可读取原头像");
            index.Read(default,_=>{});
            File.WriteAllText(config,Xml("updated"));
            var stale=index.Find(new[]{target},default,_=>{},false);
            check(stale.Single().Image==Path.Combine(graphics,"updated.png")&&PersonSearch.Portrait(p,PersonSearch.PreviewKeys(p,""),assignments,stale)==stale[0].Image,"索引过期时读取最新普通头像且不回退到r分配");
            check(File.ReadAllText(config)==Xml("updated")&&index.Find(new[]{target},default,_=>{},false).Single().Image==stale[0].Image,"重复预览复用缓存且不改写游戏映射");
            var absent="graphics/pictures/person/999999/portrait";
            check(index.Find(new[]{absent},default,_=>{},false).Count==0,"不存在的头像保持缺失而不借用其他ID");
            File.WriteAllText(config,Xml("original"));
            check(index.Find(new[]{target},default,_=>{},false).Single().Image==cold[0].Image,"外部配置再次变化后预览缓存失效");
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            bool stopped=false;try{index.Find(new[]{target},cancel.Token,_=>{},false);}catch(OperationCanceledException){stopped=true;}
            check(stopped,"快速切换人物可以取消旧头像查询");
        }finally{Files.State=oldState;}
    }
}
