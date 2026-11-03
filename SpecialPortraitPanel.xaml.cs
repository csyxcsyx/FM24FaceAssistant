using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace FM24FaceAssistant;

public sealed class SpecialPortraitEventArgs(Assignment assignment,bool refresh,bool restore=false):EventArgs
{
    public Assignment Assignment{get;}=assignment;
    public bool Refresh{get;}=refresh;
    public bool Restore{get;}=restore;
}
public record SpecialEntry(Person Person){public string[] Tags=>new[]{Person.Club,"ID "+Person.Key};public override string ToString()=>Person.Name+"\n"+Person.Club;}
public record SpecialRoleChange(Person Person,long? Editing,bool Delete=false);
public partial class SpecialPortraitPanel:UserControl
{
    List<Person> roles=new();List<string> nations=new();RoleEditorWindow? editor;
    public event Func<SpecialRoleChange,System.Threading.Tasks.Task<bool>>? RoleChanged;
    List<Face> faces=new(),candidates=new();List<Assignment> saved=new();Dictionary<string,int> usage=new();Face? selected;int page;List<RoleIdentity> identities=new();bool filling;UidGuideWindow? guide;
    public event EventHandler<SpecialPortraitEventArgs>? ApplyRequested;
    public event Action<string>? Message;
    ManagerPortraitGuideWindow? managerGuide;
    public SpecialPortraitPanel(){InitializeComponent();UpdateActions();}
    void UpdateActions(){if(ApplyButton==null)return;var person=(Saved.SelectedItem as SpecialEntry)?.Person;bool valid=person!=null;ApplyButton.IsEnabled=RefreshButton.IsEnabled=valid&&selected!=null;ImportButton.IsEnabled=RecommendButton.IsEnabled=All.IsEnabled=valid;Images.IsHitTestVisible=Images.Focusable=valid;Images.Opacity=valid?1:0.5;RestoreButton.IsEnabled=valid&&saved.Any(a=>a.Person.Id==person!.Id);RandomButton.IsEnabled=valid&&!string.IsNullOrWhiteSpace(person!.Nation);EditButton.IsEnabled=DeleteButton.IsEnabled=valid;ManagerSetupButton.Visibility=person!=null&&ManagerPortrait.IsSelf(person)?Visibility.Visible:Visibility.Collapsed;ManagerSetupButton.IsEnabled=person!=null&&saved.Any(a=>a.Person.Id==person.Id);}
    void RefreshDraft(){if(Saved.SelectedItem is not SpecialEntry){candidates.Clear();Images.ItemsSource=null;Page.Text="0 / 0";return;}Recommend(this,new RoutedEventArgs());UpdateActions();}
    public void Configure(List<Face> library,IEnumerable<Assignment> ordinary,List<Assignment> records,IEnumerable<string> nations,IEnumerable<Person>? knownPeople=null,List<Person>? roleRecords=null)
    {
        faces=library;saved=records;roles=roleRecords??records.Select(a=>a.Person).ToList();usage=ordinary.Concat(records).GroupBy(a=>a.Face.UniqueKey).ToDictionary(g=>g.Key,g=>g.Count());
        var id=(Saved.SelectedItem as SpecialEntry)?.Person.Id;filling=true;Saved.ItemsSource=roles.Select(p=>new SpecialEntry(p)).ToList();filling=false;Saved.SelectedItem=Saved.Items.Cast<SpecialEntry>().FirstOrDefault(x=>x.Person.Id==id);if(Saved.SelectedItem==null)Saved.SelectedItem=Saved.Items.Cast<SpecialEntry>().FirstOrDefault();if(Saved.SelectedItem==null){RoleTitle.Text="选择角色";RoleTags.ItemsSource=null;Current.Source=Preview.Source=null;Images.ItemsSource=null;Page.Text="0 / 0";Region.Text="";ChoiceInfo.ItemsSource=null;}UpdateActions();
        identities=records.Select(a=>new RoleIdentity(a.Person.Id,a.Person.Name,a.Person.Nation,a.Person.Newgen,"已保存角色")).Concat((knownPeople??Array.Empty<Person>()).Where(p=>p.Kind=="职员").Select(p=>new RoleIdentity(p.Id,p.Name,p.Nation,p.Newgen,"游戏名单"))).ToList();
        this.nations=nations.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList();
    }
    public void ShowSaved(long id){Saved.SelectedItem=Saved.Items.Cast<SpecialEntry>().FirstOrDefault(x=>x.Person.Id==id);}
    Person ActiveTarget()=>(Saved.SelectedItem as SpecialEntry)?.Person??throw new IOException("请先选择或创建角色。");
    void SelectSaved(object sender,SelectionChangedEventArgs e){if(filling)return;var p=(Saved.SelectedItem as SpecialEntry)?.Person;selected=null;Preview.Source=null;ChoiceInfo.ItemsSource=null;RoleTitle.Text=p?.Name??"选择角色";RoleTags.ItemsSource=p==null?null:new[]{p.Club,"ID "+p.Key,CountryNames.English(p.Nation)};var row=saved.FirstOrDefault(a=>a.Person.Id==p?.Id);Current.Source=row==null?null:Image(row.Face.Path);RefreshDraft();UpdateActions();}
    void NewEntry(object s,RoutedEventArgs e)=>OpenEditor(null);
    void EditEntry(object s,RoutedEventArgs e){if(Saved.SelectedItem is SpecialEntry item)OpenEditor(item.Person);}
    void OpenEditor(Person? person){if(editor!=null){editor.Activate();return;}editor=new RoleEditorWindow(person,nations,roles,async p=>RoleChanged!=null&&await RoleChanged(new(p,person?.Id))){Owner=Window.GetWindow(this)};try{editor.ShowDialog();}finally{editor=null;}}
    async void DeleteEntry(object s,RoutedEventArgs e){if(Saved.SelectedItem is SpecialEntry item&&RoleChanged!=null)await RoleChanged(new(item.Person,item.Person.Id,true));}
    static BitmapImage? Image(string path)=>PortraitImageCache.Load(path);
    void Select(Face face){selected=face;Preview.Source=Image(face.Path);ChoiceInfo.ItemsSource=new[]{face.Group,face.Gender,"已用于 "+usage.GetValueOrDefault(face.UniqueKey)+" 人"};UpdateActions();}
    void Recommend(object s,RoutedEventArgs e){try{var p=ActiveTarget();candidates=All.IsChecked==true?faces.Where(f=>f.Kind=="职员").DistinctBy(f=>f.UniqueKey).ToList():RegionResolver.Available(p,faces,usage);page=0;ShowPage();}catch(Exception ex){Message?.Invoke(ex.Message);}}
    void ShowPage(){int total=Math.Max(1,(candidates.Count+13)/14);page=Math.Clamp(page,0,total-1);var visible=candidates.Skip(page*14).Take(14).ToList();var regions=visible.Select(f=>f.Group).Distinct().Take(2).ToArray();Images.Tag=regions.Length>1;Region.Text=regions.Length==1?regions[0]:"";Images.ItemsSource=visible;Page.Text=$"{page+1} / {total}";}
    void Previous(object s,RoutedEventArgs e){page--;ShowPage();}void Next(object s,RoutedEventArgs e){page++;ShowPage();}
    void PickFace(object s,SelectionChangedEventArgs e){if(Images.SelectedItem is Face face)Select(face);}
    void Randomize(object s,RoutedEventArgs e){try{var p=ActiveTarget();var pool=RegionResolver.Available(p,faces,usage);if(pool.Count==0)throw new IOException("没有可推荐的职员素材。请选择国籍，或直接导入图片。");var least=pool.Min(f=>usage.GetValueOrDefault(f.UniqueKey));var list=pool.Where(f=>usage.GetValueOrDefault(f.UniqueKey)==least&&f.UniqueKey!=selected?.UniqueKey).ToList();if(list.Count==0)list=pool;Select(list[Random.Shared.Next(list.Count)]);}catch(Exception ex){Message?.Invoke(ex.Message);}}
    void Import(object s,RoutedEventArgs e){try{var p=ActiveTarget();var dialog=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg"};if(dialog.ShowDialog()!=true)return;if(Image(dialog.FileName)==null)throw new IOException("无法读取图片。");var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dialog.FileName)));var dir=Path.Combine(Files.State,"imports");Directory.CreateDirectory(dir);var dest=Path.Combine(dir,hash+Path.GetExtension(dialog.FileName));File.Copy(dialog.FileName,dest,true);Select(new(dest,"Custom","职员",p.MatchGender,hash,Catalog.PixelDigest(dest)));}catch(Exception ex){Message?.Invoke(ex.Message);}}
    void Submit(bool refresh,bool restore=false){try{var p=ActiveTarget();var face=selected??saved.FirstOrDefault(a=>a.Person.Id==p.Id)?.Face;if(face==null)throw new IOException("请先选择或导入头像。");ApplyRequested?.Invoke(this,new(new(p,face,true,p.Club),refresh,restore));}catch(Exception ex){Message?.Invoke(ex.Message);}}
    void Apply(object s,RoutedEventArgs e)=>Submit(false);void ApplyRefresh(object s,RoutedEventArgs e)=>Submit(true);void Restore(object s,RoutedEventArgs e)=>Submit(false,true);
    void OpenUidGuide(object s,RoutedEventArgs e)
    {
        if(guide!=null){if(guide.WindowState==WindowState.Minimized)guide.WindowState=WindowState.Normal;guide.Activate();return;}
        guide=new UidGuideWindow(){Owner=Window.GetWindow(this)};guide.Closed+=(_,_)=>guide=null;guide.Show();
    }
    void OpenManagerSetup(object s,RoutedEventArgs e){try{var person=ActiveTarget();var row=saved.FirstOrDefault(a=>a.Person.Id==person.Id)??throw new IOException("请先应用一张主教练头像。");var settings=Files.Read<Settings>(Path.Combine(Files.State,"settings.json"))??new();var png=Path.Combine(settings.Documents,"graphics","FM24FaceAssistant","special",row.Face.Hash+".png");ShowManagerSetup(ManagerPortrait.Export(png,person.Id,profile:settings.Profile));}catch(Exception ex){Message?.Invoke("主教练头像准备失败："+ex.Message);}}
    public void ShowManagerSetup(string? path){managerGuide?.Close();managerGuide=new ManagerPortraitGuideWindow(path){Owner=Window.GetWindow(this)};managerGuide.Closed+=(_,_)=>managerGuide=null;managerGuide.Show();}

}
