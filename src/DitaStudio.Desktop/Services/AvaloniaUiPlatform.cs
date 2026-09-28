using Avalonia.Threading;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Services;

/// <summary>UI-поток Avalonia: Dispatcher.UIThread и DispatcherTimer.</summary>
public sealed class AvaloniaUiPlatform : IUiPlatform
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

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

/// <summary>Печать в PDF встроенным браузером появится с CefGlue (этап 6 переноса); пока
/// публикация откатывается на печать установленным Edge/Chrome без своих колонтитулов.</summary>
public sealed class PendingPdfPrinter : IPdfPrinter
{
    public Task<string?> ExportAsync(string htmlPath, string pdfPath, bool showHeaderFooter, string? headerText, string? footerText) =>
        Task.FromResult<string?>("встроенный браузер для печати в PDF ещё не подключён в этой версии");
}
