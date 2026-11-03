using FM24FaceAssistant;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using System.Windows;
static class SimplificationChecks
{
    public static void Run(Action<bool,string> check,Person p,Face f,string root)
    {
        var identities=new[]{new RoleIdentity(123,"Lily Sumner","England",true,"已保存角色"),new RoleIdentity(456,"José Smith","Spain",null,"游戏名单")};
        check(RoleIdentityResolver.ByName("lily SUMNER",identities).Identity?.Id==123,"特殊角色按姓名自动匹配已保存UID");
        check(RoleIdentityResolver.ByName("Jose Smith",identities).Identity?.Id==456,"角色匹配兼容姓名音调符号");
        check(RoleIdentityResolver.ByName("Lily Sumner",identities.Append(identities[0] with{Id=789})).Identity==null,"同名多UID不自动猜测");
        foreach(var text in new[]{"123","r-123","R-123","UID: r-123","ID：123","UID=r-123\nID:123"})check(RoleIdentityResolver.FromText(text,identities).Identity?.Id==123,"识别复制的UID "+text.Replace('\n',' '));
        check(RoleIdentityResolver.FromText("Name: Yan Ping\nUID: r-2002000001",identities).Identity?.Name=="Yan Ping","复制姓名与UID时同时填入");
        check(RoleIdentityResolver.FromText("UID:123 ID:456",identities).Identity==null,"复制多个角色编号时拒绝误匹配");
        foreach(var text in new[]{"2026/3/18","£2000","0","-123","4294967296","r-99999999999999999999"})check(RoleIdentityResolver.FromText(text,identities).Identity==null,"拒绝非UID或越界文本 "+text);
        var settings=JsonSerializer.Deserialize<Settings>("{\"Verification\":\"旧验证记录\"}",Files.Json)!;
        check(settings.ShowSkinEntry&&!JsonSerializer.Serialize(settings,Files.Json).Contains("Verification"),"旧设置兼容，默认启用入口且不再保存验证记录");
        var state=Path.Combine(root,"original-state");var graphics=Path.Combine(root,"original-graphics");var ext=Path.Combine(graphics,"external");Directory.CreateDirectory(ext);File.Copy(f.Path,Path.Combine(ext,"original.png"));
        var cfg=Path.Combine(ext,"config.xml");var first=p with{Id=123,Newgen=true};var special=p with{Id=999,Name="Known Manager",Kind="职员",Newgen=true};
        var original="<record><list id=\"maps\"><record from=\"original\" to=\"graphics/pictures/person/r-123/portrait\"/><record from=\"original\" to=\"graphics/pictures/person/r-999/portrait\"/></list></record>";File.WriteAllText(cfg,original);
        var writer=new PatchWriter(state);writer.Apply(graphics,"A",new(){new(first,f,true,"manual")},Catalog.Scan(graphics,default));writer.Apply(graphics,"A",new(){new(special,f with{Kind="职员"},true,"manual")},Catalog.Scan(graphics,default),special:true);
        var config=Path.Combine(graphics,"FM24FaceAssistant","config.xml");var specialConfig=Path.Combine(graphics,"FM24FaceAssistant","special","config.xml");
        Files.Save(Path.Combine(state,"rows-"+Files.Hash("B")+".json"),new List<Assignment>{new(p,f,true,"other profile")});
        OriginalPortraits.Restore(state,graphics,"A",writer);
        check(File.ReadAllText(cfg)==original&&!XDocument.Load(config).Descendants("record").Any(x=>x.Attribute("to")!=null)&&!XDocument.Load(specialConfig).Descendants("record").Any(x=>x.Attribute("to")!=null),"恢复原头像包同时处理两套输出和串联外部快照");
        check(Files.Read<List<Assignment>>(Path.Combine(state,"rows-"+Files.Hash("A")+".json"))!.Count==0&&Files.Read<List<Assignment>>(Path.Combine(state,"special-rows-"+Files.Hash("A")+".json"))!.Count==0&&Files.Read<List<Assignment>>(Path.Combine(state,"rows-"+Files.Hash("B")+".json"))!.Count==1,"初始恢复清除当前分配，保留其他存档记录");
        int count=Directory.GetDirectories(Path.Combine(state,"backups")).Length;OriginalPortraits.Restore(state,graphics,"A",writer);check(Directory.GetDirectories(Path.Combine(state,"backups")).Length==count,"重复恢复不创建冗余备份");
        writer.Apply(graphics,"A",new(){new(first,f,true,"manual")},Catalog.Scan(graphics,default));var before=File.ReadAllText(config);File.AppendAllText(cfg,"<!--outside edit-->");bool blocked=false;try{OriginalPortraits.Restore(state,graphics,"A",writer);}catch(IOException){blocked=true;}
        check(blocked&&File.ReadAllText(config)==before,"原包恢复发现外部变更时整体停止，不部分提交");
        using(var cts=new CancellationTokenSource()){cts.Cancel();bool cancelled=false;try{OriginalPortraits.Restore(state,graphics,"A",writer,cts.Token);}catch(OperationCanceledException){cancelled=true;}check(cancelled&&File.ReadAllText(config)==before,"原包恢复可取消且不改动文件");}
        var skin=Path.Combine(root,"uid-skin");Directory.CreateDirectory(Path.Combine(skin,"panels","human"));
        var inbox=Path.Combine(skin,"panels","human","inbox item.xml");
        File.WriteAllText(inbox,"<panel><container navigation_container=\"true\"><container><widget class=\"object_portrait_picture\" id=\"snpc\"/><widget class=\"object_portrait_picture\" id=\"sncl\"/></container><container><container><widget class=\"person_button\" id=\"snob\"/><widget class=\"label\" id=\"sjob\"/></container><widget class=\"text\" id=\"nwhd\"/></container></container></panel>");
        UidSkin.Install(skin,writer);var patched=File.ReadAllText(inbox);var xml=XDocument.Parse(patched);var uidEdit=xml.Descendants("widget").Single(e=>(string?)e.Attribute("class")=="edit_box");check((string?)uidEdit.Attribute("show_selection_when_not_focussed")=="false"&&(string?)uidEdit.Attribute("highlighted_text_colour")=="inbox_text","UID失焦隐藏选区并固定选中文字颜色");
        check(xml.Descendants("record").Any(e=>(string?)e.Attribute("get_property")=="id  ")&&xml.Descendants("container").Any(e=>(string?)e.Attribute("class")=="client_object_viewer_xml_panel"),"UID补丁绑定发件人对象的原生id属性");
        check(xml.Descendants("widget").Any(e=>(string?)e.Attribute("id")=="sncl")&&xml.Descendants("widget").Any(e=>(string?)e.Attribute("id")=="nwhd"),"UID补丁保留原有俱乐部图和新闻标题");
        UidSkin.Install(skin,writer);check(File.ReadAllText(inbox)==patched,"UID补丁重复安装不改变文件");
        File.WriteAllText(inbox,patched.Replace("FU06","FU05"));UidSkin.Install(skin,writer);check(XDocument.Load(inbox).Descendants("container").Count(e=>(string?)e.Attribute("id")=="FU06")==1&&!File.ReadAllText(inbox).Contains("FU05"),"UID旧补丁升级为姓名后单行布局，不残留重复控件");
        var role=p with{Id=100,Name="Manager",Club="主教练"};var roleRows=SpecialRoleStore.Upsert(new(),role,null);
        bool unique=false;try{SpecialRoleStore.Upsert(roleRows,role with{Id=101},null);}catch(IOException){unique=true;}check(unique,"主教练我仅能创建一位");
        roleRows=SpecialRoleStore.Upsert(roleRows,role with{Id=102,Club="个人助理"},null);roleRows=SpecialRoleStore.Upsert(roleRows,role with{Id=103,Club="个人助理"},null);check(roleRows.Count==3,"其他职位允许同职多人");var stableIds=roleRows.Select(x=>x.Id).ToArray();roleRows=SpecialRoleStore.Upsert(roleRows,roleRows[0] with{Name="Updated manager"},roleRows[0].Id);check(roleRows.Select(x=>x.Id).SequenceEqual(stableIds),"修改角色资料保持列表原位置");
        roleRows=SpecialRoleStore.Upsert(roleRows,role with{Id=103,Name="Assistant",Club="My custom role"},103);check(roleRows.Single(x=>x.Id==103).Club=="My custom role","职位可自定义且资料可独立修改");
        bool duplicate=false;try{SpecialRoleStore.Upsert(roleRows,role with{Id=102},null);}catch(IOException){duplicate=true;}check(duplicate,"不允许重复UID创建角色");
        var imagePath=Path.Combine(root,"cached-portrait.png");
        void WritePortrait(byte shade){var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{shade,0,0,255},4);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(imagePath))png.Save(stream);File.SetLastWriteTimeUtc(imagePath,DateTime.UtcNow.AddSeconds(shade));}
        WritePortrait(1);var cached=PortraitImageCache.Load(imagePath);check(cached!=null&&cached.IsFrozen&&ReferenceEquals(cached,PortraitImageCache.Load(imagePath)),"图片复用冻结缓存，重复预览不重新解码");
        WritePortrait(2);check(!ReferenceEquals(cached,PortraitImageCache.Load(imagePath)),"本地图片更新后缓存自动失效");
        check(PortraitImageCache.Load(Path.Combine(root,"no-such-image.png"))==null,"缺失图片安全回退占位");
        var app=System.Windows.Application.Current as App??new App();app.InitializeComponent();var normalButton=new System.Windows.Controls.Button{Style=(Style)app.FindResource(typeof(System.Windows.Controls.Button))};normalButton.ApplyTemplate();check(((System.Windows.Controls.Border)normalButton.Template.FindName("B",normalButton)).BorderThickness==new Thickness(0)&&normalButton.FocusVisualStyle==null,"普通按钮不生成焦点边框或下划线");var settingsButton=new System.Windows.Controls.Button{Style=(Style)app.FindResource("SettingsAction")};settingsButton.ApplyTemplate();check(((System.Windows.Controls.Border)settingsButton.Template.FindName("Surface",settingsButton)).BorderThickness==new Thickness(0),"设置卡片按钮使用一致的无边框样式");var guide=new UidGuideWindow();check(guide.Owner==null&&guide.Title=="获取角色 UID","内置UID子窗口可加载，无外部浏览器依赖");guide.Close();var dialog=new RoleEditorWindow(null,new[]{"England"},new(),_=>System.Threading.Tasks.Task.FromResult(true));check(dialog.Title=="新建角色","独立角色编辑窗口可加载");dialog.Close();var rolePanel=new SpecialPortraitPanel();var batchPanel=new BatchPanel();check(rolePanel!=null&&batchPanel!=null,"标签化角色和批量页面可加载");
    }
}
