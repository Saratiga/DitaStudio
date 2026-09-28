using Xilium.CefGlue;
using Xilium.CefGlue.Common;

namespace DitaStudio.Desktop.Preview;

/// <summary>
/// Встроенный Chromium (CefGlue) для предпросмотра и печати в PDF. Запускается лениво — при
/// первом открытии предпросмотра или экспорте в PDF, а не при старте редактора: Chromium
/// тяжёлый, а многим сеансам он не нужен. Если его не удалось запустить (нет нативных
/// библиотек, headless-тесты), <see cref="EnsureStarted"/> возвращает причину, и вызывающий
/// откатывается на внешний браузер — редактор продолжает работать.
/// </summary>
public static class CefHost
{
    private static bool _attempted;
    private static string? _error;

    /// <summary>Не запускать Chromium вовсе (headless-тесты, <c>DITASTUDIO_NO_CEF=1</c>).</summary>
    public static bool Disabled { get; set; } = Environment.GetEnvironmentVariable("DITASTUDIO_NO_CEF") == "1";

    public static bool IsStarted => _attempted && _error is null;

    /// <summary>Запускает Chromium, если ещё не запущен. Null — готов, иначе — почему нельзя.
    /// Вызывать из UI-потока (на macOS CEF требует главный поток).</summary>
    public static string? EnsureStarted()
    {
        if (Disabled)
        {
            return "встроенный браузер отключён";
        }

        if (_attempted)
        {
            return _error;
        }

        _attempted = true;
        try
        {
            var cache = Path.Combine(Path.GetTempPath(), "DitaStudioCef", Environment.ProcessId.ToString());
            CefRuntimeLoader.Initialize(new CefSettings
            {
                RootCachePath = cache,
                CachePath = Path.Combine(cache, "cache"),
                WindowlessRenderingEnabled = true,
                LogSeverity = CefLogSeverity.Disable,
                Locale = "ru"
            });
        }
        catch (Exception ex)
        {
            _error = "не удалось запустить встроенный браузер: " + ex.Message;
        }

        return _error;
    }

    /// <summary>Останавливает Chromium при выходе из программы (если запускался).</summary>
    public static void Shutdown()
    {
        if (!IsStarted)
        {
            return;
        }

        try
        {
            CefRuntime.Shutdown();
        }
        catch (Exception)
        {
            // при выходе ошибка остановки браузера уже ни на что не влияет
        }
    }
}
