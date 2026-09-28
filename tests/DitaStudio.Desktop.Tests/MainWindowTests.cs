using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Desktop.Views;
using DitaStudio.Presentation.ViewModels;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Главное окно Avalonia-версии на настоящем проекте: копия samples/GuideSample открывается
/// через общие ViewModel'и так же, как из меню, дальше — документ, выбор элемента, вставка из
/// палитры, сохранение. Снимки окна и диалогов — в папке screenshots вывода теста.
/// </summary>
public sealed class MainWindowTests : IDisposable
{
    private readonly string _project;
    private readonly string? _recentBackup;
    private readonly string _recentPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "recent-projects.txt");

    public MainWindowTests()
    {
        // Открытие проекта записывает его в «Недавние» — возвращаем пользовательский список на место.
        _recentBackup = File.Exists(_recentPath) ? File.ReadAllText(_recentPath) : null;
        _project = Path.Combine(Path.GetTempPath(), "DitaStudioDesktopTests", Guid.NewGuid().ToString("N"), "GuideSample");
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

    [AvaloniaFact]
    public async Task OpenProject_FillsTreesKeysAndStatus()
    {
        var (window, vm) = await OpenAsync();

        Assert.StartsWith("Проект открыт:", vm.StatusText);
        Assert.Equal("DITA Studio — GuideSample", window.Title);
        var root = Assert.Single(vm.ProjectPanel.Tree);
        Assert.Equal("GuideSample", root.Name);
        Assert.Contains(root.Children, c => c.File?.FileName == "guide.ditamap");
        Assert.NotEmpty(vm.ProjectPanel.Keys);
        Assert.NotNull(vm.Map.SelectedMap);
        Assert.NotEmpty(Assert.Single(vm.Map.Tree).Children);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OpenDocument_SelectElement_InsertFromPalette_Save()
    {
        var (window, vm) = await OpenAsync();
        var file = Path.Combine(_project, "concepts", "about.dita");

        var pane = Assert.IsType<DocumentView>(vm.OpenDocument!(file));
        Assert.Single(vm.Documents.Tabs);
        Assert.NotEmpty(vm.SidePanels.Outline);

        int Notes() => pane.Document.Root.DescendantsAndSelf().Count(n => n.Name == "note");
        var notesBefore = Notes();
        var paragraph = pane.Document.Root.DescendantsAndSelf().First(n => n.Name == "p");
        Assert.True(pane.FocusNode(paragraph));
        Assert.Same(paragraph, vm.Current!.Author.CurrentNode);
        Assert.StartsWith("<p>", vm.SidePanels.AttributeContext);
        Assert.Contains(vm.SidePanels.Palette, e => e.Name == "note");

        vm.SidePanels.SelectedPaletteEntry = vm.SidePanels.Palette.First(e => e.Name == "note");
        vm.SidePanels.InsertSelectedPaletteEntryCommand.Execute(null);
        Assert.True(pane.IsDirty);
        Assert.StartsWith("• ", vm.Documents.Tabs[0].Title);
        Assert.Equal(notesBefore + 1, Notes());

        await vm.Documents.SaveCurrentCommand.ExecuteAsync(null);
        Assert.False(pane.IsDirty);
        Assert.Equal(notesBefore + 1, System.Text.RegularExpressions.Regex.Matches(await File.ReadAllTextAsync(file), "<note[ >/]").Count);

        pane.PerformUndo();
        Assert.Equal(notesBefore, Notes());
        window.Close();
    }

    [AvaloniaFact]
    public async Task SourceMode_EditsGoBackToModel()
    {
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;

        pane.Mode = DitaStudio.Presentation.Services.EditorMode.Source;
        Dispatcher.UIThread.RunJobs();
        var editor = window.GetVisualDescendants().OfType<XmlSourceEditor>().Single();
        Assert.Contains("<concept", editor.Text);

        editor.Text = editor.Text.Replace("О продукте", "О продукте (правка)");
        Assert.True(pane.IsDirty);
        Assert.Null(pane.CommitPendingEdits());
        Assert.Equal("О продукте (правка)", pane.Document.Title);

        editor.Text = "<concept><title>незакрытый";
        Assert.NotNull(pane.CommitPendingEdits());
        window.Close();
    }

    [AvaloniaFact]
    public async Task CreatedTopic_GoesIntoMap_AfterSelectedNode()
    {
        var (window, vm) = await OpenAsync();
        var map = Path.Combine(_project, "guide.ditamap");
        Assert.Equal(map, vm.Map.SelectedMap!.FullPath);

        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        vm.Map.SelectedNode = vm.Map.Tree.SelectMany(All).First(n => n.Item.TargetPath?.EndsWith("settings.dita") == true);

        var path = Path.Combine(_project, "tasks", "new-topic.dita");
        var topic = DitaStudio.Core.Templates.DocumentTemplates.Create("task", "Новая задача");
        topic.Save(path);
        vm.Project!.AddFile(path);

        Assert.True(vm.Map.AddCreatedTopic(path));
        var mapPane = vm.Documents.Tabs.Single(t => t.FullPath == map).Pane;
        var refs = mapPane.Document.Root.ElementChildren().Where(n => n.Name == "topicref").Select(n => n.GetAttribute("href")).ToList();
        Assert.Equal(refs.IndexOf("reference/settings.dita") + 1, refs.IndexOf("tasks/new-topic.dita"));
        Assert.True(mapPane.Document.IsDirty);
        Assert.EndsWith("new-topic.dita", vm.Map.SelectedNode?.Item.TargetPath);
        window.Close();
    }

    [AvaloniaFact]
    public void TopMenuItems_FitTheirText()
    {
        // Полоса меню Fluent была фиксированной высоты — пункты сжимались, текст срезался снизу.
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var items = window.GetLogicalDescendants().OfType<Menu>().First().Items.OfType<MenuItem>().ToList();
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            // DesiredSize уже урезан доступной высотой — естественную высоту текста меряем
            // отдельным TextBlock с тем же шрифтом без ограничений.
            var text = item.GetVisualDescendants().OfType<TextBlock>().First();
            var probe = new TextBlock
            {
                Text = text.Text,
                FontFamily = text.FontFamily,
                FontSize = text.FontSize,
                FontWeight = text.FontWeight,
            };
            probe.Measure(Size.Infinity);
            Assert.True(text.Bounds.Height + 0.5 >= probe.DesiredSize.Height,
                $"«{text.Text}»: тексту выделено {text.Bounds.Height:0.#}, нужно {probe.DesiredSize.Height:0.#}");
        }

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Screenshots_MainWindowWithProject(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var (window, vm) = await OpenAsync();
            var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;
            pane.FocusNode(pane.Document.Root.DescendantsAndSelf().First(n => n.Name == "p"));
            vm.Validation.ValidateProjectCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Save(window, $"main-window-{theme}");

            pane.Mode = DitaStudio.Presentation.Services.EditorMode.Source;
            Dispatcher.UIThread.RunJobs();
            Save(window, $"source-editor-{theme}");
            pane.Mode = DitaStudio.Presentation.Services.EditorMode.Author;

            _ = vm.Dialogs.AboutAsync();
            Dispatcher.UIThread.RunJobs();
            var about = Assert.Single(window.OwnedWindows);
            Save(about, $"dialog-about-{theme}");
            about.Close();

            _ = vm.Dialogs.InsertTableAsync();
            Dispatcher.UIThread.RunJobs();
            var table = Assert.Single(window.OwnedWindows);
            Save(table, $"dialog-table-{theme}");
            table.Close();

            window.Close();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private async Task<(MainWindow Window, MainViewModel Vm)> OpenAsync()
    {
        var window = new MainWindow();
        window.Show();
        await window.ViewModel.ProjectPanel.LoadProjectAsync(_project);
        Dispatcher.UIThread.RunJobs();
        return (window, window.ViewModel);
    }

    private static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

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
