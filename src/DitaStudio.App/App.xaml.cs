using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DitaStudio.Presentation.Plugins;

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
        // Сначала страховочная копия правок: после такой ошибки состояние приложения
        // может оказаться нерабочим, и пользователь закроет его, не сохранившись.
        try
        {
            (MainWindow as MainWindow)?.SnapshotForRecovery();
        }
        catch
        {
            // копия не получилась — сообщение об исходной ошибке важнее
        }

        MessageBox.Show(
            $"Непредвиденная ошибка:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "DITA Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
