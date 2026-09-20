using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DitaStudio.App.Plugins;

namespace DitaStudio.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Форматы дат и чисел в интерфейсе — по языку системы.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag)));

        DispatcherUnhandledException += OnUnhandledException;
        ThemeManager.Initialize();

        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        Directory.CreateDirectory(pluginsDir);
        PluginRegistry.Load(pluginsDir);

        base.OnStartup(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Непредвиденная ошибка:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "DITA Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
