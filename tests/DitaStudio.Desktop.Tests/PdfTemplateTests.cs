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
}
