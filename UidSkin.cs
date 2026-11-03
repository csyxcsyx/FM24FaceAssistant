using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
namespace FM24FaceAssistant;
public static class UidSkin
{
    // FM24 UID Finder binds the inbox sender object to its id property inside FM.
    // This is a skin binding, not an external memory address or a shared role ID.
    public static void Install(string skin,PatchWriter writer)
    {
        var file=Path.Combine(skin,"panels","human","inbox item.xml");
        if(!File.Exists(file))return;
        var before=File.ReadAllText(file);var doc=PatchWriter.Parse(before);
        if(doc.Descendants().Any(e=>(string?)e.Attribute("id")=="FU06"))return;
        foreach(var old in doc.Descendants().Where(e=>(string?)e.Attribute("id") is "FUID" or "FU02" or "FU03" or "FU04" or "FU05").ToList())old.Remove();
        var portrait=doc.Descendants("widget").FirstOrDefault(e=>(string?)e.Attribute("id")=="snpc"&&(string?)e.Attribute("class")=="object_portrait_picture");
        var headline=doc.Descendants("widget").FirstOrDefault(e=>(string?)e.Attribute("id")=="nwhd");
        if(headline==null)throw new IOException("收件箱结构不兼容，未安装 UID 显示。");
        var viewer=doc.Descendants("container").FirstOrDefault(e=>(string?)e.Attribute("class")=="client_object_viewer_xml_panel"&&(string?)e.Attribute("id")=="snpc")??portrait?.Ancestors("container").FirstOrDefault(e=>e.Attribute("navigation_container")!=null);
        if(viewer==null)throw new IOException("未找到收件箱发件人容器。");
        viewer.SetAttributeValue("class","client_object_viewer_xml_panel");viewer.SetAttributeValue("id","snpc");
        if(portrait!=null){portrait.Attribute("id")!.Remove();portrait.Add(new XElement("record",new XAttribute("id","object_property"),new XAttribute("get_property","objt"),new XAttribute("set_property","objt")));}
        var job=doc.Descendants("widget").FirstOrDefault(e=>(string?)e.Attribute("id")=="sjob")??throw new IOException("未找到发件人职位栏，未修改皮肤。");
        job.AddAfterSelf(new XElement("container",new XAttribute("id","FU06"),new XAttribute("width","160"),new XAttribute("height","18"),
            new XElement("layout",new XAttribute("class","arrange_horizontal_attachment"),new XAttribute("alignment","left"),new XAttribute("gap","1")),
            new XElement("layout",new XAttribute("class","stick_to_sides_attachment"),new XAttribute("alignment","vertical"),new XAttribute("apply_to_children","true"),new XAttribute("inset","0")),
            new XElement("widget",new XAttribute("class","label"),new XAttribute("text","UID"),new XAttribute("width","22"),new XAttribute("height","18"),new XAttribute("size","small"),new XAttribute("colour","faded text")),
            new XElement("widget",new XAttribute("class","edit_box"),new XAttribute("width","136"),new XAttribute("height","18"),new XAttribute("size","small"),new XAttribute("colour","faded text"),new XAttribute("appearance",""),new XAttribute("select_on_focus","true"),new XAttribute("show_selection_when_not_focussed","false"),new XAttribute("highlighted_text_colour","inbox_text"),new XAttribute("navigation_focus_target","false"),new XAttribute("wants_mouse_events","true"),new XElement("record",new XAttribute("id","object_property"),new XAttribute("get_property","id  "),new XAttribute("set_property","valu")))));
        writer.Commit(new(){new(file,before,doc.ToString())},"收件箱 UID 显示");
    }
}
