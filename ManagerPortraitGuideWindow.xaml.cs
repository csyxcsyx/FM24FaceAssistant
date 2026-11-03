using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
namespace FM24FaceAssistant;

public partial class ManagerPortraitGuideWindow:Window
{
    readonly string? path;
    public ManagerPortraitGuideWindow(string? image)
    {
        path=image;InitializeComponent();
        Steps.Text=image==null?"在 FM 中打开：偏好选择 → 概况。\n找到“改变轮廓贴图”，点击右侧的 × 清除个人头像，再点击“确定”。\n助手的头像映射已恢复；游戏中的个人头像需要在这里取消。":"1　在 FM 中打开：偏好选择 → 概况。\n2　滚动到底部，点击“改变轮廓贴图”旁的“选择”。\n3　进入 Pictures → FM24FaceAssistant，选择下面的图片，载入后点击“确定”。";
        FileName.Text=image==null?"":Path.GetFileName(image);Folder.Text=image==null?"":Path.GetDirectoryName(image);
        FileBox.Visibility=image==null?Visibility.Collapsed:Visibility.Visible;CopyButton.Visibility=FolderButton.Visibility=FileBox.Visibility;
        Width=Math.Min(Width,SystemParameters.WorkArea.Width);Height=Math.Min(Height,SystemParameters.WorkArea.Height);MinWidth=Math.Min(MinWidth,Width);MinHeight=Math.Min(MinHeight,Height);
        SourceInitialized+=(_,_)=>WindowTheme.Apply(new WindowInteropHelper(this).Handle);
    }
    void CopyPath(object sender,RoutedEventArgs e){try{if(path!=null){Clipboard.SetText(path);Feedback.Text="图片路径已复制。";}}catch(Exception ex){Feedback.Text="复制失败："+ex.Message;}}
    void OpenFolder(object sender,RoutedEventArgs e){try{if(path!=null){var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};start.ArgumentList.Add("/select,");start.ArgumentList.Add(path);Process.Start(start);}}catch(Exception ex){Feedback.Text="打开失败："+ex.Message;}}
    void CloseGuide(object sender,RoutedEventArgs e)=>Close();
    void GuideKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}}
}
