using FM24FaceAssistant;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;

static class RepairIntegrationChecks
{
    sealed class Transport : IRefreshTransport
    {
        public IntPtr Layout=>GetKeyboardLayout(0);public bool HasFocus{get;set;}=true;public bool Prepared,Restored,FailPrepare,FailDown;public List<RefreshStroke[]> Sent=new();
        public Task PrepareAsync(){Prepared=true;if(FailPrepare)throw new IOException("prepare");return Task.CompletedTask;}
        public void Send(IReadOnlyList<RefreshStroke> strokes){Sent.Add(strokes.ToArray());if(FailDown&&(strokes[0].Flags&2)==0)throw new IOException("send");}
        public Task RestoreAsync(){Restored=true;return Task.CompletedTask;}
    }
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    public static void Run(Action<bool,string> check,Person person,Face face,string root)
    {
        var p=person with{Newgen=null,Group=null,Nation="China"};var normal=new Mapping("external.xml","original",$"graphics/pictures/person/{p.Id}/portrait",face.Path);
        var matcher=new Matcher();var rows=matcher.Assign(new[]{p},new(){face},new(),new[]{normal},out var missing);
        check(rows.Count==1&&rows[0].OutputKeys.SequenceEqual(new[]{"r-"+p.Id})&&missing.Count==0,"普通映射不会掩盖缺失的r映射");
        var regen=normal with{Target=$"graphics/pictures/person/r-{p.Id}/portrait"};check(matcher.Assign(new[]{p},new(){face},new(),new[]{normal,regen},out _).Count==0,"两侧已有有效头像时均保留");
        var reverse=matcher.Assign(new[]{p},new(){face},new(),new[]{regen},out _);check(reverse.Single().OutputKeys.SequenceEqual(new[]{p.Id.ToString()}),"已有r映射时仅补普通映射");
        check(PersonSearch.Portrait(p,p.Keys,rows,new[]{normal})==face.Path,"普通人物预览优先普通外部映射");var alternative=normal with{Image="original-normal.png"};check(PersonSearch.Portrait(p,p.Keys,rows,new[]{alternative})=="original-normal.png"&&PersonSearch.Portrait(p,new[]{"r-"+p.Id},rows,new[]{alternative})==face.Path,"普通与r预览按目标分离");
        var repeated=matcher.Assign(new[]{p},new(){face},rows,Array.Empty<Mapping>(),out var repeatedIssues);var repeatedPlan=BatchPlan.Create(new[]{p},repeated,rows,Array.Empty<Mapping>(),new());check(repeatedPlan.Pending==0&&repeated.Count==rows.Count&&repeated[0].Face.UniqueKey==rows[0].Face.UniqueKey,"重复扫描保留已应用记录且新增数为零");
        using(var profiles=new Storage(Path.Combine(root,"profiles"))){profiles.SavePeople("A",new(){p});profiles.Save("A",rows);profiles.SavePeople("B",new());check(profiles.Profiles().SequenceEqual(new[]{"A","B"}),"存档记录列表含空白新建记录");profiles.DeleteProfile("B");check(profiles.Profiles().SequenceEqual(new[]{"A"})&&profiles.Load("A").Count==rows.Count,"删除另一记录不影响当前分配");}
        var lifetime=new GameLifetimeGate();check(!lifetime.Observe(false)&&!lifetime.Observe(false)&&!lifetime.Observe(true)&&!lifetime.Observe(false)&&!lifetime.Observe(true)&&!lifetime.Observe(false)&&lifetime.Observe(false),"未开游戏可使用工具，运行后关闭游戏才退出，短暂缺失不误关");
        var compatibilityPlan=BatchPlan.Create(new[]{p},rows,new(),new[]{normal},new());check(compatibilityPlan.MissingPlayers==0&&compatibilityPlan.Compatibility==1&&compatibilityPlan.Pending==1,"已有普通头像仅计入兼容补图，不误报缺图人数");
        var keys=PersonSearch.RequestedKeys(p,"R-"+p.Id);check(keys!.Single()=="r-"+p.Id&&PersonSearch.RequestedKeys(p,"r-999999")==null,"明确r搜索仅更新指定r侧");
        var graphics=Path.Combine(root,"repair-graphics");var external=Path.Combine(graphics,"external");Directory.CreateDirectory(external);File.Copy(face.Path,Path.Combine(external,"original.png"));var config=Path.Combine(external,"config.xml");var original=$"<record><list id=\"maps\"><record from=\"original\" to=\"{normal.Target}\" /></list></record>";File.WriteAllText(config,original);
        var writer=new PatchWriter(Path.Combine(root,"repair-state"));writer.Apply(graphics,"A",rows,Catalog.Scan(graphics,default));var maps=XDocument.Load(Path.Combine(graphics,"FM24FaceAssistant","config.xml")).Descendants("record").Where(e=>e.Attribute("to")!=null).ToArray();check(maps.Length==2&&maps.All(e=>e.Attribute("to")!.Value.Contains("/r-"+p.Id+"/"))&&File.ReadAllText(config)==original,"r补全写大小头像且不触碰普通外部配置");writer.Undo("头像补丁");check(!File.Exists(Path.Combine(graphics,"FM24FaceAssistant","config.xml"))&&File.ReadAllText(config)==original,"定向修复可以完整撤销");
        bool blocked=false;try{writer.Apply(graphics,"A",new(){rows[0] with{TargetKeys=new[]{"r-999999"}}},new());}catch(InvalidOperationException){blocked=true;}check(blocked,"拒绝与人物不符的输出ID");
        var transport=new Transport();GameRefresh.ExecuteAsync("Shift+R",transport,0).GetAwaiter().GetResult();check(transport.Prepared&&transport.Restored&&transport.Sent.Count==2,"刷新准备发送释放及恢复按顺序完成");
        var down=transport.Sent[0];var up=transport.Sent[1];check(down.Length==2&&down.All(s=>(s.Flags&8)!=0&&(s.Flags&2)==0)&&up.All(s=>(s.Flags&10)==10)&&up.Select(s=>s.Scan).SequenceEqual(down.Select(s=>s.Scan).Reverse()),"刷新使用扫描码且反序释放全部键");
        transport=new(){FailPrepare=true};try{GameRefresh.ExecuteAsync("Shift+R",transport,0).GetAwaiter().GetResult();}catch(IOException){}check(transport.Restored&&transport.Sent.Count==0,"输入状态准备失败仍恢复且不发送字符");
        transport=new(){FailDown=true};try{GameRefresh.ExecuteAsync("Shift+R",transport,0).GetAwaiter().GetResult();}catch(IOException){}check(transport.Restored&&transport.Sent.Count==2,"部分发送失败仍释放按键并恢复");
        transport=new(){HasFocus=false};try{GameRefresh.ExecuteAsync("Shift+R",transport,0).GetAwaiter().GetResult();}catch(IOException){}check(transport.Restored&&transport.Sent.Count==0,"焦点丢失不发送刷新键");
        var exe=Path.Combine(root,"程序 空格","FM24FaceAssistant.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe)!);File.WriteAllBytes(exe,new byte[]{0});var launch=new Uri(SkinEntry.LaunchUri(exe));check(launch.IsFile&&launch.LocalPath==exe&&launch.AbsoluteUri.Contains("%20"),"本地入口链接保留中文和空格路径");bool missingExe=false;try{SkinEntry.LaunchUri(exe+".missing");}catch(IOException){missingExe=true;}check(missingExe,"程序缺失时拒绝写入无效入口");
        var skin=Path.Combine(root,"repair-skin");Directory.CreateDirectory(Path.Combine(skin,"panels","client_object"));Directory.CreateDirectory(Path.Combine(skin,"panels","background_selector"));var bar=Path.Combine(skin,"panels","client_object","client object browser.xml");File.WriteAllText(bar,"<panel><container><widget class=\"menubar_icon_button\" id=\"bgs2\" /></container></panel>");var popup=Path.Combine(skin,"panels","background_selector","workings.xml");File.WriteAllText(popup,"<panel><widget id=\"F24A\"><record url=\"fm24face://open\" /></widget><widget id=\"other\" /></panel>");var skinWriter=new PatchWriter(Path.Combine(root,"repair-skin-state"));SkinEntry.Install(skin,skinWriter,exe);check(!File.ReadAllText(popup).Contains("F24A")&&File.ReadAllText(bar).Contains("F24A"),"旧弹窗入口迁移至顶部工具栏");SkinEntry.Install(skin,skinWriter,exe);var installed=XDocument.Load(bar).Descendants("widget").Where(e=>(string?)e.Attribute("id")=="F24A").ToArray();check(installed.Length==1&&installed[0].Elements("record").Any(e=>(string?)e.Attribute("id")=="primary_icon_properties"&&(string?)e.Attribute("red_replacement")=="white"),"重复安装只保留一个白色图标入口");check(new Uri((string)installed[0].Elements("record").Single(e=>(string?)e.Attribute("id")=="click_event").Attribute("url")!).LocalPath==exe&&!installed[0].ToString().Contains("fm24face://"),"游戏入口直接打开程序且不依赖自定义协议");using(var iconStream=File.OpenRead(Path.Combine(skin,"graphics","icons","24px","fm24_face_assistant_mask.png"))){var decoder=new System.Windows.Media.Imaging.PngBitmapDecoder(iconStream,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);var bitmap=new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[0],System.Windows.Media.PixelFormats.Bgra32,null,0);var pixels=new byte[24*24*4];bitmap.CopyPixels(pixels,24*4,0);check(pixels[3]==0&&pixels[(7*24+12)*4+2]==255&&pixels[(7*24+12)*4+3]==255,"皮肤图标透明底与可重着色剪影");}SkinEntry.Remove(skin,skinWriter);check(!File.ReadAllText(bar).Contains("F24A")&&File.ReadAllText(popup).Contains("other"),"移除入口保留皮肤其他内容");
        var chineseExe=Path.Combine(Path.GetDirectoryName(exe)!,"FM24头像助手.exe");File.Copy(exe,chineseExe);
        check(new Uri(SkinEntry.LaunchUri(chineseExe)).LocalPath==exe,"中文启动文件优先使用内容一致的英文兼容入口");
        File.WriteAllBytes(exe,new byte[]{1});check(new Uri(SkinEntry.LaunchUri(chineseExe)).LocalPath==chineseExe,"不把入口指向残留的不同版本兼容文件");File.Copy(chineseExe,exe,true);
        var otherSkin=Path.Combine(root,"entry-docs","skins","旧皮肤");Directory.CreateDirectory(Path.Combine(otherSkin,"panels"));var oldBar=Path.Combine(otherSkin,"panels","toolbar.xml");
        File.WriteAllText(oldBar,"<panel><widget id=\"F24A\" icon=\"icons/24px/fm24_face_assistant_mask\"><record id=\"click_event\" url=\"file:///Z:/old/FM24FaceAssistant.exe\" /></widget><widget id=\"bgs2\" /></panel>");
        SkinEntry.RepairExisting(Path.Combine(root,"entry-docs"),skin,skinWriter,true,chineseExe);
        check(XDocument.Load(oldBar).Descendants("record").Any(e=>(string?)e.Attribute("url")==SkinEntry.LaunchUri(exe)),"升级及移动目录后修复其他皮肤中已失效的旧路径");
        var unchanged=File.ReadAllText(oldBar);SkinEntry.RepairExisting(Path.Combine(root,"entry-docs"),skin,skinWriter,true,chineseExe);check(File.ReadAllText(oldBar)==unchanged,"入口自动修复重复执行不产生变化");
        SkinEntry.RepairExisting(Path.Combine(root,"entry-docs"),skin,skinWriter,false,chineseExe);check(!File.ReadAllText(oldBar).Contains("F24A"),"关闭入口同时清理其他皮肤中的旧入口");
    }
}
