using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Presentation.Services;

/// <summary>Периодический таймер в UI-потоке.</summary>
public interface IUiTimer
{
    void Start();

    void Stop();
}

/// <summary>UI-поток оболочки: переход в него из фоновых событий и таймеры.</summary>
public interface IUiPlatform
{
    /// <summary>Выполнить в UI-потоке (асинхронно, без ожидания).</summary>
    void Post(Action action);

    IUiTimer CreateTimer(TimeSpan interval, Action tick);
}

/// <summary>Печать HTML в PDF встроенным браузером оболочки (WebView2 в WPF, Chromium
/// через CefGlue в Avalonia) — умеет свои колонтитулы. Возвращает текст ошибки или null.</summary>
public interface IPdfPrinter
{
    Task<string?> ExportAsync(string htmlPath, string pdfPath, bool showHeaderFooter, string? headerText, string? footerText);

    /// <summary>Печать с картинками в колонтитулах; оболочка без их поддержки печатает только текст.</summary>
    Task<string?> ExportAsync(string htmlPath, string pdfPath, PdfPageDecoration decoration) =>
        ExportAsync(htmlPath, pdfPath, decoration.Show, decoration.HeaderText, decoration.FooterText);
}

/// <summary>Колонтитулы PDF: текст и картинки (data-URI, выравнивание, высота в мм).</summary>
public sealed record PdfPageDecoration(
    bool Show,
    string? HeaderText,
    string? FooterText,
    string? HeaderImage = null,
    DocxHeaderAlignment HeaderImageAlignment = DocxHeaderAlignment.Left,
    double HeaderImageHeightMm = 10,
    string? FooterImage = null,
    DocxHeaderAlignment FooterImageAlignment = DocxHeaderAlignment.Left,
    double FooterImageHeightMm = 10)
{
    /// <summary>Колонтитулы проекта: текст из настроек PDF, картинки — общие с DOCX.</summary>
    public static PdfPageDecoration For(DitaProject project)
    {
        var layout = project.DocxLayout;
        string? Uri(string relative) => DocxLayout.ResolveImage(project.RootPath, relative) is { } full ? DocxLayout.ImageDataUri(full) : null;
        return new PdfPageDecoration(project.PdfShowHeaderFooter, project.PdfHeaderText, project.PdfFooterText,
            Uri(layout.HeaderImage), layout.HeaderImageAlignment, layout.HeaderImageHeightMm,
            Uri(layout.FooterImage), layout.FooterImageAlignment, layout.FooterImageHeightMm);
    }
}
