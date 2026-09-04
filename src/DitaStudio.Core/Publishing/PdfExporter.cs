using System.Diagnostics;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Экспорт в PDF через печать HTML браузером на движке Chromium (Edge или Chrome),
/// который есть в системе. Внешние библиотеки не требуются.
/// </summary>
public static class PdfExporter
{
    private static readonly string[] CandidatePaths =
    {
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    };

    public static string? FindBrowser()
    {
        foreach (var path in CandidatePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static bool IsAvailable => FindBrowser() is not null;

    /// <summary>
    /// Печатает HTML-файл в PDF. Возвращает null при успехе или текст ошибки.
    /// </summary>
    public static string? ExportToPdf(string htmlPath, string pdfPath, int timeoutSeconds = 120)
    {
        var browser = FindBrowser();
        if (browser is null)
        {
            return "Не найден браузер на движке Chromium (Microsoft Edge или Google Chrome). " +
                   "Откройте собранный HTML и напечатайте его в PDF вручную.";
        }

        var profileDir = Path.Combine(Path.GetTempPath(), "DitaStudioPrint");
        Directory.CreateDirectory(profileDir);

        var arguments = string.Join(' ',
            "--headless=new",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            "--run-all-compositor-stages-before-draw",
            "--virtual-time-budget=10000",
            $"--user-data-dir=\"{profileDir}\"",
            "--print-to-pdf-no-header",
            $"--print-to-pdf=\"{pdfPath}\"",
            $"\"{new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri}\"");

        try
        {
            var info = new ProcessStartInfo(browser, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                return "Не удалось запустить браузер для печати.";
            }

            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // процесс уже завершился
                }

                return "Печать в PDF заняла слишком много времени и была прервана.";
            }

            if (!File.Exists(pdfPath))
            {
                var error = process.StandardError.ReadToEnd();
                return string.IsNullOrWhiteSpace(error)
                    ? "Браузер завершился, но PDF не создан."
                    : $"Браузер сообщил об ошибке: {error.Trim()}";
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
