using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FM24FaceAssistant;

public sealed record PythonRuntime(string Executable,string[] Prefix)
{
    public ProcessStartInfo Start(string script,string workingDirectory)
    {
        var info=new ProcessStartInfo(Executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=workingDirectory};
        foreach(var argument in Prefix)info.ArgumentList.Add(argument);
        info.ArgumentList.Add(script);
        return info;
    }

    public static PythonRuntime Find()
    {
        var candidates=new[]{new PythonRuntime("py.exe",new[]{"-3"}),new PythonRuntime("python.exe",Array.Empty<string>())};
        var errors=new System.Collections.Generic.List<string>();
        foreach(var candidate in candidates)
        {
            try
            {
                var info=new ProcessStartInfo(candidate.Executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var argument in candidate.Prefix)info.ArgumentList.Add(argument);
                info.ArgumentList.Add("-c");info.ArgumentList.Add("import PIL; print('Pillow '+PIL.__version__)");
                using var process=Process.Start(info);
                if(process==null){errors.Add(candidate.Executable+" 无法启动");continue;}
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(15000)){process.Kill(true);process.WaitForExit();errors.Add(candidate.Executable+" 检查超时");continue;}
                var detail=error.GetAwaiter().GetResult().Trim();if(process.ExitCode==0)return candidate;
                if(detail.Length==0)detail=output.GetAwaiter().GetResult().Trim();errors.Add(candidate.Executable+"："+detail);
            }
            catch(Exception ex){errors.Add(candidate.Executable+"："+ex.Message);}
        }
        throw new IOException("图库下载需要可运行的 Python 3 和 Pillow。已检查 py -3 与 python；请在可用的 Python 3 中运行 python -m pip install Pillow。"+Environment.NewLine+string.Join(Environment.NewLine,errors.Where(e=>e.Length>0)));
    }
}
