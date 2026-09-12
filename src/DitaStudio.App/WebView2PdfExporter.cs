using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace DitaStudio.App;

/// <summary>
/// Экспорт в PDF через скрытое окно с WebView2 — единственный способ задать собственный текст
/// колонтитулов (CoreWebView2PrintSettings.HeaderTitle/FooterUri поддерживает это, в отличие
/// от CLI-печати в <see cref="DitaStudio.Core.Publishing.PdfExporter"/>). Заодно служит запасным
/// путём, если в системе нет установленного Edge/Chrome — Evergreen Runtime WebView2 на
/// Windows 11 обычно есть даже без отдельного браузера.
/// </summary>
public static class WebView2PdfExporter
{
    public static async Task<string?> ExportAsync(string htmlPath, string pdfPath, bool showHeaderFooter,
        string? headerText, string? footerText)
    {
        Window? window = null;
        WebView2? webView = null;
        try
        {
            window = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -5000,
                Top = -5000
            };

            webView = new WebView2();
            window.Content = webView;
            window.Show();

            await webView.EnsureCoreWebView2Async();

            var navigationDone = new TaskCompletionSource<bool>();
            webView.CoreWebView2.NavigationCompleted += (_, args) => navigationDone.TrySetResult(args.IsSuccess);
            webView.CoreWebView2.Navigate(new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri);

            var winner = await Task.WhenAny(navigationDone.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (winner != navigationDone.Task || !navigationDone.Task.Result)
            {
                return "Не удалось загрузить HTML в WebView2 для печати.";
            }

            var printSettings = webView.CoreWebView2.Environment.CreatePrintSettings();
            printSettings.ShouldPrintHeaderAndFooter = showHeaderFooter;
            if (showHeaderFooter)
            {
                printSettings.HeaderTitle = headerText ?? string.Empty;
                printSettings.FooterUri = footerText ?? string.Empty;
            }

            var ok = await webView.CoreWebView2.PrintToPdfAsync(pdfPath, printSettings);
            return ok ? null : "WebView2 не смог напечатать PDF.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            webView?.Dispose();
            window?.Close();
        }
    }
}
