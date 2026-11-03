using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace FM24FaceAssistant;
public static class SkinEntry
{
    static bool Owned(XElement element)
    {
        if((string?)element.Attribute("id")!="F24A")return false;
        var text=element.ToString();
        if(text.Contains("fm24face://open",StringComparison.OrdinalIgnoreCase)||text.Contains("fmfaceassistant://open",StringComparison.OrdinalIgnoreCase))return true;
        var url=(string?)element.Elements("record").FirstOrDefault(e=>(string?)e.Attribute("id")=="click_event")?.Attribute("url");
        return ((string?)element.Attribute("icon"))?.Contains("fm24_face_assistant",StringComparison.Ordinal)==true&&Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.IsFile&&new[]{"FM24FaceAssistant.exe","FM24头像助手.exe"}.Contains(Path.GetFileName(uri.LocalPath),StringComparer.OrdinalIgnoreCase);
    }
    public static string LaunchUri(string executable)
    {
        var path=Path.GetFullPath(executable);
        if(!File.Exists(path)||!Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("找不到头像助手程序，请从完整程序目录重新安装入口。");
        return new Uri(path).AbsoluteUri;
    }
    public static void Install(string skin,PatchWriter writer,string? executable=null)
    {
        var launch=LaunchUri(executable??(File.Exists(Path.Combine(AppContext.BaseDirectory,"FM24FaceAssistant.exe"))?Path.Combine(AppContext.BaseDirectory,"FM24FaceAssistant.exe"):Environment.ProcessPath!));
        var panels=Path.Combine(skin,"panels");if(!Directory.Exists(panels))throw new IOException("皮肤目录中没有 panels 文件夹。");
        var toolbar=Directory.EnumerateFiles(panels,"*.xml",SearchOption.AllDirectories).FirstOrDefault(path=>File.ReadAllText(path).Contains("id=\"bgs2\""))??throw new IOException("没有找到背景切换按钮 bgs2，未修改皮肤。");
        var originals=new Dictionary<string,string>();var documents=new Dictionary<string,XDocument>();
        void Open(string file){originals[file]=File.ReadAllText(file);documents[file]=PatchWriter.Parse(originals[file]);}
        Open(toolbar);var doc=documents[toolbar];var anchor=doc.Descendants().First(e=>(string?)e.Attribute("id")=="bgs2");
        var existing=doc.Descendants().Where(Owned).ToList();foreach(var entry in existing)entry.Remove();
        var popup=Path.Combine(panels,"background_selector","workings.xml");if(File.Exists(popup)){Open(popup);foreach(var entry in documents[popup].Descendants().Where(Owned).ToList())entry.Remove();}
        var icon=Path.Combine(skin,"graphics","icons","24px","fm24_face_assistant_mask.png");Directory.CreateDirectory(Path.GetDirectoryName(icon)!);if(!File.Exists(icon))File.WriteAllBytes(icon,CreateIcon());
        anchor.AddBeforeSelf(new XElement("widget",new XAttribute("class","menubar_icon_button"),new XAttribute("id","F24A"),new XAttribute("appearance",(string?)anchor.Attribute("appearance")??"chrome/buttons/items/button"),new XAttribute("icon","icons/24px/fm24_face_assistant_mask"),new XAttribute("width","42"),new XElement("translation",new XAttribute("id","hint"),new XAttribute("type","use"),new XAttribute("value","Face Assistant")),new XElement("record",new XAttribute("id","primary_icon_properties"),new XAttribute("red_replacement","white")),new XElement("record",new XAttribute("id","click_event"),new XAttribute("event_id","OURL"),new XAttribute("url",launch))));
        var changes=documents.Select(pair=>new Change(pair.Key,originals[pair.Key],pair.Value.ToString())).Where(c=>c.Before!=c.After).ToList();if(changes.Count>0)writer.Commit(changes,"皮肤入口");
    }
    public static void Remove(string skin,PatchWriter writer)
    {
        var changes=new List<Change>();foreach(var file in Directory.EnumerateFiles(Path.Combine(skin,"panels"),"*.xml",SearchOption.AllDirectories)){var before=File.ReadAllText(file);if(!before.Contains("F24A"))continue;var doc=PatchWriter.Parse(before);var entries=doc.Descendants().Where(Owned).ToList();if(entries.Count==0)continue;foreach(var entry in entries)entry.Remove();changes.Add(new(file,before,doc.ToString()));}
        if(changes.Count>0)writer.Commit(changes,"恢复皮肤入口");
    }
    static byte[] CreateIcon()
    {
        // FM recolours the red mask using primary_icon_properties.red_replacement.
        var visual=new DrawingVisual();using(var context=visual.RenderOpen()){context.DrawEllipse(Brushes.Red,null,new System.Windows.Point(12,7),4.5,4.5);context.DrawRoundedRectangle(Brushes.Red,null,new System.Windows.Rect(4,14,16,8),4,4);}
        var bitmap=new RenderTargetBitmap(24,24,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
}
