using FM24FaceAssistant;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class IconChecks
{
    static byte[] Render(AppIconKind kind,double scale,string font)
    {
        var icon=new AppIcon{Kind=kind,Width=24,Height=24,Foreground=Brushes.White};
        TextElement.SetFontFamily(icon,new FontFamily(font));icon.Measure(new Size(24,24));icon.Arrange(new Rect(0,0,24,24));icon.UpdateLayout();
        int side=(int)(24*scale);var image=new RenderTargetBitmap(side,side,96*scale,96*scale,PixelFormats.Pbgra32);image.Render(icon);
        var bytes=new byte[side*side*4];image.CopyPixels(bytes,side*4,0);return bytes;
    }
    public static void Run(Action<bool,string> check)
    {
        foreach(var kind in Enum.GetValues<AppIconKind>())foreach(var scale in new[]{1d,1.5,2d})
        {
            var expected=Render(kind,scale,"Segoe UI");var absentFont=Render(kind,scale,"FM24 Missing Icon Font 0000");
            check(expected.Any(b=>b!=0)&&expected.SequenceEqual(absentFont),$"{kind} 在 {scale*100:F0}% 缩放及不存在的字体下仍正常绘制");
        }
        using(var icon=AppIconAssets.CreateTrayIcon())check(icon.Width>0&&icon.Height>0,"托盘图标由编译进程序的 ICO 资源载入");
        var toast=new ToastNotice();toast.ShowMessage("test",false);check(((AppIcon)toast.FindName("Icon")).Kind==AppIconKind.Information,"普通提示使用矢量信息图标");toast.ShowMessage("test",true);check(((AppIcon)toast.FindName("Icon")).Kind==AppIconKind.Warning,"错误提示使用矢量警告图标");toast.Dismiss();
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"FM24FaceAssistant.csproj")))root=root.Parent;
        if(root==null)throw new Exception("Cannot find source root for icon dependency audit.");
        var files=Directory.GetFiles(root.FullName,"*.xaml").Concat(Directory.GetFiles(root.FullName,"*.cs"));
        foreach(var file in files)
        {
            var text=File.ReadAllText(file);
            bool privateGlyph=text.EnumerateRunes().Any(r=>(r.Value>=0xe000&&r.Value<=0xf8ff)||(r.Value>=0xf0000&&r.Value<=0xffffd)||(r.Value>=0x100000&&r.Value<=0x10fffd));
            check(!privateGlyph&&!Regex.IsMatch(text,@"Segoe\s+(Fluent\s+Icons|MDL2\s+Assets|UI\s+Symbol)|Wingdings|Webdings|(?:\\u|&#x)(?:[eE][0-9a-fA-F]{3}|[fF][0-8][0-9a-fA-F]{2})"),"图标字体依赖审计："+Path.GetFileName(file));
        }
        foreach(var file in Directory.GetFiles(root.FullName,"*.xaml"))
        {
            var doc=System.Xml.Linq.XDocument.Load(file);
            if(doc.Root?.Name.LocalName=="Window")check(doc.Root.Attribute("Icon")?.Value=="assistant.ico","窗口内置图标："+Path.GetFileName(file));
        }
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(36,33,55)),null,new Rect(0,0,360,120));int column=0;
            foreach(var kind in Enum.GetValues<AppIconKind>())
            {
                var bytes=Render(kind,2,"FM24 Missing Icon Font 0000");var image=BitmapSource.Create(48,48,96,96,PixelFormats.Pbgra32,null,bytes,48*4);
                drawing.DrawImage(image,new Rect(column*90+21,16,48,48));
                drawing.DrawText(new FormattedText(kind.ToString(),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.White,1),new Point(column*90+10,82));column++;
            }
        }
        var preview=new RenderTargetBitmap(360,120,96,96,PixelFormats.Pbgra32);preview.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(preview));Directory.CreateDirectory(Path.Combine(root.FullName,"artifacts"));using var output=File.Create(Path.Combine(root.FullName,"artifacts","icon-preview.png"));encoder.Save(output);
    }
}
