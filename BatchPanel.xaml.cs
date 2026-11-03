using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FM24FaceAssistant;
public partial class BatchPanel : UserControl
{
    BatchPlan? plan; int page;
    public event RoutedEventHandler? PreviewRequested,ApplyRequested,ApplyRefreshRequested,RefreshRequested,AutoChanged;
    public event Action<Person>? PersonRequested;
    public BatchPanel(){InitializeComponent();Render();}
    public CheckBox AutomaticCheck=>Automatic;
    public void InvalidatePlan(string reason="名单或分配已变化，请重新预览。") {plan=null;Stage.Text="需要预览";Outcome.Text=reason;Summary.ItemsSource=new[]{"点击扫描缺图，生成新的方案"};ApplyButton.IsEnabled=ApplyRefreshButton.IsEnabled=false;Render();}
    public void SetStage(string stage,string detail,bool canApply=false,bool canRefresh=false){Stage.Text=stage;Outcome.Text=detail;ApplyButton.IsEnabled=ApplyRefreshButton.IsEnabled=canApply;RefreshButton.IsEnabled=canRefresh;}
    public void ShowPlan(BatchPlan value){plan=value;page=0;ResultFilter.SelectedIndex=0;Summary.ItemsSource=new[]{$"缺图球员 {value.MissingPlayers:N0}",$"缺图职员 {value.MissingStaff:N0}",$"兼容补图 {value.Compatibility:N0}",$"待应用 {value.Pending:N0}",$"已保留 {value.Preserved:N0}",$"未完成 {value.Missing:N0}",$"同图复用 {value.Reused:N0}"};SetStage(value.Pending>0?"预览完成":"没有新增分配",value.Pending>0?"检查本次新增分配后应用。":"没有新的缺图，无需重复应用。",value.Pending>0,value.OutputCount>0);Render();}
    public void MarkApplied(bool refreshed,string? refreshError=null){if(plan!=null){plan=plan.Applied();Summary.ItemsSource=new[]{$"已应用 {plan.Cards.Count(c=>c.Status=="已应用"):N0}",$"已保留 {plan.Preserved:N0}",$"未完成 {plan.Missing:N0}"};}SetStage(refreshed?"刷新指令已发送":refreshError==null?"头像已应用":"头像已应用，刷新未完成",refreshError??(refreshed?"请在游戏中检查头像效果。":"点击“只刷新皮肤”重载游戏头像。"),false,true);Render();}
    void Render(){if(Cards==null)return;var filter=(ResultFilter.SelectedItem as ComboBoxItem)?.Content?.ToString()??"全部结果";int count=plan?.Cards.Count(c=>filter=="全部结果"||c.Status==filter)??0;int pages=Math.Max(1,(count+8)/9);page=Math.Clamp(page,0,pages-1);Cards.ItemsSource=plan?.Page(filter,page);EmptyState.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;EmptyText.Text=plan==null?"预览后在此查看匹配结果":"此筛选下没有结果";PageCount.Text=$"{count:N0} 项    每页 9 张";PageNumber.Text=$"{page+1} / {pages}";Previous.IsEnabled=page>0;Next.IsEnabled=page+1<pages;}
    void FilterChanged(object sender,SelectionChangedEventArgs e){page=0;Render();}
    void PreviousClick(object sender,RoutedEventArgs e){page--;Render();}void NextClick(object sender,RoutedEventArgs e){page++;Render();}
    void ViewPerson(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is BatchCard card)PersonRequested?.Invoke(card.Person);}
    void PreviewClick(object s,RoutedEventArgs e)=>PreviewRequested?.Invoke(this,e);void ApplyClick(object s,RoutedEventArgs e)=>ApplyRequested?.Invoke(this,e);void ApplyRefreshClick(object s,RoutedEventArgs e)=>ApplyRefreshRequested?.Invoke(this,e);void RefreshClick(object s,RoutedEventArgs e)=>RefreshRequested?.Invoke(this,e);void AutoClick(object s,RoutedEventArgs e)=>AutoChanged?.Invoke(this,e);
}
