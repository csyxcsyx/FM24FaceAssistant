using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace FM24FaceAssistant;
public partial class ToastNotice : UserControl
{
    CancellationTokenSource? lifetime;
    public ToastNotice(){InitializeComponent();}
    public void ShowMessage(string text,bool error=false)
    {
        lifetime?.Cancel();lifetime?.Dispose();lifetime=new();Message.Text=text;Message.ToolTip=text.Length>280?text:null;LogButton.Visibility=error?Visibility.Visible:Visibility.Collapsed;Icon.Text=error?"\uEA39":"\uE946";Visibility=Visibility.Visible;
        BeginAnimation(OpacityProperty,null);Opacity=1;if(SystemParameters.ClientAreaAnimation)BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
        _=Expire(error?6000:Math.Clamp(1800+text.Length*28,2400,4800),lifetime.Token);
    }
    async Task Expire(int milliseconds,CancellationToken ct){try{int elapsed=0;while(elapsed<milliseconds){await Task.Delay(100,ct);elapsed+=100;}if(SystemParameters.ClientAreaAnimation){BeginAnimation(OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(450)));await Task.Delay(450,ct);}Visibility=Visibility.Collapsed;}catch(OperationCanceledException){}}
    public void Dismiss(){lifetime?.Cancel();BeginAnimation(OpacityProperty,null);Visibility=Visibility.Collapsed;}
    void OpenLog(object s,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo(Files.State){UseShellExecute=true});}catch{} }
}
