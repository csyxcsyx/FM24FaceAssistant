using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
namespace FM24FaceAssistant;
public partial class MainWindow
{
    List<Assignment> specialRows=new();
    void LoadSpecialPortraits()
    {
        specialRows=Files.Read<List<Assignment>>(Path.Combine(Files.State,"special-rows-"+Files.Hash(settings.Profile)+".json"))??new();
        var roles=SpecialRoleStore.Load(settings.Profile,specialRows);Files.Save(SpecialRoleStore.PathFor(settings.Profile),roles);
        SpecialView.Configure(faces,assigned,specialRows,people.Select(p=>CountryNames.English(p.Nation)),people,roles);
    }
    async Task<bool> ManageSpecialRole(SpecialRoleChange change)
    {
        bool success=false;await Run(async ct=>{
            var roles=SpecialRoleStore.Load(settings.Profile,specialRows);
            if(change.Delete){
                if(specialRows.Any(a=>a.Person.Id==change.Person.Id)){
                    var rows=specialRows.Where(a=>a.Person.Id!=change.Person.Id).ToList();
                    var external=await QueryMappings(rows.SelectMany(a=>a.OutputKeys.SelectMany(k=>new[]{$"graphics/pictures/person/{k}/portrait",$"graphics/pictures/person/{k}/icon"})),ct);
                    await Sta(()=>writer.Apply(Path.Combine(settings.Documents,"graphics"),settings.Profile,rows,external,ct,null,taskProgress,true));
                }
                if(assigned.Any(a=>a.Person.Id==change.Person.Id))await ApplyRows(assigned.Where(a=>a.Person.Id!=change.Person.Id).ToList(),ct);
                roles=roles.Where(p=>p.Id!=change.Person.Id).ToList();
            }else roles=SpecialRoleStore.Upsert(roles,change.Person,change.Editing);
            Files.Save(SpecialRoleStore.PathFor(settings.Profile),roles);LoadSpecialPortraits();
            if(!change.Delete)SpecialView.ShowSaved(change.Person.Id);success=true;
            Report(change.Delete?"角色已删除，助手分配的头像已恢复；刷新游戏后生效。":change.Editing.HasValue?"角色资料已保存。":"角色已创建，现在可以选择头像。");
            if(change.Delete&&ManagerPortrait.IsSelf(change.Person)){SpecialView.ShowManagerSetup(null);Report("角色已删除；请在游戏内取消主教练的个人头像。");}
        });return success;
    }
    async void ApplySpecialPortrait(object? sender,SpecialPortraitEventArgs e)
    {
        var regular=people.FirstOrDefault(p=>p.Id==e.Assignment.Person.Id);
        if(regular!=null){
            if(regular.Kind!="职员"||!RoleIdentityResolver.SameName(regular.Name,e.Assignment.Person.Name)){Report("此 UID 对应名单中的 "+regular.Name+"，请核对角色姓名和编号。");return;}
            await Run(async ct=>{var rows=assigned.Where(a=>a.Person.Id!=regular.Id).ToList();if(!e.Restore)rows.Add(e.Assignment with{Person=regular});await ApplyRows(rows,ct);if(e.Refresh){try{await Integration.RefreshAsync(settings.RefreshKey);Report("角色头像已应用，刷新指令已发送。");}catch(Exception ex){Report("头像已应用；刷新未完成："+ex.Message);}}});return;
        }
        await Run(async ct=>{
            var rows=specialRows.Where(a=>a.Person.Id!=e.Assignment.Person.Id).ToList();if(!e.Restore)rows.Add(e.Assignment);
            var external=await QueryMappings(rows.SelectMany(a=>a.OutputKeys.SelectMany(k=>new[]{$"graphics/pictures/person/{k}/portrait",$"graphics/pictures/person/{k}/icon"})),ct);
            await Sta(()=>writer.Apply(Path.Combine(settings.Documents,"graphics"),settings.Profile,rows,external,ct,null,taskProgress,true));
            LoadSpecialPortraits();SpecialView.ShowSaved(e.Assignment.Person.Id);
            if(ManagerPortrait.IsSelf(e.Assignment.Person)){
                string? path=null;if(!e.Restore){var png=Path.Combine(settings.Documents,"graphics","FM24FaceAssistant","special",e.Assignment.Face.Hash+".png");path=ManagerPortrait.Export(png,e.Assignment.Person.Id,profile:settings.Profile);}
                SpecialView.ShowManagerSetup(path);
                Report(e.Restore?"主教练映射已恢复，请在游戏内取消个人头像。":"主教练图片已准备，请按窗口提示完成游戏内设置。");return;
            }
            if(e.Refresh){try{await Integration.RefreshAsync(settings.RefreshKey);Report("特殊角色头像已应用，刷新指令已发送。");}catch(Exception ex){Report("头像已应用；刷新未完成："+ex.Message);}}
            else Report(e.Restore?"已恢复此角色原来的头像，请刷新游戏。":"特殊角色头像已应用，请刷新游戏查看。");
        });
    }
}
