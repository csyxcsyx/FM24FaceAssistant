using System;
using System.Threading.Tasks;
using System.Windows;
namespace FM24FaceAssistant;

public partial class MainWindow
{
    SkinInstallWindow? skinInstaller;
    async void OpenSkinInstaller(object sender,RoutedEventArgs e)=>await ShowSkinInstaller(false);
    async Task ShowSkinInstaller(bool startup)
    {
        if(skinInstaller!=null){skinInstaller.Activate();return;}
        if(cancel!=null||exitAfterTask){if(!startup)Report("请等待当前任务结束，再安装皮肤。");return;}
        try{
            Notice.Dismiss();skinInstaller=new SkinInstallWindow(settings.Documents,SkinPath.Text,settings.SkipSkinInstallPrompt,startup){Owner=this};
            skinInstaller.ShowDialog();settings.SkipSkinInstallPrompt=skinInstaller.SuppressPrompt;SkinPromptCheck.IsChecked=!settings.SkipSkinInstallPrompt;
            var selected=skinInstaller.SelectedSkin;if(selected!=null){settings.Skin=selected;SkinPath.Text=selected;}Persist();
            if(selected!=null)await Run(async ct=>{await SyncSkinPreference(ct);Report("皮肤位置已保存。请在 FM 偏好选择中选用 FLAYUS skin 24 YUXIANde，并重载皮肤。");});
        }catch(Exception ex){LogError(ex);Report("皮肤设置未完成："+ex.Message);}
        finally{skinInstaller=null;}
    }
    void SkinPromptChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;settings.SkipSkinInstallPrompt=SkinPromptCheck.IsChecked!=true;Persist();
    }
}
