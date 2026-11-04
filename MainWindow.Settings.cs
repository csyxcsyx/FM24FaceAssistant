using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FM24FaceAssistant;

public partial class MainWindow
{
    void ChooseDocuments(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog{Title="选择 FM24 用户资料目录"};if(dialog.ShowDialog()==true)DocsPath.Text=dialog.FolderName;}
    void ChooseLibraryDownload(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog{Title="选择图库下载目录"};if(dialog.ShowDialog()==true)LibraryDownloadPath.Text=dialog.FolderName;}
    void ResetLibraryDownload(object sender,RoutedEventArgs e)=>LibraryDownloadPath.Text="";
    async void ChooseSkin(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog{Title="选择 FLAYUS 皮肤目录"};if(dialog.ShowDialog()!=true)return;SkinPath.Text=dialog.FolderName;settings.Skin=dialog.FolderName;Persist();await Run(async ct=>{await SyncSkinPreference(ct);Report(SkinEntryState.Text);});}
    void Persist() { Files.Save(Path.Combine(Files.State, "settings.json"), settings); }
    async void SaveSettings(object sender,RoutedEventArgs e)
    {
        if(cancel!=null){Report("请等待当前任务结束，再保存设置。");return;}
        try{Integration.ParseKey(HotkeyBox.Text);Integration.ParseKey(RefreshBox.Text);var documents=Path.GetFullPath(DocsPath.Text);if(!documents.Equals(settings.Documents,StringComparison.OrdinalIgnoreCase))InvalidateBatch();var selectedLibraryPath=string.IsNullOrWhiteSpace(LibraryDownloadPath.Text)?"":Path.GetFullPath(LibraryDownloadPath.Text.Trim());settings.Documents=documents;settings.Pack=PackPath.Text;settings.LibraryDownloadPath=selectedLibraryPath;settings.Hotkey=HotkeyBox.Text;settings.RefreshKey=RefreshBox.Text;settings.Skin=SkinPath.Text;settings.ShowSkinEntry=ShowSkinEntry.IsChecked==true;SetHotkey();Persist();await Run(async ct=>{await SyncSkinPreference(ct);Report("设置已保存。");});}catch(Exception ex){Report(ex.Message);}
    }
    void RefreshProfileChoices(){var names=storage.Profiles();if(!names.Contains(settings.Profile))names.Add(settings.Profile);ProfileChoice.ItemsSource=names;ProfileChoice.SelectedItem=settings.Profile;ProfileName.Text="";ProfileState.Text="当前使用："+settings.Profile;LoadSpecialPortraits();}
    async void SwitchProfile(object sender,RoutedEventArgs e){if(ProfileChoice.SelectedItem is not string name||name==settings.Profile){Report("已在使用此记录。");return;}await SwitchRecord(name);}
    async Task SwitchRecord(string name){await Run(async ct=>{await Sta(()=>writer.Deactivate(Path.Combine(settings.Documents,"graphics")));BatchView.AutomaticCheck.IsChecked=false;settings.Profile=name;Persist();LoadProfile();RefreshProfileChoices();Connection.Text=name+" · 已保存名单 "+people.Count.ToString("N0")+" 人";Report("已切换到“"+name+"”。加载对应游戏存档后，点击扫描缺图即可恢复这套头像。");});}
    async void CreateProfile(object sender,RoutedEventArgs e){var name=ProfileName.Text.Trim();if(name.Length==0||name.Length>60){Report("请输入 1–60 字的记录名称，例如俱乐部和开档年份。");return;}if(storage.Profiles().Contains(name)){Report("此记录已存在，请在列表中选择。");return;}storage.SavePeople(name,new());await SwitchRecord(name);}
    async void DeleteProfile(object sender,RoutedEventArgs e){if(ProfileChoice.SelectedItem is not string name)return;if(name==settings.Profile){Report("不能删除正在使用的记录，请先切换到另一份记录。");return;}if(MessageBox.Show(this,"删除“"+name+"”的名单和分配记录？会先保存备份，不删除图库或游戏存档。","删除存档头像记录",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;await Run(async ct=>{await Task.Run(()=>{var dir=Path.Combine(Files.State,"deleted-records",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));Directory.CreateDirectory(dir);Files.Save(Path.Combine(dir,"record.json"),new{Name=name,People=storage.People(name),Assignments=storage.Load(name)});var path=Path.Combine(Files.State,"rows-"+Files.Hash(name)+".json");if(File.Exists(path))File.Move(path,Path.Combine(dir,"rows.json"));try{storage.DeleteProfile(name);}catch{if(File.Exists(Path.Combine(dir,"rows.json")))File.Move(Path.Combine(dir,"rows.json"),path);throw;}},ct);RefreshProfileChoices();Report("记录已删除，恢复备份已保留。");});}
    void ImportPeople(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "人物名单 JSON|*.json" }; if (d.ShowDialog() != true) return; try { var rows = Files.Read<List<Person>>(d.FileName)?.Select(p=>p.Kind=="球员"?p with{Gender="男"}:p).ToList() ?? throw new IOException("名单为空。"); if (rows.Any(p => p.Id <= 0 || string.IsNullOrWhiteSpace(p.Name) || !new[] { "球员", "职员" }.Contains(p.Kind) || !new[] { "男", "女", "未知" }.Contains(p.Gender)) || rows.Select(p => p.Id).Distinct().Count() != rows.Count) throw new IOException("名单包含无效或重复人物，未导入。"); people = rows; storage.SavePeople(settings.Profile, people); InvalidateBatch(); UpdatePeople(); Report($"已导入 {people.Count} 人。"); } catch (Exception ex) { Report(ex.Message); } }
    void ExportPeople(object sender, RoutedEventArgs e) { var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = "人物资料.json" }; if (d.ShowDialog() == true) { try { Files.Save(d.FileName, people); Report("人物资料已导出。"); } catch (Exception ex) { Report(ex.Message); } } }
    void Safe(Action action) { try { action(); Report("已完成。"); } catch (Exception e) { LogError(e);Report("操作失败："+e.Message); } }
    async void RefreshGame(object s,RoutedEventArgs e){try{await Integration.RefreshAsync(settings.RefreshKey);Report("刷新指令已发送，请检查游戏中的头像。");}catch(Exception ex){Report(ex.Message);}}
    void OpenPackSite(object s, RoutedEventArgs e) => Safe(() => Process.Start(new ProcessStartInfo("https://ngregens.org/fm26savereader_guide") { UseShellExecute = true })); void OpenState(object s, RoutedEventArgs e) => Safe(() => Process.Start(new ProcessStartInfo(Files.State) { UseShellExecute = true })); void HideTray(object s, RoutedEventArgs e) => Hide(); void ExitApp(object s, RoutedEventArgs e) { Close(); }
    void CancelTask(object s, RoutedEventArgs e) { if(committing){Report("正在保存配置，请等待完成。");return;}TaskCancel.IsEnabled=false; Report("正在取消，请稍候…");cancel?.Cancel(); }

    async void RebuildIndex(object sender,RoutedEventArgs e)=>await Run(ct=>ScanMappings(ct,true));
    void DiagnoseRefresh(object sender,RoutedEventArgs e){try{var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Sports Interactive","Football Manager 2024","Preferences","version 1","always_reload_skin.xml");var enabled=File.Exists(path)&&System.Xml.Linq.XDocument.Load(path).Descendants("boolean").Any(x=>(string?)x.Attribute("value")=="true");Report($"重载偏好：{(enabled?"已开启":"请在游戏偏好设置开启确认时重载皮肤")}；刷新键 {settings.RefreshKey}。若照片未变化，请在游戏中关闭图片缓存后重载皮肤。");}catch(Exception ex){Report(ex.Message);}}
    void ViewDownloadProgress(object sender,RoutedEventArgs e)=>UpdateDownloadStatus();
    void UpdateDownloadStatus()
    {
        ResourceSummary.Text=$"当前名单 {people.Count:N0} 人\n可用头像 {faces.Count:N0} 张\n存档记录：{settings.Profile}";
        try
        {
            var root=LibraryPaths.FindProgramRoot();
            if(root==null){LibraryDownloadStatus.Text="此程序目录下没有下载任务。";return;}
            var path=Path.Combine(LibraryPaths.DownloadDirectory(root,Files.State,settings.LibraryDownloadPath),"download-status.json");
            if(!File.Exists(path)){LibraryDownloadStatus.Text="尚未开始下载。可运行程序目录中的继续下载脚本。";return;}
            using var doc=System.Text.Json.JsonDocument.Parse(Files.ReadTextShared(path));var data=doc.RootElement;
            long Number(string key)=>data.TryGetProperty(key,out var value)&&value.TryGetInt64(out long number)?number:0;
            var stage=data.GetProperty("stage").GetString();
            if(stage=="complete"){LibraryDownloadStatus.Text=$"图库已就绪";return;}
            if(stage=="cancelled"){LibraryDownloadStatus.Text="已保存进度，点击下载 / 接续图库可继续。";return;}
            if(stage=="failed"){LibraryDownloadStatus.Text="下载未完成："+data.GetProperty("error").GetString();return;}
            if(stage=="indexing"){LibraryDownloadStatus.Text=$"校验并建立索引：{Number("processed"):N0} / {Number("total_images"):N0} 张";return;}
            bool running=libraryDownload.IsRunning&&data.TryGetProperty("task_id",out var id)&&id.GetString()==libraryDownload.TaskId;
            LibraryDownloadStatus.Text=$"{(running?"正在下载":"下载进程已停止，进度仍保留")}：{Number("downloaded")/1_000_000_000d:F2} / {Number("total")/1_000_000_000d:F2} GB · {Number("bytes_per_second")/1_000_000d:F2} MB/s";
        }
        catch(Exception ex){LibraryDownloadStatus.Text="读取下载状态失败："+ex.Message;}
    }
}
