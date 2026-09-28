using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Desktop.Preview;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Настоящий Chromium (CefGlue): печать в PDF с колонтитулами. Нужен X-сервер, поэтому
/// раздел идёт только при DITASTUDIO_CEF_TESTS=1 (в CI — под xvfb-run); в обычном прогоне
/// тесты отмечаются как пройденные без действия.
/// </summary>
public sealed class CefPreviewTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("DITASTUDIO_CEF_TESTS") == "1";

    [AvaloniaFact]
    public async Task PrintToPdf_WithHeaderAndFooter()
    {
        if (!Enabled)
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "DitaStudioCefTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var html = Path.Combine(dir, "page.html");
        var pdf = Path.Combine(dir, "page.pdf");
        await File.WriteAllTextAsync(html,
            "<html><body><h1>Проверка печати</h1>" + string.Concat(Enumerable.Repeat("<p>Абзац текста для второй страницы.</p>", 120)) + "</body></html>");

        var error = await new CefPdfPrinter().ExportAsync(html, pdf, showHeaderFooter: true, headerText: "Шапка", footerText: "Подвал");

        Assert.Null(error);
        var bytes = await File.ReadAllBytesAsync(pdf);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        var pages = Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), @"/Type\s*/Page[^s]").Count;
        Assert.True(pages >= 2, $"ожидалось несколько страниц, получено {pages}");
    }

    [AvaloniaFact]
    public async Task PrintToPdf_WithLogoInHeader()
    {
        if (!Enabled)
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "DitaStudioCefTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var html = Path.Combine(dir, "page.html");
        var pdf = Path.Combine(dir, "page.pdf");
        await File.WriteAllTextAsync(html, "<html><body><h1>Логотип в шапке</h1><p>Текст без картинок.</p></body></html>");
        const string logo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        var error = await new CefPdfPrinter().ExportAsync(html, pdf,
            new DitaStudio.Presentation.Services.PdfPageDecoration(true, "Шапка", "Подвал", logo, Core.Publishing.DocxHeaderAlignment.Left, 10));

        Assert.Null(error);
        var text = System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(pdf));
        Assert.Matches(@"/Subtype\s*/Image", text);
    }

    [AvaloniaTheory]
    [InlineData(PreviewFormat.Html)]
    [InlineData(PreviewFormat.Pdf)]
    [InlineData(PreviewFormat.DocxApprox)]
    public async Task PreviewPane_ShowsDocumentInEmbeddedBrowser(PreviewFormat format)
    {
        if (!Enabled)
        {
            return;
        }

        var root = Path.Combine(RepositoryRoot(), "samples", "GuideSample");
        var project = new DitaProject(root);
        project.Scan();
        var document = DitaDocument.Load(Path.Combine(root, "tasks", "install.dita"));
        var pane = new PreviewPane(project, document, () => null, new CefPdfPrinter());
        var window = new Window { Width = 1000, Height = 800, Content = pane };
        window.Show();

        pane.Format = format;
        var shown = await pane.RefreshAsync();

        Assert.Equal(string.Empty, pane.StatusText);
        Assert.EndsWith(format == PreviewFormat.Pdf ? ".pdf" : ".html", shown);

        // Даём Chromium загрузить и отрисовать страницу (PDF-просмотрщику — чуть дольше).
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, $"preview-{format.ToString().ToLowerInvariant()}.png"));
        pane.DisposeBrowser();
        window.Close();
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
