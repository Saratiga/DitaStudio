using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DitaStudio.Presentation.Services;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Events;

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
        Dispatcher.UIThread.InvokeAsync(() => ExportOnUiThreadAsync(htmlPath, pdfPath, showHeaderFooter, headerText, footerText));

    private static async Task<string?> ExportOnUiThreadAsync(string htmlPath, string pdfPath, bool showHeaderFooter, string? headerText, string? footerText)
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
                return "встроенный браузер не смог загрузить HTML для печати";
            }

            if (browser.Host is not { } host)
            {
                return "встроенный браузер не готов к печати";
            }

            TryDelete(pdfPath);
            var callback = new PrintCallback();
            host.PrintToPdf(pdfPath, CreateSettings(showHeaderFooter, headerText, footerText), callback);
            if (await Task.WhenAny(callback.Done, Task.Delay(PrintTimeout)) != callback.Done)
            {
                return "печать в PDF не закончилась за минуту";
            }

            return callback.Done.Result && File.Exists(pdfPath) ? null : "встроенный браузер не смог напечатать PDF";
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
    public static CefPdfPrintSettings CreateSettings(bool showHeaderFooter, string? headerText, string? footerText)
    {
        var settings = new CefPdfPrintSettings
        {
            PaperWidth = 8.27,
            PaperHeight = 11.69,
            PrintBackground = true,
            PreferCssPageSize = true,
            DisplayHeaderFooter = showHeaderFooter
        };

        if (showHeaderFooter)
        {
            settings.HeaderTemplate = HeaderTemplate(headerText);
            settings.FooterTemplate = FooterTemplate(footerText);
        }

        return settings;
    }

    // Шаблоны колонтитулов рисуются в своём документе без стилей страницы: размер шрифта и
    // отступы задаются явно, иначе текст выходит крошечным и прижатым к краю листа.
    private const string TemplateStyle = "font-family:'Segoe UI',Arial,sans-serif;font-size:9px;color:#555;width:100%;margin:0 12mm;";

    public static string HeaderTemplate(string? text) =>
        $"<div style=\"{TemplateStyle}text-align:center\">{WebUtility.HtmlEncode(text ?? string.Empty)}</div>";

    public static string FooterTemplate(string? text) =>
        $"<div style=\"{TemplateStyle}display:flex;justify-content:space-between\">" +
        $"<span>{WebUtility.HtmlEncode(text ?? string.Empty)}</span>" +
        "<span>стр. <span class=\"pageNumber\"></span> из <span class=\"totalPages\"></span></span></div>";

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
