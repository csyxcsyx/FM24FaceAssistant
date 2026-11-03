using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FM24FaceAssistant;

public partial class MainWindow
{
    async Task ApplyRows(List<Assignment> rows, CancellationToken ct) { taskProgress?.Report(new("核对人物身份",0,null)); await Task.Run(()=>SessionGuard.Verify(rows.Select(a=>a.Person),settings.Profile,ct,taskProgress),ct); mappings=await QueryMappings(rows.SelectMany(a=>a.OutputKeys.SelectMany(k=>new[]{ $"graphics/pictures/person/{k}/portrait",$"graphics/pictures/person/{k}/icon"})),ct); ct.ThrowIfCancellationRequested(); Report("准备头像与恢复备份…"); await Sta(() => writer.Apply(Path.Combine(settings.Documents, "graphics"), settings.Profile, rows, mappings, ct, null, taskProgress)); assigned = rows; LoadSpecialPortraits();  taskProgress?.Report(new("保存分配记录",0,rows.Count,null,false)); await Task.Run(()=>storage.Save(settings.Profile,rows,taskProgress));taskProgress?.Report(new("保存分配记录",rows.Count,rows.Count,null,false)); if(!batchOperation)InvalidateBatch();else batch=null; PersonSelected(this, new SelectionChangedEventArgs(SelectorEvent, new object[0], new object[0])); Report($"已应用 {rows.Count:N0} 人的头像。请在方便时刷新游戏。"); }
    static readonly RoutedEvent SelectorEvent = System.Windows.Controls.Primitives.Selector.SelectionChangedEvent;
    async void ApplyFace(object sender, RoutedEventArgs e) => await ApplySelected(false);
    async void ApplyRefresh(object sender, RoutedEventArgs e) => await ApplySelected(true);
    async Task ApplySelected(bool refresh) { if (PeopleList.SelectedItem is not Person p || pending == null) { Report("请先选择人物和待应用头像。"); return; }  var choice = new Assignment(p, pending, LockFace.IsChecked == true, "手动指定", PersonSearch.RequestedKeys(p,Search.Text) ?? assigned.FirstOrDefault(a=>a.Person.Identity==p.Identity)?.TargetKeys); await Run(async ct => { var rows = assigned.Where(a => a.Person.Id != p.Id).Append(choice).ToList(); await ApplyRows(rows, ct); if (refresh) { try { await Integration.RefreshAsync(settings.RefreshKey); Report("头像已应用，刷新指令已发送。"); } catch(Exception ex) { Report("头像已应用；刷新未完成："+ex.Message); } } }); }
    async void RestorePerson(object sender, RoutedEventArgs e) { if (PeopleList.SelectedItem is not Person p) return; await Run(ct => ApplyRows(assigned.Where(a => a.Person.Id != p.Id).ToList(), ct)); }
    async void ChoosePack(object sender, RoutedEventArgs e) { var dlg = new OpenFolderDialog { Title = "选择 NG_Regens 图库" }; if (dlg.ShowDialog() != true) return; PackPath.Text = dlg.FolderName; settings.Pack = dlg.FolderName; await Run(async ct => { faces = await Task.Run(() => Catalog.Faces(settings.Pack, ct, new Progress<string>(Report))); Files.Save(Path.Combine(Files.State, "catalog.json"), faces); CanonicalizeAssignments(); LoadSpecialPortraits(); InvalidateBatch(); Persist(); UpdateGallery(); Report($"图库索引完成：{faces.Count:N0} 张，{faces.Select(f => f.Group).Distinct().Count()} 个分类，{faces.Select(f => f.UniqueKey).Distinct().Count():N0} 张不重复图片。"); }); }
    readonly PopulationChangeGate populationGate=new(); bool probing;
    void AutoScanChanged(object sender,RoutedEventArgs e){if(!ready)return;settings.Auto=BatchView.AutomaticCheck.IsChecked==true;populationGate.Reset();Persist();}
    async void AutoTick(object? sender,EventArgs e)
    {
        if(CheckGameLifetime())return;
        if(skinInstaller!=null)return;
        if(BatchView.AutomaticCheck.IsChecked!=true||cancel!=null||probing)return;
        probing=true;
        try{
            var sample=await Task.Run(PopulationProbe.Read);
            if(sample==null)return;
            var connected=await Task.Run(()=>SessionGuard.AnchorFor(settings.Profile));
            if(connected.Length==0){BatchView.AutomaticCheck.IsChecked=false;Report("请更新一次名单，再启用新人自动补全；之后同一游戏会话可复用连接。");return;}
            if(sample.Anchor!=connected){BatchView.AutomaticCheck.IsChecked=false;Report("存档连接已变化，请更新名单后重新启用自动补全。");return;}
            bool changed=populationGate.Observe(sample,people.Count);
            if(populationGate.SessionChanged){BatchView.AutomaticCheck.IsChecked=false;Report("游戏会话已变化，请更新名单后重新启用自动补全。");return;}
            if(!changed)return;
            await Run(async ct=>{
                Report("检测到游戏人物变化，读取新增资料…");
                var scanned=await Task.Run(()=>new GameReader().ReadIncremental(ct,people),ct);
                var identities=people.Select(p=>p.Identity).ToHashSet();
                if(people.Count>100&&scanned.Count(p=>identities.Contains(p.Identity))<people.Count*0.7){BatchView.AutomaticCheck.IsChecked=false;Report("可能切换了存档，自动补全已暂停，请更新名单。");return;}
                var added=scanned.Where(p=>!identities.Contains(p.Identity)).ToList();
                MergePeople(scanned);await Task.Run(()=>SessionGuard.Bind(settings.Profile),ct);
                if(added.Count==0)return;
                Report($"发现 {added.Count} 名新人物，匹配头像…");
                var targets=await QueryMappings(added.SelectMany(p=>p.Keys.Select(k=>$"graphics/pictures/person/{k}/portrait")),ct);
                var result=await Task.Run(()=>{var alive=people.Select(p=>p.Identity).ToHashSet();var issues=new List<MatchIssue>();var rows=matcher.Assign(added,faces,assigned.Where(a=>alive.Contains(a.Person.Identity)).ToList(),targets,out _,ct,true,null,issues);return(rows,issues);},ct);
                BatchView.ShowPlan(BatchPlan.Create(added,result.rows,assigned,targets,result.issues));
                if(result.rows.Count!=assigned.Count||result.rows.Any(a=>!assigned.Any(b=>b.Person.Identity==a.Person.Identity&&b.Face.UniqueKey==a.Face.UniqueKey))){batchOperation=true;try{await ApplyRows(result.rows,ct);BatchView.MarkApplied(false);}finally{batchOperation=false;}}
                else Report($"已检查 {added.Count} 名新人物，暂无可补全头像。");
            },true);
        }catch(Exception ex){LogError(ex);}finally{probing=false;}
    }
}
