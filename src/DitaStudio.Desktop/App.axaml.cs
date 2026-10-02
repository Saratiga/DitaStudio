using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using DitaStudio.Core.Localization;
using DitaStudio.Presentation;
using DitaStudio.Presentation.Plugins;

namespace DitaStudio.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Loc.Instance.Initialize(); // язык интерфейса: выбор пользователя, иначе язык системы, иначе английский
        RequestedThemeVariant = ThemeSettings.LoadDark() ? ThemeVariant.Dark : ThemeVariant.Light;

        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        try
        {
            Directory.CreateDirectory(pluginsDir);
            PluginRegistry.Load(pluginsDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // без папки плагинов редактор работает как обычно
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => Preview.CefHost.Shutdown();
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                // Сначала страховочная копия правок: после такой ошибки состояние приложения
                // может оказаться нерабочим, и пользователь закроет его, не сохранившись.
                try
                {
                    window.SnapshotForRecovery();
                }
                catch (Exception)
                {
                    // копия не получилась — сообщение об исходной ошибке важнее
                }

                _ = window.ViewModel.Dialogs.MessageAsync("DITA Studio",
                    $"Непредвиденная ошибка:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}");
                e.Handled = true;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
