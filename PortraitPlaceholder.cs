using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace FM24FaceAssistant;
public sealed class MissingPortraitVisibility : IValueConverter
{
    public object Convert(object value,Type target,object parameter,CultureInfo culture)=>value==null?Visibility.Visible:Visibility.Collapsed;
    public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
