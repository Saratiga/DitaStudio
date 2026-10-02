using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Services;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Events;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Preview;

/// <summary>
/// Печать HTML в PDF встроенным Chromium — замена WPF WebView2PdfExporter. Скрытое окно
/// за пределами экрана с браузером: загрузить страницу, дождаться конца загрузки главного
/// фрейма, <c>PrintToPdf</c>. Колонтитулы — HTML-шаблоны Chromium: текст шапки по центру,
/// текст подвала слева и «стр. N из M» справа (у WebView2 было «название страницы / адрес»,
/// которые подменялись текстом проекта).
/// </summary>
public sealed class CefPdfPrinter : IPdfPrinter
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PrintTimeout = TimeSpan.FromSeconds(60);

    public Task<string?> ExportAsync(string htmlPath, string pdfPath, bool showHeaderFooter, string? headerText, string? footerText) =>
        ExportAsync(htmlPath, pdfPath, new PdfPageDecoration(showHeaderFooter, headerText, footerText));

    public Task<string?> ExportAsync(string htmlPath, string pdfPath, PdfPageDecoration decoration) =>
        Dispatcher.UIThread.InvokeAsync(() => ExportOnUiThreadAsync(htmlPath, pdfPath, decoration));

    private static async Task<string?> ExportOnUiThreadAsync(string htmlPath, string pdfPath, PdfPageDecoration decoration)
    {
        if (CefHost.EnsureStarted() is { } error)
        {
            return error;
        }

        Window? window = null;
        HostedCefBrowser? browser = null;
        try
        {
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            browser = new HostedCefBrowser();
            browser.LoadEnd += (_, e) =>
            {
                if (e.Frame.IsMain)
                {
                    loaded.TrySetResult(e.HttpStatusCode is 0 or 200);
                }
            };
            browser.LoadError += (_, e) =>
            {
                if (e.Frame.IsMain && e.ErrorCode != CefErrorCode.Aborted)
                {
                    loaded.TrySetResult(false);
                }
            };
            browser.Address = new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri;

            window = new Window
            {
                Width = 900,
                Height = 700,
                ShowInTaskbar = false,
                ShowActivated = false,
                SystemDecorations = SystemDecorations.None,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(-20000, -20000),
                Content = browser
            };
            window.Show();

            if (await Task.WhenAny(loaded.Task, Task.Delay(LoadTimeout)) != loaded.Task || !loaded.Task.Result)
            {
                return Loc.T("Preview_TheBuiltInBrowserCouldNot");
            }

            if (browser.Host is not { } host)
            {
                return Loc.T("Preview_TheBuiltInBrowserIsNot");
            }

            TryDelete(pdfPath);
            var callback = new PrintCallback();
            host.PrintToPdf(pdfPath, CreateSettings(decoration), callback);
            if (await Task.WhenAny(callback.Done, Task.Delay(PrintTimeout)) != callback.Done)
            {
                return Loc.T("Preview_PrintingToPDFDidNotFinish");
            }

            return callback.Done.Result && File.Exists(pdfPath) ? null : Loc.T("Preview_TheBuiltInBrowserCouldNot2");
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            window?.Close();
            browser?.Dispose();
        }
    }

    /// <summary>Параметры печати: A4, поля по умолчанию, фон элементов (заливка заметок, кода).</summary>
    public static CefPdfPrintSettings CreateSettings(bool showHeaderFooter, string? headerText, string? footerText) =>
        CreateSettings(new PdfPageDecoration(showHeaderFooter, headerText, footerText));

    public static CefPdfPrintSettings CreateSettings(PdfPageDecoration decoration)
    {
        var settings = new CefPdfPrintSettings
        {
            PaperWidth = 8.27,
            PaperHeight = 11.69,
            PrintBackground = true,
            PreferCssPageSize = true,
            DisplayHeaderFooter = decoration.Show
        };

        if (decoration.Show)
        {
            settings.HeaderTemplate = HeaderTemplate(decoration);
            settings.FooterTemplate = FooterTemplate(decoration);
        }

        return settings;
    }

    // Шаблоны колонтитулов рисуются в своём документе без стилей страницы: размер шрифта и
    // отступы задаются явно, иначе текст выходит крошечным и прижатым к краю листа.
    private const string TemplateStyle = "font-family:'Segoe UI',Arial,sans-serif;font-size:9px;color:#555;width:100%;margin:0 12mm;";

    public static string HeaderTemplate(string? text) => HeaderTemplate(new PdfPageDecoration(true, text, null));

    public static string FooterTemplate(string? text) => FooterTemplate(new PdfPageDecoration(true, null, text));

    /// <summary>Шапка: текст по центру; картинка (логотип) — на своём месте слева, по центру или справа.</summary>
    public static string HeaderTemplate(PdfPageDecoration decoration)
    {
        if (decoration.HeaderHtml is { } cssHeader)
        {
            return cssHeader;
        }

        var text = WebUtility.HtmlEncode(decoration.HeaderText ?? string.Empty);
        if (decoration.HeaderImage is null)
        {
            return $"<div style=\"{TemplateStyle}text-align:center\">{text}</div>";
        }

        return Slots(Image(decoration.HeaderImage, decoration.HeaderImageHeightMm), decoration.HeaderImageAlignment,
            (DocxHeaderAlignment.Center, text));
    }

    /// <summary>Подвал: текст слева, «стр. N из M» справа; картинка — на своём месте.</summary>
    public static string FooterTemplate(PdfPageDecoration decoration)
    {
        if (decoration.FooterHtml is { } cssFooter)
        {
            return cssFooter;
        }

        var text = WebUtility.HtmlEncode(decoration.FooterText ?? string.Empty);
        const string pages = "стр. <span class=\"pageNumber\"></span> из <span class=\"totalPages\"></span>";
        if (decoration.FooterImage is null)
        {
            return $"<div style=\"{TemplateStyle}display:flex;justify-content:space-between\">" +
                   $"<span>{text}</span><span>{pages}</span></div>";
        }

        return Slots(Image(decoration.FooterImage, decoration.FooterImageHeightMm), decoration.FooterImageAlignment,
            (DocxHeaderAlignment.Left, text), (DocxHeaderAlignment.Right, pages));
    }

    private static string Image(string dataUri, double heightMm) =>
        $"<img src=\"{dataUri}\" style=\"height:{heightMm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}mm;max-width:100%;vertical-align:middle\">";

    /// <summary>Три места — слева, по центру, справа; картинка и текст на одном месте встают рядом.</summary>
    private static string Slots(string image, DocxHeaderAlignment imageAt, params (DocxHeaderAlignment At, string Html)[] texts)
    {
        var html = new System.Text.StringBuilder($"<div style=\"{TemplateStyle}display:flex;align-items:center\">");
        foreach (var (slot, align) in new[] { (DocxHeaderAlignment.Left, "left"), (DocxHeaderAlignment.Center, "center"), (DocxHeaderAlignment.Right, "right") })
        {
            var parts = new List<string>();
            if (slot == imageAt)
            {
                parts.Add(image);
            }

            parts.AddRange(texts.Where(t => t.At == slot && t.Html.Length > 0).Select(t => t.Html));
            html.Append($"<div style=\"flex:1;text-align:{align}\">").Append(string.Join("&nbsp;&nbsp;", parts)).Append("</div>");
        }

        return html.Append("</div>").ToString();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // старый PDF открыт в просмотрщике — печать сообщит об ошибке сама
        }
    }

    private sealed class PrintCallback : CefPdfPrintCallback
    {
        private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> Done => _done.Task;

        protected override void OnPdfPrintFinished(string path, bool ok) => _done.TrySetResult(ok);
    }
}
