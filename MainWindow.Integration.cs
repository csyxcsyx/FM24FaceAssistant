using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
namespace FM24FaceAssistant;
public partial class MainWindow
{
    async Task SyncSkinPreference(CancellationToken ct)
    {
        var found=SkinPackage.FindInstalled(settings.Documents,SkinPath.Text);
        if(found!=null){SkinPath.Text=found;if(settings.Skin!=found){settings.Skin=found;Persist();}}
        var skin=SkinPath.Text;var enabled=settings.ShowSkinEntry;string status="";
        await Sta(()=>{
            ct.ThrowIfCancellationRequested();
            try{Integration.Protocol(true);}catch(Exception ex){LogError(ex);status="唤出协议未配置："+ex.Message+"\n";}
            try{
                SkinEntry.RepairExisting(settings.Documents,skin,writer,enabled);
                if(!SkinPackage.IsInstalled(skin)){status+="未找到配套皮肤。可安装随附皮肤，或选择已有目录；通用换图功能仍可使用。";return;}
                if(enabled)SkinEntry.Install(skin,writer);else SkinEntry.Remove(skin,writer);
                UidSkin.Install(skin,writer);
                status+=enabled?"入口已配置；重载游戏皮肤后显示。":"入口已关闭；重载游戏皮肤后隐藏。";
            }catch(Exception ex){LogError(ex);status+="入口未更新："+ex.Message;}
        });
        SkinEntryState.Text=status;
    }
    async void SkinEntryChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;if(cancel!=null){ShowSkinEntry.IsChecked=settings.ShowSkinEntry;Report("请等待当前任务结束，再更改入口设置。");return;}
        settings.ShowSkinEntry=ShowSkinEntry.IsChecked==true;settings.Skin=SkinPath.Text;Persist();
        await Run(async ct=>{await SyncSkinPreference(ct);Report(SkinEntryState.Text);});
    }
    async void RestoreOriginal(object sender,RoutedEventArgs e)
    {
        bool refresh=RefreshAfterRestore.IsChecked==true;bool hadManager=specialRows.Exists(a=>ManagerPortrait.IsSelf(a.Person));
        await Run(async ct=>{
            await Task.Run(()=>OriginalPortraits.Restore(Files.State,Path.Combine(settings.Documents,"graphics"),settings.Profile,writer,ct,taskProgress),ct);
            assigned=new();await Task.Run(()=>storage.Save(settings.Profile,assigned));BatchView.AutomaticCheck.IsChecked=false;recommended=null;InvalidateBatch();LoadSpecialPortraits();UpdatePeople();
            if(refresh&&System.Diagnostics.Process.GetProcessesByName("fm").Length>0){try{await Integration.RefreshAsync(settings.RefreshKey);Report("已恢复原头像包，刷新指令已发送。");}catch(Exception ex){Report("已恢复原头像包；刷新未完成："+ex.Message);}}
            else Report("已恢复原头像包，名单和图库已保留。");
            if(hadManager)SpecialView.ShowManagerSetup(null);
        });
    }
}
