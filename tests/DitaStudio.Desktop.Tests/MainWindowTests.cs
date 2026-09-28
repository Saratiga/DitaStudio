using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Model;
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
    public async Task MapContextMenu_DuplicatePasteFindReferencesDeleteFile()
    {
        var (window, vm) = await OpenAsync();
        var map = Path.Combine(_project, "guide.ditamap");
        var menu = window.GetLogicalDescendants().OfType<TreeView>().Single(t => t.Name == "MapTreeView").ContextMenu!;
        var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        foreach (var expected in new[] { "Открыть", "Свойства…", "Добавить дочерний топик…", "Дублировать", "Найти ссылки на топик",
                     "Вырезать", "Копировать", "Вставить после", "Убрать из карты", "Удалить файл…", "Развернуть всё", "Свернуть всё" })
        {
            Assert.Contains(expected, headers);
        }

        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        MapTreeNode Row(string file) => vm.Map.Tree.SelectMany(All).First(n => n.Item.TargetPath?.EndsWith(file) == true);
        DitaNode MapRoot() => vm.Documents.Tabs.Single(t => t.FullPath == map).Pane.Document.Root;
        List<string?> Hrefs() => MapRoot().ElementChildren().Where(n => n.Name == "topicref").Select(n => n.GetAttribute("href")).ToList();

        // Дублировать — копия строки сразу после неё.
        vm.Map.SelectedNode = Row("settings.dita");
        vm.Map.DuplicateCommand.Execute(null);
        Assert.Equal(2, Hrefs().Count(h => h == "reference/settings.dita"));

        // Копировать + вставить как дочерний.
        vm.Map.SelectedNode = Row("no-start.dita");
        vm.Map.CopyCommand.Execute(null);
        Assert.True(vm.Map.CanPaste);
        vm.Map.SelectedNode = Row("about.dita");
        vm.Map.PasteAsChildCommand.Execute(null);
        var about = MapRoot().ElementChildren().First(n => n.GetAttribute("href") == "concepts/about.dita");
        Assert.Equal("troubleshooting/no-start.dita", about.ElementChildren().Last().GetAttribute("href"));

        // Найти ссылки — на вкладку «Поиск».
        vm.Map.SelectedNode = Row("install.dita");
        vm.Map.FindReferencesCommand.Execute(null);
        Assert.Equal(1, vm.BottomTabIndex);
        Assert.Contains(vm.Search.Results, h => h.File.FullPath == map);

        vm.Map.CollapseAllCommand.Execute(null);
        Assert.All(vm.Map.Tree[0].Children, n => Assert.False(n.IsExpanded));
        vm.Map.ExpandAllCommand.Execute(null);
        Assert.All(vm.Map.Tree[0].Children, n => Assert.True(n.IsExpanded));

        // Удалить файл — с подтверждением; строки карты уходят, файл — с диска и из проекта.
        var file = Path.Combine(_project, "troubleshooting", "no-start.dita");
        vm.Map.SelectedNode = Row("no-start.dita");
        var deleting = vm.Map.DeleteFileCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var confirm = Assert.Single(window.OwnedWindows);
        confirm.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Да")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        await deleting;
        Assert.False(File.Exists(file));
        Assert.Null(vm.Project!.FindFile(file));
        Assert.DoesNotContain(MapRoot().DescendantsAndSelf(), n => n.GetAttribute("href")?.Contains("no-start") == true);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AuthorContextMenu_HasInsertSubmenu_InsertingAtCaret()
    {
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;
        Dispatcher.UIThread.RunJobs();
        var p = pane.Document.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.InnerText.Length > 10);
        var editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(3);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(p, vm.Current!.Author.CurrentNode);

        var menu = editor.BuildContextMenu(3);
        var insert = menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Вставить элемент");
        var sub = insert.ItemsSource!.OfType<MenuItem>().ToList();
        Assert.Contains(sub, i => i.Header as string == "Сноска");
        var inline = sub.Single(i => i.Header as string == "В строку текста").ItemsSource!.OfType<MenuItem>().ToList();
        var after = sub.Single(i => i.Header as string == "Блок после текущего").ItemsSource!.OfType<MenuItem>().ToList();
        Assert.True(after.Any(i => (i.Header as string)!.EndsWith("<note>")), string.Join(" / ", after.Select(i => i.Header)));
        Assert.DoesNotContain(after, i => (i.Header as string)!.EndsWith("<term>"));

        var before = p.InnerText;
        inline.Single(i => (i.Header as string)!.EndsWith("<term>")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        pane.AuthorEditor.FlushPendingEdits();
        var term = Assert.Single(p.ElementChildren(), c => c.Name == "term");
        Assert.Equal(3, p.Children.TakeWhile(c => !ReferenceEquals(c, term)).Sum(c => c.InnerText.Length));
        Assert.NotEqual(before, p.InnerText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TableCellMenu_InsertsRowsAndColumns()
    {
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "reference", "settings.dita"))!;
        Dispatcher.UIThread.RunJobs();
        var table = pane.Document.Root.DescendantsAndSelf().First(n => n.Name is "table" or "simpletable" or "properties");
        var cell = table.DescendantsAndSelf().First(n => n.Name is "entry" or "stentry" or "propvalue" && n.InnerText.Length > 0);
        var editor = pane.AuthorEditor.EditorFor(cell)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();

        var menu = editor.BuildContextMenu(0).Items.OfType<MenuItem>().ToList();
        var below = menu.Single(i => i.Header as string == "Вставить строку ниже");
        Assert.Contains(menu, i => i.Header as string == "Удалить столбец");
        Assert.Contains(menu, i => i.Header as string == "Вставить элемент");

        int Rows() => table.DescendantsAndSelf().Count(n => n.Name is "row" or "strow" or "property");
        var rowsBefore = Rows();
        var editorsBefore = pane.AuthorEditor.Editors.Count;
        below.Command!.Execute(below.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(rowsBefore + 1, Rows());
        Assert.True(pane.AuthorEditor.Editors.Count > editorsBefore, "новая строка появилась в «Авторе»");
        Assert.True(pane.Document.IsDirty);
        Assert.Contains("выполнено", vm.StatusText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Toolbar_HasEditingButtons_UnderlineWrapsSelection()
    {
        var (window, vm) = await OpenAsync();
        var toolbar = window.GetLogicalDescendants().OfType<WrapPanel>().Single(p => p.Name == "MainToolbar");
        var tips = toolbar.Children.OfType<Button>().Select(b => ToolTip.GetTip(b) as string).ToList();
        foreach (var expected in new[] { "Подчёркнутый (Ctrl+U)", "Перекрёстная ссылка…", "Изображение…", "Сноска у курсора",
                     "Нумерованный список", "Маркированный список", "Таблица…", "Вставить строку ниже", "Удалить столбец", "Разделить объединённую ячейку" })
        {
            Assert.Contains(expected, tips);
        }

        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;
        Dispatcher.UIThread.RunJobs();
        var p = pane.Document.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.InnerText.StartsWith("Проект"));
        var editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        editor.Select(0, "Проект".Length);
        toolbar.Children.OfType<Button>().Single(b => ToolTip.GetTip(b) as string == "Подчёркнутый (Ctrl+U)").Command!.Execute(null);
        pane.AuthorEditor.FlushPendingEdits();
        Assert.StartsWith("<p><u>Проект</u>", DitaStudio.Core.Model.XmlSerializer.ToXml(p));
        window.Close();
    }

    [AvaloniaFact]
    public async Task TitleContextMenu_TogglesUnnumbered_WithBadge()
    {
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;
        Dispatcher.UIThread.RunJobs();
        var title = pane.Document.Root.FirstElement("title")!;
        pane.AuthorEditor.EditorFor(title)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();

        var item = pane.AuthorEditor.EditorFor(title)!.BuildContextMenu(0).Items.OfType<MenuItem>()
            .Single(i => i.Header as string == "Заголовок без номера (не в оглавлении)");
        Assert.False(item.IsChecked);
        item.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("nonumber", title.GetAttribute("outputclass"));
        Assert.Contains(pane.AuthorEditor.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "без номера · не в оглавлении");
        Assert.True(pane.AuthorEditor.EditorFor(title)!.BuildContextMenu(0).Items.OfType<MenuItem>()
            .Single(i => i.Header as string == "Заголовок без номера (не в оглавлении)").IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MapCheckbox_ExcludesTopicFromPublication()
    {
        var (window, vm) = await OpenAsync();
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        MapTreeNode Row() => vm.Map.Tree.SelectMany(All).First(n => n.Item.TargetPath?.EndsWith("settings.dita") == true);
        Assert.True(Row().IsPublished);
        Assert.False(vm.Map.Tree[0].CanExclude);

        window.GetLogicalDescendants().OfType<TabControl>().Single(t => t.Name == "LeftTabs").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var boxes = window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.DataContext is MapTreeNode).ToList();
        var box = boxes.Single(c => ReferenceEquals(c.DataContext, Row()));
        box.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var mapDoc = vm.Documents.Tabs.Single(t => t.FullPath.EndsWith("guide.ditamap")).Pane.Document;
        var topicref = mapDoc.Root.DescendantsAndSelf().First(n => n.GetAttribute("href") == "reference/settings.dita");
        Assert.Equal("resource-only", topicref.GetAttribute("processing-role"));
        Assert.False(Row().IsPublished);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("excluded") && t.Text == Row().Title);
        Save(window, "map-excluded-topic");

        vm.Map.TogglePublishedCommand.Execute(Row());
        Assert.Null(topicref.GetAttribute("processing-role"));
        Assert.True(Row().IsPublished);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AlignmentButtons_SetClassOrCellAlign()
    {
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "reference", "settings.dita"))!;
        Dispatcher.UIThread.RunJobs();
        var toolbar = window.GetLogicalDescendants().OfType<WrapPanel>().Single(p => p.Name == "MainToolbar");
        Button Tool(string tip) => toolbar.Children.OfType<Button>().Single(b => ToolTip.GetTip(b) as string == tip);

        var p = pane.Document.Root.DescendantsAndSelf().First(n => n.Name is "p" or "shortdesc" && n.InnerText.Length > 0);
        pane.AuthorEditor.EditorFor(p)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        Tool("По центру").Command!.Execute(Tool("По центру").CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("align-center", p.GetAttribute("outputclass"));
        var editor = pane.AuthorEditor.EditorFor(p)!;
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, editor.HorizontalAlignment);

        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        var format = editor.BuildContextMenu(0).Items.OfType<MenuItem>().Single(i => i.Header as string == "Оформление");
        var align = format.ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == "Выравнивание").ItemsSource!.OfType<MenuItem>().ToList();
        Assert.True(align.Single(i => i.Header as string == "По центру").IsChecked);

        Tool("По левому краю").Command!.Execute(Tool("По левому краю").CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(p.GetAttribute("outputclass"));

        var entry = pane.Document.Root.DescendantsAndSelf().First(n => n.Name == "entry" && n.InnerText.Length > 0);
        pane.AuthorEditor.EditorFor(entry)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(entry, vm.Current!.Author.CurrentNode);
        Tool("По правому краю").Command!.Execute(Tool("По правому краю").CommandParameter);
        Assert.Equal("right", entry.GetAttribute("align"));
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, pane.AuthorEditor.EditorFor(entry)!.HorizontalAlignment);
        Save(window, "author-alignment");
        window.Close();
    }

    [AvaloniaFact]
    public async Task ColorPalette_ColorsSelectionAndWholeBlock()
    {
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;
        Dispatcher.UIThread.RunJobs();
        var p = pane.Document.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.InnerText.StartsWith("Проект"));
        var editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();

        var colorButton = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ColorButton");
        var palette = ((Flyout)colorButton.Flyout!).Content as WrapPanel;
        var red = palette!.Children.OfType<Button>().Single(b => ToolTip.GetTip(b) as string == "Красный");
        Assert.Contains(palette.Children.OfType<Button>(), b => b.Content as string == "Без цвета");

        editor.Select(0, "Проект".Length);
        red.Command!.Execute(red.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        pane.AuthorEditor.FlushPendingEdits();
        Assert.StartsWith("<p><ph outputclass=\"color-red\">Проект</ph>", DitaStudio.Core.Model.XmlSerializer.ToXml(p));
        editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        editor.Select("Проект состоит".Length + 1, "из".Length);
        vm.Insert.SetFontSizeCommand.Execute("20");
        Dispatcher.UIThread.RunJobs();
        pane.AuthorEditor.FlushPendingEdits();
        Assert.Contains("<ph outputclass=\"size-20\">из</ph>", DitaStudio.Core.Model.XmlSerializer.ToXml(p));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Save(window, "author-color-size");
        editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        editor.Select("Проект состоит".Length + 1, "из".Length);
        vm.Insert.SetFontSizeCommand.Execute("Обычный");
        Dispatcher.UIThread.RunJobs();

        editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        editor.Select(0, editor.Document.TextLength);
        vm.Insert.SetTextColorCommand.Execute("color-blue");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("color-blue", p.GetAttribute("outputclass"));
        Assert.DoesNotContain(p.ElementChildren(), c => c.Name == "ph");
        Assert.Equal(Avalonia.Media.Color.Parse("#1F5FBF"), ((Avalonia.Media.SolidColorBrush)pane.AuthorEditor.EditorFor(p)!.Foreground!).Color);

        var menu = pane.AuthorEditor.EditorFor(p)!.BuildContextMenu(0).Items.OfType<MenuItem>().Single(i => i.Header as string == "Оформление");
        Assert.Contains(menu.ItemsSource!.OfType<MenuItem>(), i => i.Header as string == "Цвет текста");
        window.Close();
    }

    [AvaloniaFact]
    public async Task NumberedParagraphButton_TogglesClassAndMark()
    {
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;
        Dispatcher.UIThread.RunJobs();
        var p = pane.Document.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.InnerText.StartsWith("Проект"));
        pane.AuthorEditor.EditorFor(p)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();

        var toolbar = window.GetLogicalDescendants().OfType<WrapPanel>().Single(w => w.Name == "MainToolbar");
        var button = toolbar.Children.OfType<Button>().Single(b => ToolTip.GetTip(b) as string == "Нумерованный абзац: номер по заголовкам (2.3.1)");
        button.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("numbered", p.GetAttribute("outputclass"));
        Assert.Contains(pane.AuthorEditor.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "№");

        var editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        var format = editor.BuildContextMenu(0).Items.OfType<MenuItem>().Single(i => i.Header as string == "Оформление");
        Assert.True(format.ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == "Нумерованный абзац (2.3.1)").IsChecked);

        button.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(p.GetAttribute("outputclass"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task PagePlacementMenu_PutsBlockOnSeparatePage()
    {
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;
        Dispatcher.UIThread.RunJobs();
        var p = pane.Document.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.InnerText.StartsWith("Проект"));
        var editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();

        MenuItem Placement() => editor.BuildContextMenu(0).Items.OfType<MenuItem>().Single(i => i.Header as string == "Оформление")
            .ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == "Положение на листе (PDF, DOCX)");
        var placement = Placement();
        Assert.True(placement.IsEnabled);
        Assert.True(placement.ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == "Обычное (в тексте)").IsChecked);
        var bottomRight = placement.ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == "Внизу справа");
        bottomRight.Command!.Execute(bottomRight.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("place-bottom-right", p.GetAttribute("outputclass"));
        Assert.Contains(pane.AuthorEditor.GetVisualDescendants().OfType<TextBlock>(), t => t.Tag as string == "placement-mark" && t.Text!.Contains("внизу справа"));

        editor = pane.AuthorEditor.EditorFor(p)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        placement = Placement();
        Assert.True(placement.ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == "Внизу справа").IsChecked);
        var normal = placement.ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == "Обычное (в тексте)");
        normal.Command!.Execute(normal.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(p.GetAttribute("outputclass"));
        Assert.DoesNotContain(pane.AuthorEditor.GetVisualDescendants().OfType<TextBlock>(), t => t.Tag as string == "placement-mark");
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
