using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DitaStudio.Presentation;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Паритет с WPF-версией (она остаётся как легаси): каждый пункт меню и подсказка кнопки WPF
/// есть и в Avalonia, и сценарии автоматизированных UI-тестов WPF (tests/DitaStudio.UiTests,
/// подмножество docs/TESTPLAN.md) проходят в Avalonia-окне так же — через меню, как пользователь.
/// </summary>
public sealed class ParityTests : IDisposable
{
    private readonly string _project;
    private readonly string? _recentBackup;
    private readonly string _recentPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "recent-projects.txt");

    public ParityTests()
    {
        _recentBackup = File.Exists(_recentPath) ? File.ReadAllText(_recentPath) : null;
        _project = Path.Combine(Path.GetTempPath(), "DitaStudioParityTests", Guid.NewGuid().ToString("N"), "GuideSample");
        CopyDirectory(Path.Combine(RepositoryRoot(), "samples", "GuideSample"), _project);
    }

    public void Dispose()
    {
        try
        {
            if (_recentBackup is null)
            {
                File.Delete(_recentPath);
            }
            else
            {
                File.WriteAllText(_recentPath, _recentBackup);
            }

            Directory.Delete(Path.GetDirectoryName(_project)!, true);
        }
        catch (IOException)
        {
            // временные файлы удалятся системой
        }
    }

    // ------------------------------------------------------------ разметка окна

    private static IEnumerable<string> Attributes(string file, params string[] names)
    {
        var xml = XDocument.Load(Path.Combine(RepositoryRoot(), file));
        return xml.Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => names.Contains(a.Name.LocalName))
            .Select(a => a.Value)
            .Where(v => !v.StartsWith('{'))
            .Distinct();
    }

    [Fact]
    public void EveryWpfMenuItem_ExistsInAvalonia()
    {
        var avalonia = Attributes("src/DitaStudio.Desktop/MainWindow.axaml", "Header").ToHashSet();
        var missing = Attributes("src/DitaStudio.App/MainWindow.xaml", "Header").Where(h => !avalonia.Contains(h)).ToList();
        Assert.True(missing.Count == 0, "нет в Avalonia: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryWpfToolbarTooltip_ExistsInAvalonia()
    {
        var avalonia = Attributes("src/DitaStudio.Desktop/MainWindow.axaml", "ToolTip.Tip", "ToolTip").ToHashSet();
        var missing = Attributes("src/DitaStudio.App/MainWindow.xaml", "ToolTip").Where(t => !avalonia.Contains(t)).ToList();
        Assert.True(missing.Count == 0, "нет в Avalonia: " + string.Join(", ", missing));
    }

    // ------------------------------------------------------------ сценарии SmokeTests WPF

    [AvaloniaFact]
    public void MainWindow_HasExpectedTitle()
    {
        var window = new MainWindow();
        window.Show();
        Assert.Contains("DITA Studio", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OpenRecentProject_UpdatesStatusBarAndWindowTitle()
    {
        var window = await OpenViaRecentProjectsAsync();
        Assert.Matches(@"Проект открыт: \d+ файлов, \d+ ключ", window.ViewModel.StatusText);
        Assert.Contains("GuideSample", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void HelpAbout_OpensDialogWithProductNameAndCloses()
    {
        var window = new MainWindow();
        window.Show();

        ClickMenu(window, "Справка", "О программе");
        var about = Assert.Single(window.OwnedWindows);
        Assert.Contains("DITA Studio", AllText(about));

        ClickButton(about, "OK", "ОК", "Закрыть");
        Assert.Empty(window.OwnedWindows);
        window.Close();
    }

    [AvaloniaFact]
    public async Task PageSetupDialog_SavesPaperOrientationAndMargins()
    {
        var window = await OpenViaRecentProjectsAsync();
        ClickMenu(window, "Публикация", "Параметры страницы…");
        var dialog = Assert.Single(window.OwnedWindows);
        Assert.Contains("Размер бумаги", AllText(dialog));

        dialog.GetLogicalDescendants().OfType<ComboBox>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Размер бумаги").SelectedItem = "A5";
        dialog.GetLogicalDescendants().OfType<RadioButton>().First(r => r.Content as string == "Альбомная").IsChecked = true;
        dialog.GetLogicalDescendants().OfType<TextBox>().First(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Поле сверху, мм").Text = "12,5";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("A5, альбомная: 210 × 148 мм", AllText(dialog));

        var frame = dialog.CaptureRenderedFrame();
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, "dialog-page-setup.png"));

        ClickButton(dialog, "ОК");
        Dispatcher.UIThread.RunJobs();
        var layout = window.ViewModel.Project!.DocxLayout;
        Assert.Equal("A5", layout.PaperSize);
        Assert.True(layout.Landscape);
        Assert.Equal(12.5, layout.MarginTopMm);
        Assert.Null(layout.MarginLeftMm);
        window.Close();
    }

    [AvaloniaFact]
    public async Task PageSetupDialog_CustomPaperSize_ShowsFieldsValidatesAndSaves()
    {
        var window = await OpenViaRecentProjectsAsync();
        ClickMenu(window, "Публикация", "Параметры страницы…");
        var dialog = Assert.Single(window.OwnedWindows);
        TextBox Box(string name) => dialog.GetLogicalDescendants().OfType<TextBox>().First(t => Avalonia.Automation.AutomationProperties.GetName(t) == name);
        var width = Box("Ширина листа, мм");
        var height = Box("Высота листа, мм");
        Assert.False(((Control)width.Parent!).IsVisible, "поля своего размера скрыты, пока не выбран пункт");

        dialog.GetLogicalDescendants().OfType<ComboBox>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Размер бумаги").SelectedItem = "Свой размер…";
        Dispatcher.UIThread.RunJobs();
        Assert.True(((Control)width.Parent!).IsVisible);
        Assert.Contains("Укажите ширину и высоту листа", AllText(dialog));

        width.Text = "10";
        height.Text = "300";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Укажите ширину и высоту листа", AllText(dialog)); // 10 мм — меньше границы

        var separator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        width.Text = "120" + separator + "5";
        height.Text = "300";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Свой размер, книжная: 120" + separator + "5 × 300 мм", AllText(dialog));

        ClickButton(dialog, "ОК");
        Dispatcher.UIThread.RunJobs();
        var layout = window.ViewModel.Project!.DocxLayout;
        Assert.Equal(120.5, layout.PaperWidthMm);
        Assert.Equal(300, layout.PaperHeightMm);
        Assert.Equal(string.Empty, layout.PaperSize);

        // Повторное открытие показывает сохранённый размер, а выбор A4 снимает свой.
        ClickMenu(window, "Публикация", "Параметры страницы…");
        dialog = Assert.Single(window.OwnedWindows);
        Assert.Equal("Свой размер…", dialog.GetLogicalDescendants().OfType<ComboBox>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Размер бумаги").SelectedItem);
        dialog.GetLogicalDescendants().OfType<ComboBox>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Размер бумаги").SelectedItem = "A4";
        Dispatcher.UIThread.RunJobs();
        ClickButton(dialog, "ОК");
        Dispatcher.UIThread.RunJobs();
        layout = window.ViewModel.Project!.DocxLayout;
        Assert.Equal("A4", layout.PaperSize);
        Assert.Null(layout.PaperWidthMm);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DocxLayoutDialog_ShowsSectionsAndCancelDoesNotSave()
    {
        var window = await OpenViaRecentProjectsAsync();
        var settingsFile = Path.Combine(_project, ".ditastudio-docx");
        var existedBefore = File.Exists(settingsFile);

        ClickMenu(window, "Публикация", "Оформление DOCX…");
        var dialog = Assert.Single(window.OwnedWindows);
        var text = AllText(dialog);
        Assert.Contains("Титульная страница", text);
        Assert.Contains("Оглавление", text);
        Assert.Contains("Колонтитулы", text);
        Assert.Contains("Текст нижнего колонтитула", text);

        ClickButton(dialog, "Отмена");
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(existedBefore, File.Exists(settingsFile));
        window.Close();
    }

    // ------------------------------------------------------------ помощники

    private async Task<MainWindow> OpenViaRecentProjectsAsync()
    {
        RecentProjects.Add(_project);
        var window = new MainWindow();
        window.Show();

        ClickMenu(window, "Файл", "Недавние проекты", _project);
        for (var i = 0; i < 100 && !window.ViewModel.StatusText.StartsWith("Проект открыт", StringComparison.Ordinal); i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        return window;
    }

    /// <summary>Пункт меню по пути заголовков — как щелчок пользователя (команда или Click).</summary>
    private static void ClickMenu(Window window, params string[] path)
    {
        IEnumerable<MenuItem> level = window.GetLogicalDescendants().OfType<Menu>().First().Items.OfType<MenuItem>();
        MenuItem? item = null;
        foreach (var header in path)
        {
            item = level.FirstOrDefault(m => (m.Header as string)?.Replace("_", string.Empty) == header)
                   ?? throw new InvalidOperationException($"нет пункта меню «{header}»");
            level = item.Items.OfType<MenuItem>().Concat(item.ItemsSource?.OfType<MenuItem>() ?? Array.Empty<MenuItem>());
        }

        if (item!.Command is { } command)
        {
            command.Execute(item.CommandParameter);
        }
        else
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void ClickButton(Window window, params string[] captions)
    {
        var button = window.GetLogicalDescendants().OfType<Button>().First(b => captions.Contains(b.Content as string));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static string AllText(Window window) =>
        string.Join(" ", window.GetLogicalDescendants().Select(c => c switch
        {
            TextBlock t => t.Text,
            HeaderedContentControl h => h.Header as string,
            ContentControl cc => cc.Content as string,
            TextBox box => (box.Watermark ?? string.Empty) + " " + AutomationName(box),
            _ => null
        }).Where(s => !string.IsNullOrEmpty(s)));

    private static string AutomationName(Control control) => Avalonia.Automation.AutomationProperties.GetName(control) ?? string.Empty;

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DitaStudio.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория (DitaStudio.sln).");
    }
}
