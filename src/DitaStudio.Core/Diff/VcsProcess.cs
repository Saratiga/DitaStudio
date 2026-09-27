using System.Diagnostics;
using System.Text;

namespace DitaStudio.Core.Diff;

/// <summary>Общий раннер внешнего VCS-клиента (git, svn) для <see cref="GitHistory"/> и
/// <see cref="SvnHistory"/> — запустить, вернуть stdout, null при ошибке или отсутствии клиента.</summary>
internal static class VcsProcess
{
    /// <summary>Сколько ждать клиента. Вызов идёт из UI-потока — зависший git (сетевой диск,
    /// блокировка индекса) не должен вешать редактор.</summary>
    private const int TimeoutMilliseconds = 15_000;

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
                CreateNoWindow = true,
                // git и svn cat отдают байты файла как есть (у нас это UTF-8), а пути в выводе
                // git — тоже UTF-8. Без явной кодировки выбор зависел бы от консольной кодовой
                // страницы процесса, и кириллица в путях/тексте могла прийти искажённой.
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
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

            // Оба потока читаем одновременно: невычитанный stderr заполняет буфер канала,
            // клиент блокируется на записи, а мы — на чтении stdout, и ждём друг друга вечно.
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                TryKill(process);
                return null;
            }

            return process.ExitCode == 0 && output.Wait(TimeoutMilliseconds) ? output.Result : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // процесс успел завершиться сам
        }
    }
}
