using FM24FaceAssistant;
using System.Xml.Linq;
using System.IO;
static class UpgradeChecks
{
    public static void Run(Action<bool,string> check,Person person,Face face,string root)
    {
        var p=person with{Nation="Germany",Group=null};
        Face F(string group)=>face with{Group=group,Hash=group,VisualHash=group};
        var local=F("CentralEurope");var nearby=F("France");var broad=F("Poland");var china=F("China");
        var usage=new Dictionary<string,int>{{local.UniqueKey,2},{nearby.UniqueKey,2}};
        var pool=new List<Face>{local,nearby,broad,china};
        check(RegionResolver.Available(p,pool,usage).Any(f=>f==broad),"本国及邻近素材耗尽后继续查找未用兼容素材");
        var rows=new Matcher().Assign(new[]{p,p with{Id=p.Id+1},p with{Id=p.Id+2}},pool,new(),new List<Mapping>(),out _);
        check(rows.Select(a=>a.Face.UniqueKey).Distinct().Count()==3&&!rows.Any(a=>a.Face.Group=="China"),"批量匹配用完兼容未用头像后才重复，排除无关区域");
        var random=RegionResolver.Available(p,pool,usage);check(random.Where(f=>usage.GetValueOrDefault(f.UniqueKey)==random.Min(x=>usage.GetValueOrDefault(x.UniqueKey))).All(f=>f.Group=="Poland"),"推荐与随机重选共享兼容未用素材优先级");
        var state=Path.Combine(root,"version-state");var profile="A";var record=Path.Combine(state,"rows-"+Files.Hash(profile)+".json");var other=Path.Combine(root,"version-external.xml");Directory.CreateDirectory(state);File.WriteAllText(record,"[]");File.WriteAllText(other,"baseline");
        var writer=new PatchWriter(state);writer.Commit(new(){new(record,"[]","[1]"),new(other,"baseline","batch")},"头像补丁");
        var first=RestorePoints.List(state,profile).Single();
        writer.Commit(new(){new(record,"[1]","[2]"),new(other,"batch","manual")},"头像补丁");
        File.WriteAllText(other,"batch"); // Known mixed revision, as observed on the user's installation.
        RestorePoints.Restore(state,profile,first,writer);
        check(File.ReadAllText(record)=="[]"&&File.ReadAllText(other)=="baseline","选时间点恢复已知混合状态，并保存当前状态");
        var undoRestore=RestorePoints.List(state,profile).First();RestorePoints.Restore(state,profile,undoRestore,writer);
        check(File.ReadAllText(record)=="[2]"&&File.ReadAllText(other)=="batch","恢复操作本身可再次回退");
        File.WriteAllText(other,"unrelated external edit");bool blocked=false;try{RestorePoints.Restore(state,profile,first,writer);}catch(IOException){blocked=true;}
        check(blocked&&File.ReadAllText(record)=="[2]","未知外部修改阻止整个恢复，不产生部分覆盖");
        check(RestorePoints.List(state,"B").Count==0,"恢复版本按存档记录隔离");
        var repeated=new string('x',1_000_000);var blob=SnapshotStore.Put(state,repeated);var again=SnapshotStore.Put(state,repeated);
        check(blob==again&&SnapshotStore.Read(blob)==repeated&&new FileInfo(blob).Length<10_000,"快照压缩去重，同一内容仅存一份");
        using(var cts=new CancellationTokenSource()){cts.Cancel();bool cancelled=false;try{RestorePoints.Restore(state,profile,first,writer,cts.Token);}catch(OperationCanceledException){cancelled=true;}check(cancelled&&File.ReadAllText(record)=="[2]","恢复提交前可取消");}
        var specialState=Path.Combine(root,"special-state");var specialGraphics=Path.Combine(root,"special-graphics");var specialWriter=new PatchWriter(specialState);var special=new Assignment(p with{Id=2002077931,Name="Rex Fang",Newgen=true,Kind="职员",Dob="special"},face with{Kind="职员"},true,"主教练");
        specialWriter.Apply(specialGraphics,"A",new(){special},new(),special:true);
        var path=Path.Combine(specialGraphics,"FM24FaceAssistant","special","config.xml");var xml=XDocument.Load(path);
        check(xml.Descendants("record").Count(e=>e.Attribute("to")!=null)==2&&File.ReadAllText(path).Contains("r-2002077931"),"特殊角色使用实际UID输出大小头像映射");
        check(ManagerPortrait.IsSelf(special.Person with{Club="主教练（我）"})&&!ManagerPortrait.IsSelf(special.Person with{Club="新闻官"}),"仅玩家主教练使用专用头像设置流程");
        var export=ManagerPortrait.Export(Path.Combine(specialGraphics,"FM24FaceAssistant","special",face.Hash+".png"),special.Person.Id,Path.Combine(root,"pictures"));
        check(Path.GetFileName(export)=="r-2002077931.png"&&File.ReadAllBytes(export).SequenceEqual(File.ReadAllBytes(Path.Combine(specialGraphics,"FM24FaceAssistant","special",face.Hash+".png"))),"主教练图片导出保留正确编号和图片内容");
        File.WriteAllText(export,"stale");ManagerPortrait.Export(Path.Combine(specialGraphics,"FM24FaceAssistant","special",face.Hash+".png"),special.Person.Id,Path.Combine(root,"pictures"));
        check(File.ReadAllBytes(export).SequenceEqual(File.ReadAllBytes(Path.Combine(specialGraphics,"FM24FaceAssistant","special",face.Hash+".png")))&&!Directory.EnumerateFiles(Path.GetDirectoryName(export)!,"*.tmp").Any(),"再次导出更新原文件且不残留临时文件");
        bool invalidManager=false;try{ManagerPortrait.Export(face.Path,-1,root);}catch(IOException){invalidManager=true;}check(invalidManager,"主教练导出拒绝无效UID");
        check(ManagerPortrait.Export(face.Path,special.Person.Id,Path.Combine(root,"pictures"),"A")!=ManagerPortrait.Export(face.Path,special.Person.Id,Path.Combine(root,"pictures"),"B"),"主教练导出按存档记录隔离，重复UID不覆盖另一份记录");
        specialWriter.Deactivate(specialGraphics);check(!XDocument.Load(path).Descendants("record").Any(e=>e.Attribute("to")!=null),"停用或切换存档时一并停用特殊角色输出");
        var migrated=Path.Combine(root,"migrated-state");Directory.CreateDirectory(migrated);foreach(var file in Directory.GetFiles(state,"*",SearchOption.AllDirectories)){var dest=Path.Combine(migrated,Path.GetRelativePath(state,file));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(file,dest);}
        Files.Save(Path.Combine(migrated,"legacy-roots.json"),new[]{state});
        check(RestorePoints.List(migrated,profile).Count==RestorePoints.List(state,profile).Count&&PatchJournal.Load(Path.Combine(migrated,"backups",Path.GetFileName(first.Folder)))!.Any(c=>c.Path==Path.Combine(migrated,Path.GetFileName(record))),"数据迁移后旧恢复点识别且目标路径转至统一目录");
        check(StateLocation.Redirect(Path.Combine(state,"snapshot-blobs","test.gz"),migrated)==Path.Combine(migrated,"snapshot-blobs","test.gz")&&StateLocation.Redirect(other,migrated)==other,"迁移只改数据根目录，不改外部头像包路径");
        var compressState=Path.Combine(root,"compress-state");var legacyFolder=Path.Combine(compressState,"backups","20260101-000000-0000000");Directory.CreateDirectory(legacyFolder);Files.Atomic(Path.Combine(legacyFolder,"0.before"),"old");Files.Atomic(Path.Combine(legacyFolder,"0.after"),"new");Files.Save(Path.Combine(legacyFolder,"transaction.json"),new JournalManifest(2,new(){new(record,"0.before","0.after",Files.Hash("old"),Files.Hash("new"))}));
        BackupCompaction.Run(compressState);check(Files.Read<JournalManifest>(Path.Combine(legacyFolder,"transaction.json"))!.Version==3&&!File.Exists(Path.Combine(legacyFolder,"0.before"))&&PatchJournal.Load(legacyFolder)!.Single().Before=="old","旧备份压缩后保留内容、清单与恢复能力");
        var toast=new ToastNotice();toast.ShowMessage("这是需要居中换行显示的消息。",true);var noticeIcon=toast.FindName("Icon") as System.Windows.Controls.TextBlock;check(toast.MinWidth<=620&&toast.Width==620&&noticeIcon?.FontFamily.Source=="Segoe UI"&&noticeIcon.Text=="!","提示条图标使用通用字体且无缺字符");toast.Dismiss();
        var app=System.Windows.Application.Current as App??new App();app.InitializeComponent();var panel=new SpecialPortraitPanel();panel.Configure(new(){face},new List<Assignment>(),new(),new[]{"China","England"});check(true,"特殊角色界面及国籍搜索组件加载");
        var managerGuide=new ManagerPortraitGuideWindow(export);check(managerGuide.Title=="主教练头像设置","主教练设置子窗口可加载");managerGuide.Close();
    }
}

