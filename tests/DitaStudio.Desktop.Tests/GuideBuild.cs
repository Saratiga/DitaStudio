using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Desktop.Services;
using DitaStudio.Docx;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Сборка руководств пользователя в PDF и DOCX (docs/guides) из samples/DitaStudioBeginnerGuide и samples/DitaStudioGuide — тем же
/// кодом, что и команды «Публикация» редактора: единый HTML печатается в PDF установленным Edge/Chrome, DOCX — со снимками
/// SVG, растеризованными Skia. Обычный прогон тестов её пропускает, сборка — только с переменной окружения
/// <c>DITASTUDIO_GUIDE_BUILD=1</c>:
/// <code>$env:DITASTUDIO_GUIDE_BUILD=1; dotnet test tests/DitaStudio.Desktop.Tests --filter GuideBuild</code>
/// </summary>
public sealed class GuideBuild
{
    [Fact]
    public void BuildGuides()
    {
        if (Environment.GetEnvironmentVariable("DITASTUDIO_GUIDE_BUILD") != "1")
        {
            return;
        }

        var root = RepositoryRoot();
        var target = Path.Combine(root, "docs", "guides");
        Directory.CreateDirectory(target);
        Build(Path.Combine(root, "samples", "DitaStudioBeginnerGuide"), Path.Combine(target, "DitaStudio-Beginner-Guide"));
        Build(Path.Combine(root, "samples", "DitaStudioGuide"), Path.Combine(target, "DitaStudio-User-Guide"));
    }

    private static void Build(string projectRoot, string outputBase)
    {
        var project = new DitaProject(projectRoot);
        project.Scan();
        var map = Path.Combine(projectRoot, "guide.ditamap");
        var temp = Path.Combine(Path.GetTempPath(), "DitaStudioGuideBuild", Guid.NewGuid().ToString("N"));
        try
        {
            var html = new HtmlPublisher(project).Publish(map, new PublishOptions { OutputDirectory = temp, SingleFile = true });
            Assert.Empty(html.Warnings);
            var error = PdfExporter.ExportToPdf(html.EntryFile, outputBase + ".pdf");
            Assert.True(error is null, error);

            var docx = new DocxPublisher(project).Publish(map,
                new PublishOptions { OutputDirectory = temp, ImageRasterizer = new SkiaSvgRasterizer() }, outputBase + ".docx");
            Assert.Empty(docx.Warnings);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch (IOException)
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DitaStudio.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория (DitaStudio.sln).");
    }
}
