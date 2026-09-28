using Avalonia;
using Avalonia.Headless;
using DitaStudio.Desktop;

[assembly: AvaloniaTestApplication(typeof(DitaStudio.Desktop.Tests.TestAppBuilder))]

namespace DitaStudio.Desktop.Tests;

/// <summary>То же приложение, что и настоящее, но на headless-платформе: рендер через Skia
/// в память (UseHeadlessDrawing=false), чтобы можно было снимать кадры окон.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
