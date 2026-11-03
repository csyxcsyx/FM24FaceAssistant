using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FM24FaceAssistant;

public interface IGameReader { List<Person> Read(CancellationToken token); }
public sealed class GameReader : IGameReader
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    IProgress<(int Done,int Total)>? progress;
    public List<Person> ReadProgress(CancellationToken ct,IProgress<(int Done,int Total)> progress){this.progress=progress;return Read(ct);}
    public List<Person> Read(CancellationToken token) => ReadImpl(token, false);
    public List<Person> ReadIdentities(CancellationToken token) => ReadImpl(token, true);
    public List<Person> ReadIncremental(CancellationToken token,List<Person> known)=>ReadImpl(token,false,known.ToDictionary(p=>p.Id));
    List<Person> ReadImpl(CancellationToken token, bool identitiesOnly,Dictionary<long,Person>? known=null)
    {
        var proc = Process.GetProcessesByName("fm").FirstOrDefault(p => p.MainModule?.FileVersionInfo.FileVersion == "24.4.2") ?? throw new IOException("未找到已运行的 FM24 24.4.2。请自行启动并加载存档；工具不会启动游戏。");
        using (proc)
        {
            var handle = OpenProcess(0x10 | 0x1000, false, proc.Id); if (handle == IntPtr.Zero) throw new IOException("无法只读访问 FM 进程，请确认两者权限一致。");
            try
            {
                long basis = proc.MainModule!.BaseAddress.ToInt64();
                byte[] Bytes(long address, int count) { if (address < 0x10000 || count < 0 || count > 32_000_000) throw new IOException("无效的游戏内存范围。"); var b = new byte[count]; if (!ReadProcessMemory(handle, (IntPtr)address, b, (nuint)count, out var n) || n != (nuint)count) throw new IOException("游戏数据正在变化或读取布局不兼容，请返回稳定界面后重试。"); return b; }
                long Ptr(long a) => BitConverter.ToInt64(Bytes(a, 8));
                string Str(long a) { if (a < 0x10000) return ""; try { int n = BitConverter.ToInt32(Bytes(a, 4)); return n > 0 && n <= 1024 ? new UTF8Encoding(false, true).GetString(Bytes(a + 4, n)) : ""; } catch { return ""; } }
                string Sub(long a) { try { return Str(Ptr(a)); } catch { return ""; } }
                var people = new List<Person>(); var addresses=new Dictionary<long,PersonAddress>();
                foreach (var (rva, kind, size, uid, name, dob, nation) in new[] { (0x642EDD0L, "球员", 0x3E8, 0x284, 0x2C0, 0x2BC, 0x2E8), (0x642EDE8L, "职员", 0x268, 0x104, 0x140, 0x13C, 0x168) })
                {
                    var start = Ptr(basis + rva); var end = Ptr(basis + rva + 8); long count = (end - start) / 8;
                    if (end < start || (end - start) % 8 != 0 || count < 1 || count > 1_000_000) throw new IOException("人物列表尚未加载或此构建不兼容；未生成补丁。");
                    var pointers = Bytes(start, (int)count * 8); int valid = 0;
                    for (int i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested(); if(i%500==0)progress?.Report((i,(int)count)); long address = BitConverter.ToInt64(pointers, i * 8); if (address < 0x10000) continue;
                        byte[] data; try { data = Bytes(address, size); } catch { continue; }
                        long id = BitConverter.ToUInt32(data, uid); var day = BitConverter.ToUInt16(data, dob); var year = BitConverter.ToUInt16(data, dob + 2); if (id == 0 || year < 1850 || year > 2300 || day < 1 || day > 366) continue;
                        if (identitiesOnly) { people.Add(new(id, "", kind, kind=="球员"?"男":"未知", "", "", $"{year}:{day}", null)); valid++; continue; }
                        if(known!=null&&known.TryGetValue(id,out var previous)&&previous.Dob==$"{year}:{day}"){addresses[id]=new(address,kind,previous.Dob);people.Add(previous);valid++;continue;}
                        string label = Str(BitConverter.ToInt64(data, name)); if (label.Length == 0) label = (Sub(BitConverter.ToInt64(data, name + 16)) + " " + Sub(BitConverter.ToInt64(data, name + 24))).Trim(); if (label.Length == 0) continue;
                        string nat = "未知"; try { var np = BitConverter.ToInt64(data, nation); nat = Str(Ptr(np + 0x18)); if (nat.Length == 0) nat = "未知"; } catch { }
                        addresses[id]=new(address,kind,$"{year}:{day}"); people.Add(new(id, label, kind, kind=="球员"?"男":"未知", nat, "未读取", $"{year}:{day}", null)); valid++;
                    }
                    if (valid < count * 0.75) throw new IOException("有效人物比例不足，读取布局可能不兼容；已停止扫描，未输出补丁。");
                    if (start != Ptr(basis + rva) || end != Ptr(basis + rva + 8)) throw new IOException("扫描期间存档发生变化，请稍后重试。");
                }
                if(!identitiesOnly)SessionGuard.Save(new(proc.Id,proc.StartTime.ToUniversalTime().Ticks,Ptr(basis+0x642EDD0),Ptr(basis+0x642EDD8),Ptr(basis+0x642EDE8),Ptr(basis+0x642EDF0),addresses));
                return people.DistinctBy(p => p.Id).ToList();
            }
            finally { CloseHandle(handle); }
        }
    }
}
