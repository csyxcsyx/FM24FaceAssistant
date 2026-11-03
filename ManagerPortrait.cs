using System;
using System.IO;
namespace FM24FaceAssistant;

public static class ManagerPortrait
{
    public static bool IsSelf(Person person)=>person.Dob=="special"&&SpecialRoleStore.RoleName(person.Club)=="主教练（我）";
    public static string Export(string png,long id,string? pictures=null,string? profile=null)
    {
        if(id<=0||id>uint.MaxValue)throw new IOException("主教练 UID 无效。");
        var folder=Path.Combine(pictures??Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"FM24FaceAssistant");
        Directory.CreateDirectory(folder);
        var prefix=profile==null?"":Files.Hash(profile)[..8]+"-";
        var path=Path.Combine(folder,$"{prefix}r-{id}.png");
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.Copy(png,temp);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        return path;
    }
}
