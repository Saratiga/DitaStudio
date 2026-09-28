using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DitaStudio.Core.Diff;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Plugins;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Schema.Dtd;
using DitaStudio.Core.Templates;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// PDF через браузер, построчный diff, git и svn.
internal static partial class CoreChecks
{
    internal static void PdfExporterTests()
    {
        Section("Экспорт в PDF через headless-браузер (по отчёту покрытия)");

        var browser = PdfExporter.FindBrowser();
        Check(PdfExporter.IsAvailable == (browser is not null), "IsAvailable согласован с FindBrowser()");

        if (!OperatingSystem.IsWindows() && browser is null)
        {
            // Linux: браузер из пакета ищется по PATH (google-chrome, chromium…).
            var fakeDir = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fakeDir);
            var fake = Path.Combine(fakeDir, "chromium");
            File.WriteAllText(fake, "#!/bin/sh\n");
            var oldPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", fakeDir + Path.PathSeparator + oldPath);
                Check(PdfExporter.FindBrowser() == fake, "браузер находится по PATH (chromium)");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", oldPath);
                Directory.Delete(fakeDir, true);
            }
        }

        if (browser is null)
        {
            Note("Edge/Chrome не найден по стандартным путям — остальные проверки раздела пропущены");
            return;
        }

        Check(File.Exists(browser), $"FindBrowser вернул существующий исполняемый файл: {browser}");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var htmlPath = Path.Combine(root, "page.html");
            File.WriteAllText(htmlPath, "<html><body><h1>Проверка PdfExporter</h1></body></html>");
            var pdfPath = Path.Combine(root, "out.pdf");

            var error = PdfExporter.ExportToPdf(htmlPath, pdfPath, timeoutSeconds: 60);
            Check(error is null, $"ExportToPdf не сообщил об ошибке: {error}");
            Check(File.Exists(pdfPath), "PDF-файл создан");

            var header = new byte[5];
            using (var stream = File.OpenRead(pdfPath))
            {
                stream.ReadExactly(header);
            }

            Check(System.Text.Encoding.ASCII.GetString(header) == "%PDF-", "созданный файл начинается с настоящей PDF-сигнатуры");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    internal static void DiffTests()
    {
        Section("Сравнение файлов (построчный diff)");

        var same = XmlDiff.Compare("a\nb\nc", "a\nb\nc");
        Check(same.All(d => d.Kind == DiffKind.Equal), "идентичный текст — все строки совпадают");

        var changed = XmlDiff.Compare("a\nb\nc", "a\nX\nc");
        Check(changed.Count(d => d.Kind == DiffKind.Removed) == 1 && changed.Count(d => d.Kind == DiffKind.Added) == 1,
            $"изменённая строка — одно удаление и одно добавление: {string.Join(",", changed.Select(d => d.Kind))}");
        Check(changed[0].Kind == DiffKind.Equal && changed[^1].Kind == DiffKind.Equal,
            "общие строки вокруг изменения остались Equal");

        var addedOnly = XmlDiff.Compare("a\nc", "a\nb\nc");
        Check(addedOnly.Count(d => d.Kind == DiffKind.Added) == 1 && addedOnly.Count(d => d.Kind == DiffKind.Removed) == 0,
            "добавленная строка распознана без ложного удаления");

        var removedOnly = XmlDiff.Compare("a\nb\nc", "a\nc");
        Check(removedOnly.Count(d => d.Kind == DiffKind.Removed) == 1 && removedOnly.Count(d => d.Kind == DiffKind.Added) == 0,
            "удалённая строка распознана без ложного добавления");
    }

    internal static void GitHistoryTests()
    {
        Section("Сравнение с git-историей");

        // Кириллица в пути репозитория: путь из `git rev-parse --show-toplevel` должен
        // прочитаться без искажений, иначе ReadRevision не найдёт файл.
        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-проект");
        Directory.CreateDirectory(root);

        try
        {
            RunExternalOrSkip("git", root, "init");
            if (!Directory.Exists(Path.Combine(root, ".git")))
            {
                Note("git недоступен в окружении — раздел пропущен");
                return;
            }

            RunExternalOrSkip("git", root, "config", "user.email", "test@example.com");
            RunExternalOrSkip("git", root, "config", "user.name", "Test");

            var tracked = Path.Combine(root, "topic.dita");
            File.WriteAllText(tracked, "версия из коммита");
            RunExternalOrSkip("git", root, "add", "topic.dita");
            RunExternalOrSkip("git", root, "commit", "-m", "начальный коммит");

            File.WriteAllText(tracked, "рабочая копия, ещё не закоммичена");

            var headContent = GitHistory.ReadRevision(tracked);
            Check(headContent?.Trim() == "версия из коммита", "ReadRevision вернул содержимое из HEAD, а не рабочей копии");
            Check(GitHistory.IsInRepository(tracked), "файл внутри git-репозитория распознан");

            var untracked = Path.Combine(root, "untracked.dita");
            File.WriteAllText(untracked, "не в истории");
            Check(GitHistory.ReadRevision(untracked) is null, "неотслеживаемый файл — ReadRevision возвращает null");

            var outsideRepo = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + ".dita");
            Check(!GitHistory.IsInRepository(outsideRepo), "файл вне репозитория git не распознан как отслеживаемый");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    internal static void SvnHistoryTests()
    {
        Section("Сравнение с историей SVN");

        var repoPath = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-svn-repo");
        var wcPath = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-svn-wc");

        try
        {
            RunExternalOrSkip("svnadmin", Path.GetTempPath(), "create", repoPath);
            if (!Directory.Exists(Path.Combine(repoPath, "conf")))
            {
                Check(!SvnHistory.IsInRepository(Path.Combine(Path.GetTempPath(), "nonexistent.dita")),
                    "svn недоступен — IsInRepository не падает, возвращает false");
                Note("svn недоступен в окружении — остальные проверки раздела пропущены");
                return;
            }

            Directory.CreateDirectory(wcPath);
            var repoUrl = "file:///" + repoPath.Replace('\\', '/');
            RunExternalOrSkip("svn", wcPath, "checkout", repoUrl, ".");
            if (!Directory.Exists(Path.Combine(wcPath, ".svn")))
            {
                Note("svn checkout не удался — остальные проверки раздела пропущены");
                return;
            }

            var tracked = Path.Combine(wcPath, "topic.dita");
            File.WriteAllText(tracked, "версия из репозитория");
            RunExternalOrSkip("svn", wcPath, "add", "topic.dita");
            // Сообщение коммита — ASCII: svn перекодирует его из системной кодировки, и без
            // UTF-8-локали (Linux с LANG=C, Windows не с кириллической кодовой страницей)
            // кириллица роняет commit, а раздел молча проверял бы файл без истории.
            RunExternalOrSkip("svn", wcPath, "commit", "-m", "initial commit");

            File.WriteAllText(tracked, "рабочая копия, ещё не закоммичена");

            var baseContent = SvnHistory.ReadRevision(tracked);
            Check(baseContent?.Trim() == "версия из репозитория", "ReadRevision вернул содержимое BASE, а не рабочей копии");
            Check(SvnHistory.IsInRepository(tracked), "файл под версионным контролем распознан");

            // «@» в имени svn читает как peg-ревизию — путь нужно передавать с завершающим «@».
            var withAt = Path.Combine(wcPath, "logo@2x.dita");
            File.WriteAllText(withAt, "файл с @ в имени");
            RunExternalOrSkip("svn", wcPath, "add", "logo@2x.dita@");
            RunExternalOrSkip("svn", wcPath, "commit", "-m", "file with at sign");
            File.WriteAllText(withAt, "правка в рабочей копии");
            Check(SvnHistory.ReadRevision(withAt)?.Trim() == "файл с @ в имени",
                "ReadRevision: «@» в имени файла не принимается за peg-ревизию");

            var untracked = Path.Combine(wcPath, "untracked.dita");
            File.WriteAllText(untracked, "не в истории");
            Check(SvnHistory.ReadRevision(untracked) is null, "неотслеживаемый файл — ReadRevision возвращает null");

            var outsideWc = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + ".dita");
            Check(!SvnHistory.IsInRepository(outsideWc), "файл вне рабочей копии svn не распознан как отслеживаемый");
        }
        finally
        {
            try
            {
                Directory.Delete(repoPath, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }

            try
            {
                Directory.Delete(wcPath, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void RunExternalOrSkip(string executable, string workingDirectory, params string[] arguments)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo(executable)
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

            using var process = System.Diagnostics.Process.Start(info);
            process?.WaitForExit(5000);
        }
        catch
        {
            // клиент не установлен — вызывающий тест сам обнаружит отсутствие результата и пропустит раздел
        }
    }
}
