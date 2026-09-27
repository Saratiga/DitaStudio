using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;

namespace DitaStudio.Tests;

// Учебные проекты из samples/ — живая документация к редактору: после правки топиков
// (или кода, который их читает) они должны по-прежнему проходить проверку и собираться.
// Раньше это проверялось вручную из редактора.
internal static partial class CoreChecks
{
    internal static void SampleProjectsTests()
    {
        Section("Учебные проекты samples/: проверка и сборка");

        var samples = Path.Combine(RepositoryRoot(), "samples");
        foreach (var name in new[] { "GuideSample", "DitaStudioGuide" })
        {
            CheckSampleProject(Path.Combine(samples, name), name);
        }
    }

    private static void CheckSampleProject(string root, string name)
    {
        var project = new DitaProject(root);
        project.Scan();
        var map = Path.Combine(root, "guide.ditamap");
        Check(File.Exists(map), $"{name}: карта guide.ditamap на месте");

        var issues = project.ValidateAll();
        var errors = issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
        Check(errors.Count == 0, $"{name}: проверка проекта без ошибок" + Describe(errors));
        var warnings = issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();
        Check(warnings.Count == 0, $"{name}: проверка проекта без предупреждений" + Describe(warnings));

        var output = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-" + name);
        try
        {
            var html = new HtmlPublisher(project).Publish(map, new PublishOptions { OutputDirectory = output });
            Check(File.Exists(html.EntryFile), $"{name}: HTML-публикация создала {Path.GetFileName(html.EntryFile)}");
            Check(html.Warnings.Count == 0, $"{name}: HTML-публикация без предупреждений" + Describe(html.Warnings));

            var docxPath = Path.Combine(output, name + ".docx");
            var docx = new DocxPublisher(project).Publish(map, new PublishOptions { OutputDirectory = output }, docxPath);
            // SVG в DOCX не встраивается (показывается ссылкой) — известное ограничение экспорта,
            // а руководство намеренно показывает SVG-схему; остальные предупреждения — провал.
            var docxWarnings = docx.Warnings
                .Where(w => !w.StartsWith("Формат изображения не поддерживается в DOCX", StringComparison.Ordinal))
                .ToList();
            Check(docxWarnings.Count == 0, $"{name}: DOCX-публикация без предупреждений" + Describe(docxWarnings));
            CheckValidDocx(docxPath, name);
        }
        finally
        {
            try
            {
                Directory.Delete(output, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static string Describe(IReadOnlyList<ValidationIssue> issues) =>
        Describe(issues.Select(i => $"{(i.FilePath is null ? string.Empty : Path.GetFileName(i.FilePath) + ": ")}{i.Message}").ToList());

    private static string Describe(IReadOnlyList<string> messages) =>
        messages.Count == 0 ? string.Empty : $" ({messages.Count}): " + string.Join("; ", messages.Take(10));

    /// <summary>Корень репозитория — ближайшая вверх от сборки тестов папка с DitaStudio.sln.</summary>
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DitaStudio.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория (DitaStudio.sln) выше " + AppContext.BaseDirectory);
    }
}
