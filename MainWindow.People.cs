using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FM24FaceAssistant;

public partial class MainWindow
{
    List<Person>? nationOptionSource; List<Face>? groupOptionSource;
    void UpdateFilterChoices()
    {
        if(NationFilter!=null&&!ReferenceEquals(nationOptionSource,people)){nationOptionSource=people;NationFilter.SetOptions(new[]{new PickerOption("","All nations")}.Concat(people.Select(p=>p.Nation).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n=>n).Select(PickerOption.Nation)));}
        if(GroupFilter!=null&&!ReferenceEquals(groupOptionSource,faces)){groupOptionSource=faces;GroupFilter.SetOptions(new[]{new PickerOption("","All categories")}.Concat(faces.Select(f=>f.Group).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g=>g).Select(PickerOption.Group)));}
    }
    void UpdatePeople() { if (PeopleList == null) return; UpdateFilterChoices(); var query = Search?.Text?.Trim() ?? ""; var nation = NationFilter?.Text?.Trim() ?? ""; var club = ClubFilter?.Text?.Trim() ?? ""; var kind = (KindFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString(); var selected = (PeopleList.SelectedItem as Person)?.Id; var visible = people.Where(p => PersonSearch.Matches(p, query) && (nation.Length == 0 || (NationFilter?.SelectedValue is string exactNation ? p.Nation.Equals(exactNation,StringComparison.OrdinalIgnoreCase) : CountryNames.Matches(p.Nation,nation))) && (club.Length == 0 || p.Club.Contains(club, StringComparison.OrdinalIgnoreCase)) && (kind == null || kind == "全部人物" || p.Kind == kind)).ToList(); PeopleList.ItemsSource = visible; PersonCount.Text = $"{visible.Count:N0} / {people.Count:N0} 人"; PeopleList.SelectedItem = visible.FirstOrDefault(p => p.Id == (selected ?? settings.LastPersonId)) ?? visible.FirstOrDefault(); }
    readonly DispatcherTimer searchTimer=new(){Interval=TimeSpan.FromMilliseconds(100)}; bool searchHooked;
    void FilterChanged(object sender,RoutedEventArgs e){if(!ready)return;if(!searchHooked){searchTimer.Tick+=(_,_)=>{searchTimer.Stop();UpdatePeople();};searchHooked=true;}searchTimer.Stop();searchTimer.Start();}
    int portraitVersion; CancellationTokenSource? portraitCancel;
    List<Assignment>? usageSource;Dictionary<string,int> usageCache=new();
    Dictionary<string,int> PortraitUsage(){if(!ReferenceEquals(usageSource,assigned)){usageSource=assigned;usageCache=assigned.GroupBy(a=>a.Face.UniqueKey).ToDictionary(g=>g.Key,g=>g.Count());}return usageCache;}
    async void PersonSelected(object sender, SelectionChangedEventArgs e)
    {
        var selectionVersion=++portraitVersion;
        portraitCancel?.Cancel();portraitCancel?.Dispose();portraitCancel=new();
        var selectionToken=portraitCancel.Token;
        if(PeopleList.SelectedItem is not Person p){
            pending=null;recommended=null;PendingImage.Source=CurrentImage.Source=null;
            PersonTitle.Text="没有匹配人物";PersonInfo.ItemsSource=MatchInfo.ItemsSource=null;
            GalleryRegion.Text="";GenderHelp.Visibility=Visibility.Collapsed;
            Gallery.ItemsSource=Array.Empty<Face>();PageLabel.Text="0 / 0";return;
        }
        settings.LastPersonId=p.Id;recommended=null;seenPreviews.Clear();pending=null;
        PendingImage.Source=null;PersonTitle.Text=p.Name;
        var gender=NameGender.Resolve(p);
        var genderLabel=gender.Gender+(gender.Source=="姓名推断"?"（推断）":gender.Source=="默认男性"?"（默认）":"");
        var keys=PersonSearch.PreviewKeys(p,Search.Text);var key=keys[0];
        PersonInfo.ItemsSource=new[]{"ID "+key,p.Kind,genderLabel,CountryNames.English(p.Nation)};
        GenderHelp.Visibility=p.Kind=="职员"?Visibility.Visible:Visibility.Collapsed;
        GenderHelp.ToolTip=$"头像匹配：{gender.Gender}（{gender.Source}）\n{gender.Detail}";
        var a=assigned.FirstOrDefault(x=>x.Person.Identity==p.Identity&&x.OutputKeys.Contains(key));
        // Resolve the primary ID before considering the compatibility r-portrait.
        // A missing/stale index is not evidence that the primary portrait is absent.
        CurrentImage.Source=Bitmap(a?.Face.Path);
        MatchInfo.ItemsSource=new[]{(a?.Reason??"选择候选头像后应用").Replace(" · ","；")};
        LockFace.IsChecked=a?.Locked??true;page=0;UpdateGallery();
        if(a==null){
            try{
                var external=await QueryMappings(keys.Select(k=>$"graphics/pictures/person/{k}/portrait"),selectionToken,true);
                if(selectionVersion==portraitVersion&&(PeopleList.SelectedItem as Person)?.Identity==p.Identity){
                    var resolved=keys.Select(k=>(Key:k,Path:PersonSearch.Portrait(p,new[]{k},assigned,external))).FirstOrDefault(x=>x.Path!=null);
                    CurrentImage.Source=Bitmap(resolved.Path);
                    if(resolved.Path!=null&&resolved.Key!=key){
                        PersonInfo.ItemsSource=new[]{"ID "+resolved.Key,p.Kind,genderLabel,CountryNames.English(p.Nation)};
                        if(pending==null){
                            var fallback=assigned.FirstOrDefault(x=>x.Person.Identity==p.Identity&&x.OutputKeys.Contains(resolved.Key));
                            MatchInfo.ItemsSource=new[]{(fallback?.Reason??"选择候选头像后应用").Replace(" · ","；")};
                            LockFace.IsChecked=fallback?.Locked??true;
                        }
                    }
                }
            }catch(OperationCanceledException){}
            catch(Exception ex){if(selectionVersion==portraitVersion)Report("当前头像读取失败："+ex.Message);}
        }
    }
    static BitmapImage? Bitmap(string? path)=>PortraitImageCache.Load(path);
    List<Face>? recommended; const int pageSize=14;
    List<Face> Candidates() {var p=PeopleList.SelectedItem as Person;if(p==null)return new();if(recommended==null){recommended=(AllFaces.IsChecked==true?faces.Where(f=>f.Kind==p.Kind&&(p.Kind=="职员"||f.Gender=="男")).DistinctBy(f=>f.UniqueKey).ToList():RegionResolver.Available(p,faces,PortraitUsage()));}var q=GroupFilter.Text.Trim();return q.Length==0?recommended:recommended.Where(f=>GroupFilter.SelectedValue is string exactGroup?f.Group.Equals(exactGroup,StringComparison.OrdinalIgnoreCase):PickerOption.GroupMatches(f.Group,q)).ToList();}
    void ScopeChanged(object sender,RoutedEventArgs e){recommended=null;page=0;if(ready)UpdateGallery();}
    void UpdateGallery() { if (Gallery == null) return; UpdateFilterChoices(); var list = Candidates(); int pages = Math.Max(1, (list.Count + pageSize-1) / pageSize); page = Math.Clamp(page, 0, pages - 1); var visible=list.Skip(page*pageSize).Take(pageSize).ToList(); var regions=visible.Select(f=>f.Group).Distinct().Take(2).ToArray();Gallery.Tag=regions.Length>1;GalleryRegion.Text=regions.Length==1?regions[0]:"";GalleryRegion.ToolTip=GalleryRegion.Text; Gallery.ItemsSource=visible; PageLabel.Text = $"{page + 1} / {pages}    共 {list.Count:N0} 张"; }
    void GalleryChanged(object sender, RoutedEventArgs e) { if (ready) { page = 0; UpdateGallery(); } }
    void PreviousPage(object sender, RoutedEventArgs e) { page--; UpdateGallery(); }
    void NextPage(object sender, RoutedEventArgs e) { page++; UpdateGallery(); }
    void FaceSelected(object sender, SelectionChangedEventArgs e) { if (Gallery.SelectedItem is Face f) SelectFace(f); }
    void SelectFace(Face f) { pending = f; PendingImage.Source = Bitmap(f.Path); var used=PortraitUsage().GetValueOrDefault(f.UniqueKey);MatchInfo.ItemsSource=new[]{f.Group,f.Kind,f.Gender,$"已用于 {used} 人"}; }
    readonly HashSet<string> seenPreviews=new();
    void Reroll(object sender,RoutedEventArgs e){if(PeopleList.SelectedItem is not Person person)return;var usage=PortraitUsage();var list=RegionResolver.Available(person,faces,usage);if(list.Count==0){Report("没有对应地区素材，可勾选全部素材手动选择。");return;}var least=list.Min(f=>usage.GetValueOrDefault(f.UniqueKey));var eligible=list.Where(f=>usage.GetValueOrDefault(f.UniqueKey)==least).ToArray();var choices=eligible.Where(f=>!seenPreviews.Contains(f.UniqueKey)&&f.UniqueKey!=pending?.UniqueKey).ToArray();if(choices.Length==0){seenPreviews.Clear();choices=eligible.Where(f=>f.UniqueKey!=pending?.UniqueKey).ToArray();}if(choices.Length==0)choices=eligible;var chosen=choices[Random.Shared.Next(choices.Length)];seenPreviews.Add(chosen.UniqueKey);SelectFace(chosen);}
    void ImportImage(object sender, RoutedEventArgs e) { if (PeopleList.SelectedItem is not Person p) return; var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg" }; if (dialog.ShowDialog() != true) return; try { if (Bitmap(dialog.FileName) == null) throw new IOException("无法解码图片。"); var dir = Path.Combine(Files.State, "imports"); Directory.CreateDirectory(dir); var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dialog.FileName))); var dest = Path.Combine(dir, hash + Path.GetExtension(dialog.FileName)); File.Copy(dialog.FileName, dest, true); SelectFace(new(dest, p.Group ?? "自定义", p.Kind, p.MatchGender, hash, Catalog.PixelDigest(dest))); } catch (Exception ex) { Report(ex.Message); } }
}
