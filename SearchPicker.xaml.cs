using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FM24FaceAssistant;

public partial class SearchPicker : UserControl
{
    public static readonly DependencyProperty PlaceholderProperty=DependencyProperty.Register(nameof(Placeholder),typeof(string),typeof(SearchPicker),new PropertyMetadata("All"));
    public string Placeholder{get=>(string)GetValue(PlaceholderProperty);set=>SetValue(PlaceholderProperty,value);}
    public static readonly DependencyProperty PopupWidthProperty=DependencyProperty.Register(nameof(PopupWidth),typeof(double),typeof(SearchPicker),new PropertyMetadata(280d));
    public double PopupWidth{get=>(double)GetValue(PopupWidthProperty);set=>SetValue(PopupWidthProperty,value);}
    public static readonly DependencyProperty TextProperty=DependencyProperty.Register(nameof(Text),typeof(string),typeof(SearchPicker),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,TextUpdated));
    public string Text{get=>(string)GetValue(TextProperty);set=>SetValue(TextProperty,value);}
    public static readonly RoutedEvent QueryChangedEvent=EventManager.RegisterRoutedEvent(nameof(QueryChanged),RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(SearchPicker));
    public event RoutedEventHandler QueryChanged{add=>AddHandler(QueryChangedEvent,value);remove=>RemoveHandler(QueryChangedEvent,value);}
    IReadOnlyList<PickerOption> options=Array.Empty<PickerOption>(); bool choosing;
    public string? SelectedValue{get;private set;}
    public bool CloseDropdown(){if(!Dropdown.IsOpen)return false;Dropdown.IsOpen=false;Editor.Focus();return true;}
    public SearchPicker(){InitializeComponent();GotKeyboardFocus+=(_,_)=>Frame.BorderBrush=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#A69AB8")!;LostKeyboardFocus+=(_,_)=>Frame.BorderBrush=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#706582")!;}
    public void SetOptions(IEnumerable<PickerOption> source){options=source.ToArray();Filter();}
    static void TextUpdated(DependencyObject target,DependencyPropertyChangedEventArgs e){var picker=(SearchPicker)target;if(picker.Editor==null)return;if(picker.Editor.Text!=(string)e.NewValue)picker.Editor.Text=(string)e.NewValue;}
    void Filter(bool all=false){var filtered=PickerOption.Filter(options,all?"":Text);OptionsList.ItemsSource=filtered;Empty.Visibility=filtered.Count==0?Visibility.Visible:Visibility.Collapsed;}
    void EditChanged(object sender,TextChangedEventArgs e){e.Handled=true;SetCurrentValue(TextProperty,Editor.Text);if(choosing)return;SelectedValue=null;Filter();if(Editor.IsKeyboardFocusWithin)Dropdown.IsOpen=true;RaiseEvent(new RoutedEventArgs(QueryChangedEvent));}
    void TogglePopup(object sender,RoutedEventArgs e){if(Dropdown.IsOpen)Dropdown.IsOpen=false;else{Filter(true);Dropdown.IsOpen=true;Editor.Focus();}}
    void Choose(PickerOption option){choosing=true;SelectedValue=option.Value;Text=option.Value;Editor.CaretIndex=Editor.Text.Length;choosing=false;Dropdown.IsOpen=false;Editor.Focus();RaiseEvent(new RoutedEventArgs(QueryChangedEvent));}
    void PickItem(object sender,MouseButtonEventArgs e){if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(OptionsList,source) is ListBoxItem item&&item.DataContext is PickerOption option)Choose(option);}
    void PickerKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape&&Dropdown.IsOpen){Dropdown.IsOpen=false;e.Handled=true;return;}
        if(e.Key is Key.Down or Key.Up){if(!Dropdown.IsOpen){Filter();Dropdown.IsOpen=true;}int count=OptionsList.Items.Count;if(count>0){int next=OptionsList.SelectedIndex+(e.Key==Key.Down?1:-1);OptionsList.SelectedIndex=Math.Clamp(next,0,count-1);OptionsList.ScrollIntoView(OptionsList.SelectedItem);}e.Handled=true;}
        else if(e.Key==Key.Enter&&Dropdown.IsOpen){if(OptionsList.SelectedItem is PickerOption selected)Choose(selected);else if(OptionsList.Items.Count==1)Choose((PickerOption)OptionsList.Items[0]);e.Handled=true;}
        else if(e.Key==Key.Tab)Dropdown.IsOpen=false;
    }
    void ListKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter&&OptionsList.SelectedItem is PickerOption option){Choose(option);e.Handled=true;}else if(e.Key==Key.Escape){Dropdown.IsOpen=false;Editor.Focus();e.Handled=true;}}
    void PopupClosed(object? sender,EventArgs e){OptionsList.SelectedItem=null;}
}
