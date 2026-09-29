using DitaStudio.Core.Publishing;
using DitaStudio.Desktop.Preview;
using DitaStudio.Presentation.Services;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Шаблоны колонтитулов PDF (встроенный Chromium): текст, номера страниц, картинка.</summary>
public sealed class PdfTemplateTests
{
    private const string Logo = "data:image/png;base64,AAAA";

    [Fact]
    public void WithoutImage_TemplatesAreAsBefore()
    {
        Assert.Equal(CefPdfPrinter.HeaderTemplate("Шапка"), CefPdfPrinter.HeaderTemplate(new PdfPageDecoration(true, "Шапка", null)));
        Assert.Contains("pageNumber", CefPdfPrinter.FooterTemplate("Подвал"));
    }

    [Fact]
    public void HeaderImage_GoesToItsSlot_TextStaysCentered()
    {
        var html = CefPdfPrinter.HeaderTemplate(new PdfPageDecoration(true, "Руководство & Ко", null,
            Logo, DocxHeaderAlignment.Right, 12.5));
        Assert.Contains($"<img src=\"{Logo}\" style=\"height:12.5mm", html);
        var center = html.IndexOf("text-align:center", StringComparison.Ordinal);
        var right = html.IndexOf("text-align:right", StringComparison.Ordinal);
        Assert.True(center < html.IndexOf("Руководство &amp; Ко", StringComparison.Ordinal));
        Assert.True(right < html.IndexOf("<img", StringComparison.Ordinal), "картинка — в правом месте");
    }

    [Fact]
    public void FooterImage_KeepsPageNumbers()
    {
        var html = CefPdfPrinter.FooterTemplate(new PdfPageDecoration(true, null, "Компания", FooterImage: Logo,
            FooterImageAlignment: DocxHeaderAlignment.Left));
        Assert.Contains("pageNumber", html);
        Assert.Contains("Компания", html);
        Assert.True(html.IndexOf("<img", StringComparison.Ordinal) < html.IndexOf("Компания", StringComparison.Ordinal),
            "картинка и текст слева — рядом, картинка первой");
    }

    [Fact]
    public void Settings_UseDecorationTemplates()
    {
        var settings = CefPdfPrinter.CreateSettings(new PdfPageDecoration(true, "Шапка", "Подвал", Logo));
        Assert.True(settings.DisplayHeaderFooter);
        Assert.Contains("<img", settings.HeaderTemplate);
        Assert.False(CefPdfPrinter.CreateSettings(new PdfPageDecoration(false, "Шапка", null, Logo)).DisplayHeaderFooter);
    }

    /// <summary>В9б: колонтитулы из CSS (@page { @top-left { … } }) — шаблоны Chromium; перекрывают настройки диалога.</summary>
    [Fact]
    public void CssMarginBoxes_BecomeTemplates_AndOverrideDialogSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "DitaStudioPdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "m.ditamap"), "<map><title>M</title></map>");
            File.WriteAllText(Path.Combine(root, "custom.css"),
                "@page { @top-left { content: \"Слева\" url(logo.png); color: #C00000; font-size: 9pt; border-bottom: 1pt solid #888 }" +
                " @bottom-right { content: \"Стр. \" counter(page) \" из \" counter(pages); font-weight: bold } }");
            File.WriteAllBytes(Path.Combine(root, "logo.png"), new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var project = new DitaStudio.Core.Project.DitaProject(root);
            project.Scan();
            project.SetCustomCssPath("custom.css");
            project.SetPdfHeaderFooter(false, "Из диалога", "Подвал из диалога");
            project.SetDocxLayout(new DocxLayout { MarginTopMm = 20, MarginBottomMm = 20 });

            var decoration = PdfPageDecoration.For(project);
            Assert.True(decoration.Show, "CSS включает колонтитулы, даже если в диалоге они выключены");
            var header = CefPdfPrinter.HeaderTemplate(decoration);
            var footer = CefPdfPrinter.FooterTemplate(decoration);
            Assert.Contains("Слева", header);
            Assert.DoesNotContain("Из диалога", header);
            Assert.Contains("color:#C00000", header);
            Assert.Contains("border-bottom:1pt solid #888", header);
            Assert.Contains("<img src=\"data:image/png;base64,", header);
            Assert.Contains("height:9mm", header); // поле 20 мм: область колонтитула 9 мм
            Assert.Contains("class=\"pageNumber\"", footer);
            Assert.Contains("class=\"totalPages\"", footer);
            Assert.DoesNotContain("Подвал из диалога", footer);
            Assert.True(footer.IndexOf("text-align:right", StringComparison.Ordinal) < footer.IndexOf("pageNumber", StringComparison.Ordinal), "номер — в правом поле");

            // Только верх в CSS: подвал из диалога не подставляется, если диалог его выключил.
            File.WriteAllText(Path.Combine(root, "custom.css"), "@page { @top-center { content: string(title) } }");
            var topOnly = PdfPageDecoration.For(project);
            Assert.Contains("class=\"title\"", CefPdfPrinter.HeaderTemplate(topOnly));
            Assert.DoesNotContain("pageNumber", CefPdfPrinter.FooterTemplate(topOnly));

            // Без CSS — прежние шаблоны из диалога.
            File.WriteAllText(Path.Combine(root, "custom.css"), "p { color: red }");
            project.SetPdfHeaderFooter(true, "Из диалога", "Подвал из диалога");
            var plain = PdfPageDecoration.For(project);
            Assert.Contains("Из диалога", CefPdfPrinter.HeaderTemplate(plain));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
