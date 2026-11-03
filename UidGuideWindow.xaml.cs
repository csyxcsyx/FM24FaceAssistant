using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
namespace FM24FaceAssistant;
public partial class UidGuideWindow:Window
{
    public UidGuideWindow(){InitializeComponent();Width=Math.Min(Width,SystemParameters.WorkArea.Width);Height=Math.Min(Height,SystemParameters.WorkArea.Height);MinWidth=Math.Min(MinWidth,Width);MinHeight=Math.Min(MinHeight,Height);SourceInitialized+=(_,_)=>WindowTheme.Apply(new WindowInteropHelper(this).Handle);}
    void CloseGuide(object sender,RoutedEventArgs e)=>Close();
    void GuideKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}}
}
