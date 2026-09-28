using System.Windows;
using System.Windows.Threading;
using DitaStudio.Presentation.Services;

namespace DitaStudio.App.Services;

/// <summary>UI-поток WPF: Dispatcher и DispatcherTimer.</summary>
public sealed class WpfUiPlatform : IUiPlatform
{
    public void Post(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick) => new Timer(interval, tick);

    private sealed class Timer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public Timer(TimeSpan interval, Action tick)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
            _timer.Tick += (_, _) => tick();
        }

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();
    }
}

/// <summary>Печать в PDF со своими колонтитулами через скрытый WebView2.</summary>
public sealed class WebView2PdfPrinter : IPdfPrinter
{
    public Task<string?> ExportAsync(string htmlPath, string pdfPath, bool showHeaderFooter, string? headerText, string? footerText) =>
        WebView2PdfExporter.ExportAsync(htmlPath, pdfPath, showHeaderFooter, headerText, footerText);
}
