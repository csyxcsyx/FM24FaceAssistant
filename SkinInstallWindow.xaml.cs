using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
namespace FM24FaceAssistant;

public partial class SkinInstallWindow:Window
{
    CancellationTokenSource? cancellation;bool closingAfterCancel,committing;
    public string? SelectedSkin {get;private set;}
    public bool SuppressPrompt=>NoPrompt.IsChecked==true;
    public SkinInstallWindow(string documents,string? existing,bool suppress,bool startup=false)
    {
        InitializeComponent();Destination.Text=SkinPackage.DefaultPath(documents);NoPrompt.IsChecked=suppress;
        if(!startup)Intro.Text="随附 FLAYUS skin 24 YUXIANde，包含特殊角色 UID 显示和头像助手入口。";
        if(SkinPackage.IsInstalled(existing)){Destination.Text=existing!;State.Text="已找到此皮肤。可以直接使用，也可以更改位置后安装另一份。";}
        if(SkinPackage.BundlePath()==null)State.Text="随附安装包缺失。可选择已有皮肤，或检查程序的 Bundled 文件夹。";
        Width=Math.Min(Width,SystemParameters.WorkArea.Width);Height=Math.Min(Height,SystemParameters.WorkArea.Height);MinWidth=Math.Min(MinWidth,Width);MinHeight=Math.Min(MinHeight,Height);
        SourceInitialized+=(_,_)=>WindowTheme.Apply(new WindowInteropHelper(this).Handle);
    }
    void BrowseDestination(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFolderDialog{Title="选择存放皮肤的位置，程序会在其中创建皮肤文件夹"};
        if(picker.ShowDialog(this)==true)Destination.Text=Path.GetFileName(picker.FolderName).Equals(SkinPackage.Name,StringComparison.OrdinalIgnoreCase)?picker.FolderName:Path.Combine(picker.FolderName,SkinPackage.Name);
    }
    void ChooseExisting(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFolderDialog{Title="选择已有皮肤文件夹（包含 skin_config.xml）"};if(picker.ShowDialog(this)!=true)return;
        if(!SkinPackage.IsInstalled(picker.FolderName)){State.Text="该目录不是完整的 FLAYUS skin 24 YUXIANde 皮肤，请选择其根文件夹。";return;}
        SelectedSkin=picker.FolderName;DialogResult=true;
    }
    async void InstallSkin(object sender,RoutedEventArgs e)
    {
        if(SelectedSkin!=null){DialogResult=true;return;}
        if(cancellation!=null)return;
        try{
            var destination=Path.GetFullPath(Destination.Text.Trim());
            if(SkinPackage.IsInstalled(destination)){SelectedSkin=destination;DialogResult=true;return;}
            var archive=SkinPackage.BundlePath()??throw new IOException("未找到随附安装包，请保留程序的 Bundled 文件夹。");
            cancellation=new();committing=false;ProgressArea.Visibility=Visibility.Visible;InstallProgress.IsIndeterminate=true;
            Destination.IsEnabled=BrowseButton.IsEnabled=ExistingButton.IsEnabled=InstallButton.IsEnabled=false;LaterButton.Content="取消安装";State.Text="正在安装，请稍候…";
            var source=cancellation;IProgress<TaskProgress> progress=new ThrottledTaskProgress(new Progress<TaskProgress>(value=>{
                if(cancellation!=source)return;committing=!value.CanCancel;LaterButton.IsEnabled=value.CanCancel;
                InstallProgress.IsIndeterminate=value.Percent==null;InstallProgress.Value=value.Percent??0;
                ProgressText.Text=value.Stage+(value.Percent is double percent?$"  {percent:F0}%":"");CurrentFile.Text=value.Current??"";
            }));
            SelectedSkin=await Task.Run(()=>SkinPackage.Install(archive,destination,source.Token,progress));
            State.Text="安装完成。点击“完成”保存位置，然后在 FM 偏好选择中选用该皮肤并重载。";InstallProgress.IsIndeterminate=false;InstallProgress.Value=100;ProgressText.Text="皮肤已安装";CurrentFile.Text="";InstallButton.Content="完成";LaterButton.Content="关闭";
        }catch(OperationCanceledException){State.Text="安装已取消，没有写入不完整的皮肤。";ProgressText.Text="已取消";}
        catch(Exception ex){State.Text="安装未完成："+ex.Message;ProgressText.Text="可更改位置后重试，或选择已有皮肤。";}
        finally{
            cancellation?.Dispose();cancellation=null;committing=false;Destination.IsEnabled=BrowseButton.IsEnabled=ExistingButton.IsEnabled=SelectedSkin==null;InstallButton.IsEnabled=LaterButton.IsEnabled=true;
            if(SelectedSkin==null)LaterButton.Content="暂不安装";if(closingAfterCancel)Close();
        }
    }
    void CloseInstaller(object sender,RoutedEventArgs e)=>Close();
    void InstallerClosing(object? sender,CancelEventArgs e)
    {
        if(cancellation==null)return;e.Cancel=true;if(committing){State.Text="正在保存皮肤，即将完成。";return;}closingAfterCancel=true;LaterButton.IsEnabled=false;State.Text="正在取消…";cancellation.Cancel();
    }
    void InstallerKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}}
}
