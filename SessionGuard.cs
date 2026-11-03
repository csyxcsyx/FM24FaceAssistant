using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
namespace FM24FaceAssistant;
public record PersonAddress(long Address,string Kind,string Dob);
public record SessionSnapshot(int Pid,long Started,long PlayerStart,long PlayerEnd,long StaffStart,long StaffEnd,Dictionary<long,PersonAddress> People,string Profile="",string Anchor="");
public static class SessionGuard
{
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr handle,IntPtr address,byte[] buffer,nuint length,out nuint read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static SessionSnapshot? cache;
    public static void Save(SessionSnapshot snapshot){Files.Save(Path.Combine(Files.State,"session.json"),snapshot);cache=snapshot;}
    public static void Bind(string profile){if(cache!=null){var sample=PopulationProbe.Read();Save(cache with{Profile=profile,Anchor=sample?.Pid==cache.Pid?sample.Anchor:""});}}
    public static string AnchorFor(string profile){cache??=Files.Read<SessionSnapshot>(Path.Combine(Files.State,"session.json"));return cache?.Profile==profile?cache.Anchor:"";}
    public static void Verify(IEnumerable<Person> persons,string profile,System.Threading.CancellationToken ct=default,IProgress<TaskProgress>? progress=null)
    {
        ct.ThrowIfCancellationRequested();var targets=persons.ToList();
        using var process=Process.GetProcessesByName("fm").FirstOrDefault();if(process==null)return;
        cache??=Files.Read<SessionSnapshot>(Path.Combine(Files.State,"session.json"));
        if(cache==null||cache.Profile!=profile||cache.Pid!=process.Id||cache.Started!=process.StartTime.ToUniversalTime().Ticks)throw new IOException("请先点击更新名单，连接当前游戏存档；之后可直接复用缓存。");
        var handle=OpenProcess(0x1010,false,process.Id);if(handle==IntPtr.Zero)throw new IOException("无法核对游戏，请检查权限。");
        byte[] Read(long address,int size){var bytes=new byte[size];if(!ReadProcessMemory(handle,(IntPtr)address,bytes,(nuint)size,out var n)||n!=(nuint)size)throw new IOException("游戏数据已变化，请更新名单。");return bytes;}
        try{long basis=process.MainModule!.BaseAddress.ToInt64();var roots=Read(basis+0x642EDD0,40);if(BitConverter.ToInt64(roots,0)!=cache.PlayerStart||BitConverter.ToInt64(roots,8)!=cache.PlayerEnd||BitConverter.ToInt64(roots,24)!=cache.StaffStart||BitConverter.ToInt64(roots,32)!=cache.StaffEnd)throw new IOException("检测到游戏名单变化，请更新名单后再应用。");
            int verified=0;foreach(var p in targets){ct.ThrowIfCancellationRequested();progress?.Report(new("核对人物身份",++verified,targets.Count,p.Name));if(!cache.People.TryGetValue(p.Id,out var entry))throw new IOException("人物不属于当前连接的存档，请更新名单。");var uid=entry.Kind=="球员"?0x284:0x104;var dob=entry.Kind=="球员"?0x2BC:0x13C;var date=Read(entry.Address+dob,4);if(BitConverter.ToUInt32(Read(entry.Address+uid,4))!=p.Id||$"{BitConverter.ToUInt16(date,2)}:{BitConverter.ToUInt16(date,0)}"!=p.Dob)throw new IOException("人物身份已变化，请更新名单。");}
        }finally{CloseHandle(handle);}
    }
}
