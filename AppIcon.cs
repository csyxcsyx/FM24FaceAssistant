using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FM24FaceAssistant;
public enum AppIconKind { Refresh, Skin, Information, Warning }

/// <summary>Font-independent icons, drawn in a 24-unit viewport.</summary>
public sealed class AppIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(AppIconKind),typeof(AppIcon),new FrameworkPropertyMetadata(AppIconKind.Information,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty=TextElement.ForegroundProperty.AddOwner(typeof(AppIcon),new FrameworkPropertyMetadata(Brushes.Goldenrod,FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender));
    public AppIconKind Kind {get=>(AppIconKind)GetValue(KindProperty);set=>SetValue(KindProperty,value);}
    public Brush Foreground {get=>(Brush)GetValue(ForegroundProperty);set=>SetValue(ForegroundProperty,value);}
    static readonly Geometry refresh=Shape("M20,10 A8,8 0 0 0 5,7 M5,3 L5,7 L9,7 M4,14 A8,8 0 0 0 19,17 M19,21 L19,17 L15,17");
    static readonly Geometry skin=Shape("M8,3 L4,5 L1,10 L5,13 L7,10 L7,21 L17,21 L17,10 L19,13 L23,10 L20,5 L16,3 C16,8 8,8 8,3 Z");
    static readonly Geometry information=Shape("M12,2 A10,10 0 1 1 11.999,2 M12,11 L12,17");
    static readonly Geometry warning=Shape("M12,2 L22,21 L2,21 Z M12,8 L12,13");
    static Geometry Shape(string path){var g=Geometry.Parse(path);g.Freeze();return g;}
    public AppIcon(){Width=16;Height=16;IsHitTestVisible=false;Focusable=false;}
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);var scale=Math.Min(RenderSize.Width,RenderSize.Height)/24;if(scale<=0)return;
        context.PushTransform(new MatrixTransform(scale,0,0,scale,(RenderSize.Width-24*scale)/2,(RenderSize.Height-24*scale)/2));
        var pen=new Pen(Foreground,1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        context.DrawGeometry(null,pen,Kind switch{AppIconKind.Refresh=>refresh,AppIconKind.Skin=>skin,AppIconKind.Warning=>warning,_=>information});
        if(Kind==AppIconKind.Information)context.DrawEllipse(Foreground,null,new Point(12,7),1,1);
        if(Kind==AppIconKind.Warning)context.DrawEllipse(Foreground,null,new Point(12,17),1,1);
        context.Pop();
    }
}
public static class AppIconAssets
{
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/FM24FaceAssistant;component/assistant.ico",UriKind.Absolute))?.Stream??throw new InvalidOperationException("内置程序图标缺失。");
        using var source=new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)source.Clone();
    }
}
