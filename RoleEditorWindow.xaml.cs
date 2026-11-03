using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
namespace FM24FaceAssistant;
public partial class RoleEditorWindow:Window
{
    readonly Person? original;readonly List<Person> existing;readonly Func<Person,Task<bool>> save;bool saving;
    public RoleEditorWindow(Person? person,IEnumerable<string> nations,List<Person> roles,Func<Person,Task<bool>> save)
    {
        original=person;existing=roles;this.save=save;InitializeComponent();
        Title=Heading.Text=person==null?"新建角色":"编辑角色";SaveButton.Content=person==null?"创建角色":"保存修改";
        Role.SetOptions(new[]{"主教练（我）","新闻官","个人助理","经纪人","球员联络员","其他角色"}.Select(x=>new PickerOption(x,x)));
        Nation.SetOptions(nations.Select(PickerOption.Nation));NameInput.Text=person?.Name??"";UidInput.Text=person?.Key??"";UidInput.IsReadOnly=person!=null;Role.Text=person?.Club??"个人助理";Nation.Text=person?.Nation??"";
        SourceInitialized+=(_,_)=>WindowTheme.Apply(new WindowInteropHelper(this).Handle);Loaded+=(_,_)=>NameInput.Focus();
    }
    async void SaveEdit(object sender,RoutedEventArgs e)
    {
        if(saving)return;
        try{
            var text=UidInput.Text.Trim();bool regen=text.StartsWith("r-",StringComparison.OrdinalIgnoreCase);if(regen)text=text[2..];
            if(!long.TryParse(text,out var id))throw new IOException("请填写有效的游戏 UID。");
            var person=new Person(id,NameInput.Text.Trim(),"职员",original?.Gender??"未知",Nation.SelectedValue??CountryNames.English(Nation.Text),Role.Text.Trim(),"special",regen?true:original?.Newgen);
            SpecialRoleStore.Upsert(existing,person,original?.Id);
            saving=true;SaveButton.IsEnabled=false;Error.Text="正在保存…";
            if(await save(person)){saving=false;DialogResult=true;}
            else Error.Text="未能保存。请检查程序提示或日志后重试。";
        }catch(Exception ex){Error.Text=ex.Message;}
        finally{saving=false;SaveButton.IsEnabled=true;}
    }
    void CancelEdit(object sender,RoutedEventArgs e){if(!saving)Close();}
    void EditorKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&!saving){e.Handled=true;Close();}}
    void EditorClosing(object? sender,CancelEventArgs e){if(saving)e.Cancel=true;}
}
