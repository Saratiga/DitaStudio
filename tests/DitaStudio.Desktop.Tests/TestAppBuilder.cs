using Avalonia;
using Avalonia.Headless;
using DitaStudio.Desktop;

[assembly: AvaloniaTestApplication(typeof(DitaStudio.Desktop.Tests.TestAppBuilder))]

namespace DitaStudio.Desktop.Tests;

/// <summary>То же приложение, что и настоящее, но на headless-платформе: рендер через Skia
/// в память (UseHeadlessDrawing=false), чтобы можно было снимать кадры окон.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // Встроенный Chromium на headless-платформе не запускается — предпросмотр уходит в
        // запасной режим; настоящую печать проверяет отдельный тест под X-сервером.
        Preview.CefHost.Disabled = Environment.GetEnvironmentVariable("DITASTUDIO_CEF_TESTS") != "1";
        return AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
