using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FM24FaceAssistant;
public partial class MainWindow
{
    bool batchOperation;
    void InvalidateBatch(){batch=null;if(ready)BatchView.InvalidatePlan();}
    void InspectBatchPerson(Person person){Tabs.SelectedIndex=0;Search.Text=person.Id.ToString();NationFilter.Text="";GroupFilter.Text="";KindFilter.SelectedIndex=0;UpdatePeople();PeopleList.SelectedItem=people.FirstOrDefault(p=>p.Identity==person.Identity);}
    async void PreviewMatch(object sender,RoutedEventArgs e)=>await Run(async ct=>{
        batch=null;BatchView.SetStage("正在预览","检查已有头像并生成候选方案…");
        try{if(faces.Count==0)throw new InvalidOperationException("尚未加载可用头像。请在“设置与恢复 → 常用”点击“下载 / 接续图库”，完成后重新扫描缺图。");if(!await UpdateRoster(ct))return;await ScanMappings(ct);taskProgress?.Report(new("匹配缺失头像",0,people.Count));var result=await Task.Run(()=>{var issues=new List<MatchIssue>();var rows=matcher.Assign(people,faces,assigned,mappings,out _,ct,false,taskProgress,issues);var active=PatchJournal.LoadActive(System.IO.Path.Combine(Files.State,"active.json"));var outputReady=active?.Profile==settings.Profile&&System.IO.File.Exists(System.IO.Path.Combine(settings.Documents,"graphics","FM24FaceAssistant","config.xml"));var plan=BatchPlan.Create(people,rows,outputReady?assigned:new(),mappings,issues);return(rows,plan);},ct);batch=result.plan.Pending>0?result.rows:null;BatchView.ShowPlan(result.plan);Report("预览完成，尚未写入头像。检查结果后应用。");}
        catch(OperationCanceledException){BatchView.SetStage("预览已取消","未写入任何头像，可重新预览。");throw;}
        catch(Exception ex){BatchView.SetStage("预览失败",ex.Message);throw;}
    });
    async void ApplyBatch(object sender,RoutedEventArgs e)=>await ApplyBatchPlan(false);
    async void ApplyBatchRefresh(object sender,RoutedEventArgs e)=>await ApplyBatchPlan(true);
    async Task ApplyBatchPlan(bool refresh)
    {
        if(batch==null){Report("没有待应用的新头像，请先扫描缺图。");return;}
        var rows=batch;
        await Run(async ct=>{batchOperation=true;BatchView.SetStage("正在应用","生成头像并保存配置，请稍候…");
            try{await ApplyRows(rows,ct);BatchView.MarkApplied(false);if(refresh){try{await Integration.RefreshAsync(settings.RefreshKey);BatchView.MarkApplied(true);Report("批量头像已应用，刷新指令已发送；请在游戏中检查效果。");}catch(Exception ex){BatchView.MarkApplied(false,ex.Message);Report("头像已应用，刷新未完成："+ex.Message);}}}
            catch(OperationCanceledException){BatchView.SetStage("应用已取消","未提交的分配不会生效；可重试当前方案。",true);throw;}
            catch(Exception ex){BatchView.SetStage("应用失败",ex.Message+"；可重试或在设置中恢复。",true);throw;}
            finally{batchOperation=false;}
        });
    }
    async void RefreshBatch(object sender,RoutedEventArgs e)=>await Run(async ct=>{try{BatchView.SetStage("正在刷新","等待游戏获得焦点…");await Integration.RefreshAsync(settings.RefreshKey);BatchView.MarkApplied(true);Report("刷新指令已发送，请在游戏中检查头像。");}catch(Exception ex){BatchView.MarkApplied(false,ex.Message);Report("刷新未完成："+ex.Message);}});
}
