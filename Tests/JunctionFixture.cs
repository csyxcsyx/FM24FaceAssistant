using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// A junction inside the test sandbox needs no symlink privilege.
static class JunctionFixture
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,int inputSize,IntPtr output,int outputSize,out int returned,IntPtr overlapped);
    public static void Create(string path,string target)
    {
        Directory.CreateDirectory(path);
        var substitute=Encoding.Unicode.GetBytes(@"\??\"+Path.GetFullPath(target));var print=Encoding.Unicode.GetBytes(Path.GetFullPath(target));
        var buffer=new byte[16+substitute.Length+2+print.Length+2];
        BitConverter.GetBytes(0xa0000003u).CopyTo(buffer,0);BitConverter.GetBytes((ushort)(buffer.Length-8)).CopyTo(buffer,4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(buffer,10);BitConverter.GetBytes((ushort)(substitute.Length+2)).CopyTo(buffer,12);BitConverter.GetBytes((ushort)print.Length).CopyTo(buffer,14);
        substitute.CopyTo(buffer,16);print.CopyTo(buffer,16+substitute.Length+2);
        using var handle=CreateFile(path,0x40000000,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
        if(handle.IsInvalid||!DeviceIoControl(handle,0x000900a4,buffer,buffer.Length,IntPtr.Zero,0,out _,IntPtr.Zero))throw new IOException("Cannot create test junction: "+Marshal.GetLastWin32Error());
    }
}
