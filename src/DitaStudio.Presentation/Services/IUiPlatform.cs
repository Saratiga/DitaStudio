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
}
