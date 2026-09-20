using System.Diagnostics;

namespace DitaStudio.Core.Diff;

/// <summary>Общий раннер внешнего VCS-клиента (git, svn) для <see cref="GitHistory"/> и
/// <see cref="SvnHistory"/> — запустить, вернуть stdout, null при ошибке или отсутствии клиента.</summary>
internal static class VcsProcess
{
    public static string? Run(string executable, string workingDirectory, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in arguments)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
