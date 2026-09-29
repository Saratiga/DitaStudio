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
    double FooterImageHeightMm = 10,
    string? HeaderHtml = null,
    string? FooterHtml = null)
{
    /// <summary>Колонтитулы проекта: текст из настроек PDF, картинки — общие с DOCX.</summary>
    public static PdfPageDecoration For(DitaProject project)
    {
        var layout = project.DocxLayout;
        string? Uri(string relative) => DocxLayout.ResolveImage(project.RootPath, relative) is { } full ? DocxLayout.ImageDataUri(full) : null;

        // Поля страницы из CSS проекта (@page { @top-left { content: … } }) перекрывают колонтитулы из диалога.
        var boxes = PageMarginBoxes.Parse(project.ReadCustomCss(out _));
        var topMm = layout.MarginTopMm ?? 10;
        var bottomMm = layout.MarginBottomMm ?? 10;
        var sideMm = layout.MarginLeftMm ?? 12;
        var headerArea = PdfMarginTemplates.AreaHeightMm(topMm);
        var footerArea = PdfMarginTemplates.AreaHeightMm(bottomMm);
        var show = project.PdfShowHeaderFooter || boxes.HasTop || boxes.HasBottom;
        var headerHtml = boxes.HasTop ? PdfMarginTemplates.Html(boxes.Top, Uri, sideMm, headerArea) : boxes.HasBottom && !project.PdfShowHeaderFooter ? "<div></div>" : null;
        var footerHtml = boxes.HasBottom ? PdfMarginTemplates.Html(boxes.Bottom, Uri, sideMm, footerArea) : boxes.HasTop && !project.PdfShowHeaderFooter ? "<div></div>" : null;
        return new PdfPageDecoration(show, project.PdfHeaderText, project.PdfFooterText,
            Uri(layout.HeaderImage), layout.HeaderImageAlignment, layout.FitHeaderFooterImages ? headerArea : layout.HeaderImageHeightMm,
            Uri(layout.FooterImage), layout.FooterImageAlignment, layout.FitHeaderFooterImages ? footerArea : layout.FooterImageHeightMm,
            headerHtml, footerHtml);
    }
}
