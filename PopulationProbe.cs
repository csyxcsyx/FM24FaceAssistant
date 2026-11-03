using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace FM24FaceAssistant;
public record PopulationSample(int Pid,long Started,int Players,int Staff,string Fingerprint,string Anchor="")
{
    public int Total=>Players+Staff;
}
public sealed class PopulationChangeGate
{
    PopulationSample? baseline; string? pending;
    public bool SessionChanged {get;private set;}
    public void Reset(){baseline=null;pending=null;SessionChanged=false;}
    public bool Observe(PopulationSample? sample,int knownCount)
    {
        if(sample==null){pending=null;return false;}
        if(baseline==null){baseline=sample;if(knownCount>0&&sample.Total>knownCount)pending=sample.Fingerprint;return false;}
        if(sample.Pid!=baseline.Pid||sample.Started!=baseline.Started||(sample.Anchor.Length>0&&baseline.Anchor.Length>0&&sample.Anchor!=baseline.Anchor)){SessionChanged=true;baseline=sample;pending=null;return false;}
        if(sample.Fingerprint==pending){baseline=sample;pending=null;return true;}
        if(sample.Fingerprint!=baseline.Fingerprint)pending=sample.Fingerprint;
        else pending=null;
        return false;
    }
}
public static class PopulationProbe
{
    public static string? LastError {get;private set;}
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr handle,IntPtr address,byte[] buffer,nuint size,out nuint read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static PopulationSample? Read()
    {
        try{
            LastError=null;using var process=Process.GetProcessesByName("fm").FirstOrDefault();if(process==null){LastError="游戏未运行";return null;}if(process.MainModule?.FileVersionInfo.FileVersion!="24.4.2"){LastError="游戏版本不匹配";return null;}
            var handle=OpenProcess(0x1010,false,process.Id);if(handle==IntPtr.Zero){LastError="无法只读访问游戏进程";return null;}
            try{
                byte[] Bytes(long address,int length){var bytes=new byte[length];if(!ReadProcessMemory(handle,(IntPtr)address,bytes,(nuint)length,out var n)||n!=(nuint)length)throw new IOException();return bytes;}
                long basis=process.MainModule.BaseAddress.ToInt64();var header=Bytes(basis+0x642EDD0,40);
                var values=new[]{BitConverter.ToInt64(header,0),BitConverter.ToInt64(header,8),BitConverter.ToInt64(header,24),BitConverter.ToInt64(header,32)};
                int Count(long start,long end){long n=(end-start)/8;if(start<0x10000||end<start||(end-start)%8!=0||n<1||n>1000000)throw new IOException();return (int)n;}
                int players=Count(values[0],values[1]),staff=Count(values[2],values[3]);
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(BitConverter.GetBytes(players));hash.AppendData(BitConverter.GetBytes(staff));
                using var anchor=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for(int i=0;i<4;i+=2){var start=values[i];var end=values[i+1];int length=(int)Math.Min(128,end-start);var head=Bytes(start,length);anchor.AppendData(head);hash.AppendData(head);hash.AppendData(Bytes(end-length,length));}
                if(!header.SequenceEqual(Bytes(basis+0x642EDD0,40)))return null;
                return new(process.Id,process.StartTime.ToUniversalTime().Ticks,players,staff,Convert.ToHexString(hash.GetHashAndReset()),Convert.ToHexString(anchor.GetHashAndReset()));
            }finally{CloseHandle(handle);}
        }catch(Exception ex){LastError=ex.Message;return null;}
    }
}
