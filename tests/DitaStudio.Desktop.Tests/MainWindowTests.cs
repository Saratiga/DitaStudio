using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Editing;
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

    /// <summary>Строки дерева карты, реально стоящие в окне (не остатки прежнего дерева).</summary>
    private static List<TreeViewItem> MapRows(Window window)
    {
        window.GetLogicalDescendants().OfType<TabControl>().First(t => t.Name == "LeftTabs").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var tree = window.GetLogicalDescendants().OfType<TreeView>().First(t => t.Name == "MapTreeView");
        return tree.GetVisualDescendants().OfType<TreeViewItem>().Where(i => i.IsAttachedToVisualTree() && i.DataContext is MapTreeNode).ToList();
    }

    private static Point Center(Visual visual, Window window) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point point, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task MapTree_DoubleClickOnParentTopic_OpensIt_AndKeepsExpansion()
    {
        var (window, vm) = await OpenAsync();
        window.Width = 1200;
        window.Height = 900;
        var rows = MapRows(window);
        var parent = rows.First(r => r.DataContext is MapTreeNode { Children.Count: > 0, Item.TargetPath: not null });
        var node = (MapTreeNode)parent.DataContext!;
        Assert.True(node.IsExpanded);
        var tabsBefore = vm.Documents.Tabs.Count;

        // Точка — на самой строке заголовка (у раскрытого родителя высота строки включает детей).
        var header = new Point(90, 10);
        Click(window, parent.TranslatePoint(header, window)!.Value, times: 2);

        Assert.Equal(tabsBefore + 1, vm.Documents.Tabs.Count);
        Assert.EndsWith(Path.GetFileName(node.Item.TargetPath!), vm.Documents.SelectedTab!.FullPath);
        Assert.True(node.IsExpanded, "двойной щелчок открывает топик и не сворачивает ветку");
        window.Close();
    }

    [AvaloniaFact]
    public async Task MapTree_PublishCheckbox_ChangesOnlyItself()
    {
        var (window, vm) = await OpenAsync();
        window.Width = 1200;
        window.Height = 900;
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        var branch = vm.Map.Tree.SelectMany(All).First(n => n.Children.Count > 0 && !n.IsRoot);
        branch.IsExpanded = false; // ветка свёрнута пользователем
        var leaf = vm.Map.Tree.SelectMany(All).First(n => n.Children.Count == 0 && n.CanExclude && n.Item.TargetPath is not null
                                                         && !branch.Children.Contains(n));
        vm.OpenDocument!(Path.Combine(_project, "reference", "settings.dita")); // рабочий документ, от которого нельзя уводить
        var rows = MapRows(window);
        var row = rows.First(r => ReferenceEquals(r.DataContext, leaf));
        var box = row.GetVisualDescendants().OfType<CheckBox>().First();
        vm.Map.SelectedNode = branch;
        var selectedTabBefore = vm.Documents.SelectedTab;
        var expandedBefore = vm.Map.Tree.SelectMany(All).Select(n => (n.Item.Node, n.IsExpanded)).ToList();
        var tabsBefore = vm.Documents.Tabs.Count;
        var publishedBefore = leaf.IsPublished;

        // Два быстрых щелчка по флажку: переключается только он.
        Click(window, Center(box, window), times: 2);

        var leafAfter = vm.Map.Tree.SelectMany(All).First(n => ReferenceEquals(n.Item.Node, leaf.Item.Node));
        Assert.Equal(publishedBefore, leafAfter.IsPublished);
        Assert.Same(selectedTabBefore, vm.Documents.SelectedTab);
        Assert.NotNull(vm.Map.SelectedNode); // выделение не теряется при перестроении дерева
        Assert.Equal(expandedBefore, vm.Map.Tree.SelectMany(All).Select(n => (n.Item.Node, n.IsExpanded)).ToList());
        Assert.All(vm.Map.Tree.SelectMany(All).Where(n => ReferenceEquals(n.Item.Node, branch.Item.Node)), n => Assert.False(n.IsExpanded));
        Assert.True(vm.Documents.Tabs.Count <= tabsBefore + 1);

        // Одиночный щелчок — переключение, и снова только оно.
        rows = MapRows(window);
        row = rows.First(r => r.DataContext is MapTreeNode m && ReferenceEquals(m.Item.Node, leaf.Item.Node));
        Click(window, Center(row.GetVisualDescendants().OfType<CheckBox>().First(), window));
        Assert.False(vm.Map.Tree.SelectMany(All).First(n => ReferenceEquals(n.Item.Node, leaf.Item.Node)).IsPublished);
        Assert.Same(selectedTabBefore, vm.Documents.SelectedTab);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MapMenu_AddedSection_IsSelected_AndFileCommandsAreUnavailable()
    {
        var (window, vm) = await OpenAsync();
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        var topic = vm.Map.Tree.SelectMany(All).First(n => n.Item.TargetPath?.EndsWith("settings.dita") == true);
        vm.Map.SelectedNode = topic;
        Assert.True(vm.Map.SelectedHasFile);
        Assert.True(vm.Map.DeleteFileCommand.CanExecute(null));
        Assert.True(vm.Map.FindReferencesCommand.CanExecute(null));

        vm.Map.AddTopicheadCommand.Execute(null);

        // Новая строка выделена: следующая команда меню («Убрать из карты», «Дублировать») действует на неё.
        Assert.Equal("topichead", vm.Map.SelectedNode?.Item.Node.Name);

        // У раздела нет файла: команды про файл недоступны, а не «молча ничего не делают».
        Assert.False(vm.Map.SelectedHasFile);
        Assert.False(vm.Map.FindReferencesCommand.CanExecute(null));
        Assert.False(vm.Map.RenameFileCommand.CanExecute(null));
        Assert.False(vm.Map.DeleteFileCommand.CanExecute(null));
        Assert.True(vm.Map.DeleteCommand.CanExecute(null), "убрать раздел из карты можно");

        // Если такую команду всё же запустили (не из меню) — сообщение, а не тишина.
        vm.StatusText = string.Empty;
        await vm.Map.DeleteFileCommand.ExecuteAsync(null);
        Assert.Contains("нет файла", vm.StatusText);

        // Корень карты не переставляется и не удаляется.
        vm.Map.SelectedNode = vm.Map.Tree[0];
        Assert.False(vm.Map.DeleteCommand.CanExecute(null));
        Assert.False(vm.Map.MoveUpCommand.CanExecute(null));
        Assert.False(vm.Map.CutCommand.CanExecute(null));
        Assert.False(vm.Map.DuplicateCommand.CanExecute(null));

        // Без выбранной строки то же самое.
        vm.Map.SelectedNode = null;
        Assert.False(vm.Map.DeleteCommand.CanExecute(null));
        Assert.False(vm.Map.DeleteFileCommand.CanExecute(null));
        window.Close();
    }

    /// <summary>Сценарий из замечания: «+ Раздел», правый щелчок по «Новый раздел», пункты контекстного меню.</summary>
    [AvaloniaFact]
    public async Task MapMenu_NewSection_RightClick_ShowsOnlyApplicableItems_AndRemovesFromMap()
    {
        var (window, vm) = await OpenAsync();
        window.Width = 1200;
        window.Height = 900;
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        int Heads() => vm.Map.Tree.SelectMany(All).Count(n => n.Item.Node.Name == "topichead" && n.Title == "Новый раздел");
        var before = Heads();

        MapRows(window); // вкладка «Карта»
        var add = window.GetLogicalDescendants().OfType<Button>().First(x => x.Content as string == "+ Раздел");
        Click(window, Center(add, window)); // настоящий щелчок по кнопке «+ Раздел»
        Assert.Equal(before + 1, Heads());

        var menu = window.GetLogicalDescendants().OfType<TreeView>().First(t => t.Name == "MapTreeView").ContextMenu!;
        bool Visible(string header) => menu.Items.OfType<MenuItem>().First(i => i.Header as string == header).IsVisible;
        IEnumerable<string> VisibleHeaders() => menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).Select(i => (string)i.Header!);

        var row = MapRows(window).First(r => (r.DataContext as MapTreeNode)?.Title == "Новый раздел");
        var point = row.TranslatePoint(new Point(100, 10), window)!.Value;
        window.MouseDown(point, Avalonia.Input.MouseButton.Right);
        window.MouseUp(point, Avalonia.Input.MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Новый раздел", vm.Map.SelectedNode?.Title);
        Assert.False(Visible("Удалить файл…"), "у раздела нет файла — пункта «Удалить файл…» нет");
        Assert.False(Visible("Найти ссылки на топик"));
        Assert.False(Visible("Переименовать / переместить файл…"));
        Assert.All(new[] { "Убрать из карты", "Дублировать", "Вырезать", "Копировать", "Добавить раздел" }, h => Assert.True(Visible(h), h));

        // «Убрать из карты» — с подтверждением.
        var remove = menu.Items.OfType<MenuItem>().First(i => i.Header as string == "Убрать из карты");
        remove.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var confirm = Assert.Single(window.OwnedWindows);
        confirm.GetLogicalDescendants().OfType<Button>().First(x => x.Content as string == "Да")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, Heads());

        // У строки-топика пункты про файл на месте.
        var topicRow = MapRows(window).First(r => (r.DataContext as MapTreeNode)?.Item.TargetPath?.EndsWith("settings.dita") == true);
        var topicPoint = topicRow.TranslatePoint(new Point(100, 10), window)!.Value;
        window.MouseDown(topicPoint, Avalonia.Input.MouseButton.Right);
        window.MouseUp(topicPoint, Avalonia.Input.MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.All(new[] { "Удалить файл…", "Найти ссылки на топик", "Переименовать / переместить файл…" }, h => Assert.True(Visible(h), h));
        window.Close();
    }

    [AvaloniaFact]
    public async Task MapDrag_IntoParent_MakesChild_KeepsSelection_IsOneUndoStep_AndRejectsOwnBranch()
    {
        var (window, vm) = await OpenAsync();
        var map = Path.Combine(_project, "guide.ditamap");
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        MapTreeNode Row(string file) => vm.Map.Tree.SelectMany(All).First(n => n.Item.TargetPath?.EndsWith(file) == true);
        DitaNode MapRoot() => vm.Documents.Tabs.Single(t => t.FullPath == map).Pane.Document.Root;
        DitaNode Node(string file) => MapRoot().DescendantsAndSelf().First(n => n.GetAttribute("href")?.EndsWith(file) == true);

        vm.OpenDocument!(map); // правки карты идут в её вкладку
        var settings = Row("settings.dita");
        var about = Row("about.dita");
        Assert.NotSame(Node("settings.dita").Parent, Node("about.dita"));

        // Перетащить на середину строки — стать дочерним.
        Assert.True(MapViewModel.CanDrop(settings, about, DropPosition.Child, out _));
        vm.Map.MoveByDrag(settings, about, DropPosition.Child);
        Assert.Same(Node("about.dita"), Node("settings.dita").Parent);
        Assert.Same(Node("settings.dita"), Node("about.dita").ElementChildren().Last());
        Assert.Equal("settings.dita", Path.GetFileName(vm.Map.SelectedNode?.Item.TargetPath)); // перенесённая строка остаётся выделенной
        Assert.True(Row("about.dita").IsExpanded, "цель раскрыта, чтобы перенесённая строка была видна");

        // Один шаг отмены возвращает всё как было.
        vm.Insert.UndoCommand.Execute(null); // Ctrl+Alt+Z: дерево карты обновляется вместе с документом
        Assert.NotSame(Node("about.dita"), Node("settings.dita").Parent);
        Assert.Same(Node("settings.dita"), Row("settings.dita").Item.Node);

        // Перед и после — на уровень цели, из другой ветки.
        vm.Map.MoveByDrag(Row("settings.dita"), Row("install.dita"), DropPosition.Before);
        Assert.Same(Node("install.dita").Parent, Node("settings.dita").Parent);
        Assert.True(Node("settings.dita").IndexInParent < Node("install.dita").IndexInParent);

        // В собственную ветку нельзя: сообщение, карта не менялась.
        var parentRow = vm.Map.Tree.SelectMany(All).First(n => n.Children.Count > 0 && n.Item.TargetPath is not null && !n.IsRoot);
        var child = parentRow.Children[0];
        Assert.False(MapViewModel.CanDrop(parentRow, child, DropPosition.Child, out var reason));
        Assert.Contains("собственную ветку", reason);
        var before = MapRoot().ToString();
        vm.Map.MoveByDrag(parentRow, child, DropPosition.Child);
        Assert.Equal(before, MapRoot().ToString());
        Assert.Contains("собственную ветку", vm.StatusText);
        window.Close();
    }

    /// <summary>Строка вложенной карты (mapref) правится в её собственном файле, а не в открытой карте.</summary>
    [AvaloniaFact]
    public async Task MapNested_EditsGoToTheNestedMapFile()
    {
        File.WriteAllText(Path.Combine(_project, "sub.ditamap"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<map><title>Вложенная</title>" +
            "<topicref href=\"tasks/install.dita\"/><topichead><topicmeta><navtitle>Внутри</navtitle></topicmeta></topichead></map>");
        File.WriteAllText(Path.Combine(_project, "outer.ditamap"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<map><title>Внешняя</title><topicref href=\"concepts/about.dita\"/><mapref href=\"sub.ditamap\"/></map>");
        var (window, vm) = await OpenAsync();
        var outer = Path.Combine(_project, "outer.ditamap");
        var sub = Path.Combine(_project, "sub.ditamap");
        vm.Map.SelectedMap = vm.Map.Maps.First(m => m.FullPath == outer);
        Dispatcher.UIThread.RunJobs();
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        var innerHead = vm.Map.Tree.SelectMany(All).First(n => n.Title == "Внутри");
        Assert.Equal(sub, Path.GetFullPath(innerHead.Item.MapPath));

        // Убрать вложенный раздел: он исчезает из вложенной карты, внешняя карта не тронута.
        vm.Map.SelectedNode = innerHead;
        var deleting = vm.Map.DeleteCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        window.OwnedWindows[0].GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Да")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        await deleting;

        var subTab = vm.Documents.Tabs.Single(t => Path.GetFullPath(t.FullPath) == sub);
        Assert.DoesNotContain(subTab.Pane.Document.Root.DescendantsAndSelf(), n => n.Name == "topichead");
        Assert.True(subTab.Pane.Document.IsDirty, "правка записана во вкладку вложенной карты");
        Assert.DoesNotContain(vm.Map.Tree.SelectMany(All), n => n.Title == "Внутри");
        Assert.DoesNotContain(vm.Documents.Tabs, t => Path.GetFullPath(t.FullPath) == outer && t.Pane.Document.IsDirty);

        // Добавить раздел, выделив строку вложенной карты: он появляется во вложенной карте.
        vm.Map.SelectedNode = vm.Map.Tree.SelectMany(All).First(n => n.Item.TargetPath?.EndsWith("install.dita") == true);
        vm.Map.AddTopicheadCommand.Execute(null);
        Assert.Contains(subTab.Pane.Document.Root.ElementChildren(), n => n.Name == "topichead");
        Assert.Equal("topichead", vm.Map.SelectedNode?.Item.Node.Name);
        Assert.Equal(sub, Path.GetFullPath(vm.Map.SelectedNode!.Item.MapPath));
        window.Close();
    }

    /// <summary>Длинная карта: правка (перенос, флажок) не сдвигает полосу прокрутки — список не «убегает» за строкой.</summary>
    [AvaloniaFact]
    public async Task MapTree_LongMap_EditKeepsScrollPosition()
    {
        var rows = string.Concat(Enumerable.Range(1, 90).Select(i => $"<topicref href=\"concepts/about.dita\" navtitle=\"Строка {i}\"/>"));
        File.WriteAllText(Path.Combine(_project, "long.ditamap"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<map><title>Длинная</title>" + rows + "</map>");
        var (window, vm) = await OpenAsync();
        window.Width = 1200;
        window.Height = 700;
        vm.Map.SelectedMap = vm.Map.Maps.First(m => Path.GetFileName(m.FullPath) == "long.ditamap");
        MapRows(window);
        Dispatcher.UIThread.RunJobs();
        var scroll = window.GetLogicalDescendants().OfType<TreeView>().First(t => t.Name == "MapTreeView")
            .GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height * 2, "карта длиннее окна");

        scroll.Offset = new Vector(0, 1500);
        Dispatcher.UIThread.RunJobs();
        var offset = scroll.Offset.Y;
        Assert.InRange(offset, 1400, 1600);

        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        var last = vm.Map.Tree.SelectMany(All).Where(n => !n.IsRoot).ToList();
        // Строка из нижней части списка поднимается высоко вверх, за край видимой области.
        vm.Map.MoveByDrag(last[80], last[10], Core.Editing.DropPosition.Before);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.InRange(scroll.Offset.Y, offset - 2, offset + 2);
        window.Close();
    }

    /// <summary>
    /// В1: матрица «команда меню карты × тип строки». Ни одна команда не бросает исключение, не даёт
    /// карту с новыми ошибками структуры и не отказывает молча — если карта не изменилась, в строке
    /// состояния причина. Типы строк: топик с потомком, раздел без файла, topicgroup, keydef с href и
    /// без, mapref, строки bookmap (booktitle, frontmatter, chapter, part, appendix, backmatter), корень.
    /// </summary>
    [AvaloniaFact]
    public async Task MapCommands_Matrix_NeverThrowNeverBreakStructure_NeverFailSilently()
    {
        const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
        File.WriteAllText(Path.Combine(_project, "sub.ditamap"),
            Head + "<map><title>Вложенная</title><topicref href=\"tasks/first-run.dita\"/></map>");
        File.WriteAllText(Path.Combine(_project, "matrix.ditamap"), Head +
            "<map><title>Матрица</title>" +
            "<topicref href=\"concepts/about.dita\"><topicref href=\"reference/settings.dita\"/></topicref>" +
            "<topichead navtitle=\"Группа\"><topicref href=\"tasks/install.dita\"/></topichead>" +
            "<topicgroup><topicref href=\"concepts/about.dita\"/></topicgroup>" +
            "<keydef keys=\"k1\" href=\"concepts/about.dita\"/><keydef keys=\"k2\"/>" +
            "<mapref href=\"sub.ditamap\"/><topicref navtitle=\"Без файла\"/></map>");
        File.WriteAllText(Path.Combine(_project, "matrix.bookmap"), Head +
            "<bookmap><booktitle><mainbooktitle>Книга</mainbooktitle></booktitle>" +
            "<frontmatter><preface href=\"concepts/about.dita\"/></frontmatter>" +
            "<chapter href=\"concepts/about.dita\"><topicref href=\"reference/settings.dita\"/></chapter>" +
            "<part><chapter href=\"tasks/install.dita\"/></part>" +
            "<appendices><appendix href=\"tasks/first-run.dita\"/></appendices><backmatter/></bookmap>");

        var (window, vm) = await OpenAsync();
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        var validator = new DitaStudio.Core.Validation.DitaValidator { CheckStyleRules = false };
        var failures = new System.Text.StringBuilder();
        var mutations = 0;
        var rowsChecked = 0;

        foreach (var mapFile in new[] { "matrix.ditamap", "matrix.bookmap" })
        {
            var full = Path.Combine(_project, mapFile);
            vm.Map.SelectedMap = vm.Map.Maps.First(m => m.FullPath == full);
            vm.OpenDocument!(full);
            vm.OpenDocument!(Path.Combine(_project, "sub.ditamap"));
            vm.OpenDocument!(full);
            var docs = vm.Documents.Tabs.Where(t => t.Pane is not null && t.FullPath is not null && t.FullPath.EndsWith("map"))
                .Select(t => t.Pane!.Document).Distinct().ToList();
            var originals = docs.ToDictionary(d => d, d => d.ToXmlString());
            int Errors() => docs.Sum(d => validator.Validate(d).Count(i => i.Severity == DitaStudio.Core.Validation.IssueSeverity.Error));
            void Restore()
            {
                foreach (var doc in docs)
                {
                    doc.Root = DitaDocument.Parse(originals[doc]).Root;
                    doc.IsDirty = false;
                }

                vm.Map.RebuildTree();
            }

            var baseline = Errors();
            vm.Map.RebuildTree();
            var rowCount = vm.Map.Tree.SelectMany(All).Count();
            Assert.True(rowCount >= 7, $"{mapFile}: в дереве {rowCount} строк");

            for (var row = 0; row < rowCount; row++)
            {
                rowsChecked++;
                var commands = new (string Name, CommunityToolkit.Mvvm.Input.IRelayCommand Command, bool NeedsClipboard)[]
                {
                    ("Вверх", vm.Map.MoveUpCommand, false), ("Вниз", vm.Map.MoveDownCommand, false),
                    ("Дублировать", vm.Map.DuplicateCommand, false), ("Вложить", vm.Map.IndentCommand, false),
                    ("Вынести", vm.Map.OutdentCommand, false), ("Вырезать", vm.Map.CutCommand, false),
                    ("+ Раздел", vm.Map.AddTopicheadCommand, false),
                    ("Вставить после", vm.Map.PasteCommand, true), ("Вставить перед", vm.Map.PasteBeforeCommand, true),
                    ("Вставить внутрь", vm.Map.PasteAsChildCommand, true),
                    ("Свойства", vm.Map.ShowPropertiesCommand, false)
                };
                // Вставка проверяется с каждым видом строки в буфере (topicref, preface, chapter, keydef…).
                var sources = vm.Map.Tree.SelectMany(All).Where(n => !n.IsRoot && n.Item.TargetPath is not null)
                    .Select(n => n.Item.Node.Name).Distinct().ToList();
                var cases = commands.SelectMany(c => c.NeedsClipboard ? sources.Select(s => (c.Name + " ← " + s, c.Command, s)) : new[] { (c.Name, c.Command, (string?)null) });
                foreach (var (name, command, source) in cases)
                {
                    Restore();
                    var rows = vm.Map.Tree.SelectMany(All).ToList();
                    var label = $"{mapFile} / строка {row} «{rows[row].Item.Node.Name}» / {name}";
                    try
                    {
                        if (source is not null)
                        {
                            vm.Map.SelectedNode = rows.First(n => !n.IsRoot && n.Item.Node.Name == source && n.Item.TargetPath is not null);
                            vm.Map.CopyCommand.Execute(null);
                        }

                        vm.Map.SelectedNode = rows[row];
                        if (!command.CanExecute(null))
                        {
                            continue;
                        }

                        vm.StatusText = string.Empty;
                        command.Execute(null);
                        Dispatcher.UIThread.RunJobs();
                        var changed = docs.Any(d => d.ToXmlString() != originals[d]);
                        mutations += changed ? 1 : 0;
                        if (name != "Свойства" && !changed && string.IsNullOrEmpty(vm.StatusText))
                        {
                            failures.AppendLine($"{label}: молчаливый отказ");
                        }

                        if (Errors() > baseline)
                        {
                            failures.AppendLine($"{label}: новые ошибки структуры — " +
                                string.Join("; ", docs.SelectMany(d => validator.Validate(d)).Where(i => i.Severity == DitaStudio.Core.Validation.IssueSeverity.Error).Select(i => i.Message).Distinct()));
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.AppendLine($"{label}: исключение {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        Assert.True(mutations > 40, $"команды почти ничего не меняли ({mutations})");
        Assert.True(failures.Length == 0, $"строк проверено: {rowsChecked}\n{failures}");
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

    /// <summary>Уход фокуса из изменённого заголовка топика: вопрос, переименовать ли файл; «Да» — файл и ссылки на него меняются,
    /// «Нет» — ничего, и тот же заголовок больше не спрашивают.</summary>
    [AvaloniaFact]
    public async Task TitleEdit_LostFocus_OffersRenamingFile_ByTitle()
    {
        var (window, vm) = await OpenAsync();
        var oldPath = Path.Combine(_project, "concepts", "about.dita");
        var pane = (DocumentView)vm.OpenDocument!(oldPath)!;
        Dispatcher.UIThread.RunJobs();
        var title = pane.Document.Root.FirstElement("title")!;
        var shortdesc = pane.Document.Root.FirstElement("shortdesc")!;

        void EditTitleAndLeave(string typed)
        {
            var editor = pane.AuthorEditor.EditorFor(title)!;
            editor.FocusEditor(0);
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput(typed);
            Dispatcher.UIThread.RunJobs();
            pane.AuthorEditor.EditorFor(shortdesc)!.FocusEditor(0); // фокус уходит из заголовка
            Dispatcher.UIThread.RunJobs();
        }

        // Пока заголовок не тронут — вопроса нет.
        pane.AuthorEditor.EditorFor(shortdesc)!.FocusEditor(0);
        pane.AuthorEditor.EditorFor(title)!.FocusEditor(0);
        pane.AuthorEditor.EditorFor(shortdesc)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows);

        // Напечатали и стёрли — заголовок прежний, вопроса нет.
        var same = pane.AuthorEditor.EditorFor(title)!;
        same.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("x");
        Dispatcher.UIThread.RunJobs();
        same.Document.Remove(0, 1);
        pane.AuthorEditor.EditorFor(shortdesc)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows);
        Assert.Equal("О продукте", title.InnerText);

        // Отказ: файл остался, повторно про тот же заголовок не спрашивают.
        EditTitleAndLeave("Новый ");
        var ask = Assert.Single(window.OwnedWindows);
        Assert.Contains("Новый О продукте".Length > 0 ? "novyy_o_produkte.dita" : string.Empty, ask.GetLogicalDescendants().OfType<SelectableTextBlock>().First().Text);
        ask.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Нет")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(File.Exists(oldPath));

        pane.AuthorEditor.EditorFor(title)!.FocusEditor(0);
        pane.AuthorEditor.EditorFor(shortdesc)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows);

        // Согласие: заголовок меняется ещё раз, файл переименован по заголовку, ссылка в карте обновлена.
        EditTitleAndLeave("Ещё ");
        ask = Assert.Single(window.OwnedWindows);
        ask.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Да")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 20 && File.Exists(oldPath); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        var newPath = Path.Combine(_project, "concepts", "esche_novyy_o_produkte.dita");
        Assert.False(File.Exists(oldPath), "старый файл переименован");
        Assert.True(File.Exists(newPath), "файл назван по заголовку: " + string.Join(", ", Directory.GetFiles(Path.Combine(_project, "concepts")).Select(Path.GetFileName)));
        Assert.Contains("Ещё Новый О продукте", File.ReadAllText(newPath));
        Assert.Contains("concepts/esche_novyy_o_produkte.dita", File.ReadAllText(Path.Combine(_project, "guide.ditamap")));
        Assert.DoesNotContain("concepts/about.dita", File.ReadAllText(Path.Combine(_project, "guide.ditamap")));
        window.Close();
    }

    /// <summary>Г7: «ОК» в «Оформление DOCX» не сбрасывает то, чего в диалоге нет, — свой размер листа, ориентацию, поля.</summary>
    [AvaloniaFact]
    public async Task DocxLayoutDialog_Ok_KeepsCustomPaperSizeAndMargins()
    {
        var (window, vm) = await OpenAsync();
        vm.Project!.SetDocxLayout(new Core.Publishing.DocxLayout
        {
            PaperWidthMm = 150, PaperHeightMm = 200, Landscape = true, MarginTopMm = 12, MarginLeftMm = 18
        });

        var asking = vm.Dialogs.DocxLayoutSettingsAsync(vm.Project);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows);
        dialog.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "ОК")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var result = await asking;

        Assert.NotNull(result);
        Assert.Equal(150, result!.PaperWidthMm);
        Assert.Equal(200, result.PaperHeightMm);
        Assert.True(result.Landscape);
        Assert.Equal(12, result.MarginTopMm);
        Assert.Equal(18, result.MarginLeftMm);
        window.Close();
    }

    /// <summary>Г10: ПКМ по топику в карте — «Размещать на новой странице» и «Не размещать»: класс на строке карты, отметка в меню, отмена.</summary>
    [AvaloniaFact]
    public async Task MapMenu_PageBreakBefore_TogglesClassOnRow_AndIsUndoable()
    {
        var (window, vm) = await OpenAsync();
        var map = Path.Combine(_project, "guide.ditamap");
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        DitaNode Row() => vm.Documents.Tabs.Single(t => t.FullPath == map).Pane.Document.Root
            .DescendantsAndSelf().First(n => n.GetAttribute("href")?.EndsWith("settings.dita") == true);
        vm.OpenDocument!(map);
        vm.Map.SelectedNode = vm.Map.Tree.SelectMany(All).First(n => n.Item.TargetPath?.EndsWith("settings.dita") == true);

        Assert.False(vm.Map.SelectedPageBreakBefore);
        vm.Map.TogglePageBreakBeforeCommand.Execute(null);
        Assert.Contains("page-break-before", Row().GetAttribute("outputclass"));
        Assert.True(vm.Map.SelectedPageBreakBefore, "отметка в меню после правки");
        Assert.False(vm.Map.SelectedPageBreakNone);

        // «Наоборот»: заменяет прежний выбор.
        vm.Map.ToggleNoPageBreakCommand.Execute(null);
        Assert.Equal("page-break-none", Row().GetAttribute("outputclass"));
        Assert.True(vm.Map.SelectedPageBreakNone);
        Assert.False(vm.Map.SelectedPageBreakBefore);

        // Повторный выбор снимает; отмена возвращает предыдущий выбор одним шагом.
        vm.Map.ToggleNoPageBreakCommand.Execute(null);
        Assert.Null(Row().GetAttribute("outputclass"));
        vm.Insert.UndoCommand.Execute(null);
        Assert.Equal("page-break-none", Row().GetAttribute("outputclass"));

        // У раздела без файла пункты недоступны.
        vm.Map.AddTopicheadCommand.Execute(null);
        Assert.False(vm.Map.TogglePageBreakBeforeCommand.CanExecute(null));
        window.Close();
    }

    /// <summary>Г1: закрепление вкладок (слева, без «✕», не закрываются Ctrl+W, запоминаются в проекте и открываются при запуске),
    /// «Закрыть все» / «Закрыть остальные» / «Закрыть справа» — включая закреплённые.</summary>
    [AvaloniaFact]
    public async Task Tabs_Pin_CloseAll_CloseOthers_CloseToRight()
    {
        var (window, vm) = await OpenAsync();
        var files = new[] { "concepts/about.dita", "reference/settings.dita", "tasks/install.dita", "tasks/first-run.dita" };
        foreach (var file in files)
        {
            vm.OpenDocument!(Path.Combine(_project, file));
        }

        var tabs = vm.Documents.Tabs;
        string Name(TabViewModel t) => Path.GetFileName(t.FullPath);
        Assert.Equal(new[] { "about.dita", "settings.dita", "install.dita", "first-run.dita" }, tabs.Select(Name));

        // Закрепление: вкладка уходит влево, у неё нет «✕», Ctrl+W её не закрывает; выбор запоминается в проекте.
        var install = tabs.Single(t => Name(t) == "install.dita");
        install.TogglePinCommand.Execute(null);
        Assert.True(install.IsPinned);
        Assert.False(install.CanClose);
        Assert.Equal("install.dita", Name(tabs[0]));
        Assert.Equal("tasks/install.dita", File.ReadAllText(Path.Combine(_project, ".ditastudio-pinned")).Trim());
        vm.Documents.SelectedTab = install;
        await vm.Documents.CloseCurrentTabCommand.ExecuteAsync(null);
        install.CloseCommand.Execute(null);
        Assert.Contains(install, tabs);

        // Второе закрепление встаёт после первого закреплённого.
        var about = tabs.Single(t => Name(t) == "about.dita");
        about.TogglePinCommand.Execute(null);
        Assert.Equal(new[] { "install.dita", "about.dita" }, tabs.Take(2).Select(Name));

        // «Закрыть справа» от about: закрывает settings и first-run, закреплённые остаются.
        await about.CloseToRightCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "install.dita", "about.dita" }, tabs.Select(Name));

        // Проект открывается заново — закреплённые вкладки открываются сами и закреплены.
        await window.ViewModel.ProjectPanel.LoadProjectAsync(_project);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "install.dita", "about.dita" }, tabs.Select(Name));
        Assert.All(tabs, t => Assert.True(t.IsPinned));

        // «Закрыть остальные» закрывает и закреплённые; «Закрыть все» — все.
        vm.OpenDocument!(Path.Combine(_project, "reference/settings.dita"));
        var keep = tabs.Single(t => Name(t) == "settings.dita");
        await keep.CloseOthersCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "settings.dita" }, tabs.Select(Name));
        vm.OpenDocument!(Path.Combine(_project, "tasks/install.dita"));
        await tabs[0].CloseAllCommand.ExecuteAsync(null);
        Assert.Empty(tabs);
        Assert.False(File.Exists(Path.Combine(_project, ".ditastudio-pinned")), "закрытые закреплённые вкладки из проекта убраны");

        // Несохранённая вкладка: «Отмена» в вопросе останавливает групповое закрытие.
        var pane = vm.OpenDocument!(Path.Combine(_project, "concepts/about.dita"))!;
        vm.OpenDocument!(Path.Combine(_project, "tasks/first-run.dita"));
        pane.Document.IsDirty = true;
        var closing = vm.Documents.CloseAllAsync();
        Dispatcher.UIThread.RunJobs();
        window.OwnedWindows[0].GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Отмена")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        await closing;
        Assert.Contains(tabs, t => Name(t) == "about.dita");
        window.Close();
    }

    /// <summary>Г3: двойной щелчок по красной (битой) строке карты не открывает карту молча, а объясняет причину и предлагает
    /// создать файл, выбрать другой или убрать строку; «Создать файл по ссылке» делает строку не красной.</summary>
    [AvaloniaFact]
    public async Task MapBrokenRow_ExplainsReason_AndOffersFix_CreateFileFixesIt()
    {
        File.WriteAllText(Path.Combine(_project, "broken.ditamap"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<map><title>Битая</title><topicref href=\"tasks/absent-topic.dita\" navtitle=\"Нет файла\"/></map>");
        var (window, vm) = await OpenAsync();
        vm.Map.SelectedMap = vm.Map.Maps.First(m => Path.GetFileName(m.FullPath) == "broken.ditamap");
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        var row = vm.Map.Tree.SelectMany(All).First(n => n.Title == "Нет файла");
        vm.Map.SelectedNode = row;

        Assert.True(row.IsBroken);
        Assert.Contains("Файл не найден", row.ToolTip);
        Assert.Contains("absent-topic.dita", row.ToolTip);
        Assert.Contains("absent-topic.dita", vm.StatusText); // причина видна и в строке состояния
        Assert.True(vm.Map.SelectedIsBroken);
        Assert.True(vm.Map.CreateMissingFileCommand.CanExecute(null));

        // Двойной щелчок: вместо молчаливого открытия карты — окно с причиной и тремя способами исправить.
        var tabsBefore = vm.Documents.Tabs.Count;
        var asking = vm.Map.OpenSelectedCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows);
        var texts = string.Join("|", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains("Файл не найден", texts);
        Assert.Contains("Создать файл по ссылке", texts);
        Assert.Contains("Выбрать другой файл", texts);
        Assert.Contains("Убрать строку из карты", texts);
        Assert.Equal(tabsBefore, vm.Documents.Tabs.Count); // карта не открылась «пустой»
        dialog.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Отмена")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        await asking;

        // «Создать файл по ссылке»: файл появляется, строка больше не красная, топик открыт.
        var target = Path.Combine(_project, "tasks", "absent-topic.dita");
        Assert.False(File.Exists(target));
        await vm.Map.CreateMissingFileCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(File.Exists(target));
        Assert.Contains("Нет файла", File.ReadAllText(target));
        var fixedRow = vm.Map.Tree.SelectMany(All).First(n => n.Title is "Нет файла");
        Assert.False(fixedRow.IsBroken);
        Assert.Contains(vm.Documents.Tabs, t => Path.GetFullPath(t.FullPath) == Path.GetFullPath(target));
        window.Close();
    }

    /// <summary>Г8: «Параметры страницы» и «Оформление DOCX» — одно окно с двумя вкладками; один «ОК» сохраняет обе части;
    /// каждый пункт меню открывает свою вкладку.</summary>
    [AvaloniaFact]
    public async Task LayoutDialog_IsOneWindowWithTwoTabs_SavesBothParts()
    {
        var (window, vm) = await OpenAsync();
        vm.Project!.SetDocxLayout(new Core.Publishing.DocxLayout { PaperWidthMm = 150, PaperHeightMm = 200, TitlePage = true });

        // «Параметры страницы…» открывает вкладку «Страница», «Оформление DOCX…» — «Оформление».
        var asking = vm.Dialogs.PageSetupAsync(vm.Project);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows);
        var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().First(t => t.Name == "LayoutTabs");
        Assert.Equal(new[] { "Оформление", "Страница" }, tabs.Items.OfType<TabItem>().Select(t => t.Header as string));
        Assert.Equal(1, tabs.SelectedIndex);

        // Правка на обеих вкладках: титул выключен, высота листа 210.
        dialog.GetLogicalDescendants().OfType<CheckBox>().First(c => (c.Content as string)!.StartsWith("Отдельная титульная страница")).IsChecked = false;
        dialog.GetLogicalDescendants().OfType<TextBox>().First(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Высота листа, мм").Text = "210";
        dialog.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "ОК")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var result = await asking;

        Assert.NotNull(result);
        Assert.False(result!.TitlePage);
        Assert.Equal(150, result.PaperWidthMm);
        Assert.Equal(210, result.PaperHeightMm);

        var second = vm.Dialogs.DocxLayoutSettingsAsync(vm.Project);
        Dispatcher.UIThread.RunJobs();
        var other = Assert.Single(window.OwnedWindows);
        Assert.Equal(0, other.GetLogicalDescendants().OfType<TabControl>().First(t => t.Name == "LayoutTabs").SelectedIndex);
        other.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Отмена")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(await second);
        window.Close();
    }

    /// <summary>Г5: у карты «Автор» — список топиков по иерархии, «Предпросмотр» — текст всего издания; «битая» строка красная с причиной.</summary>
    [AvaloniaFact]
    public async Task MapDocument_AuthorShowsTopicHierarchy_PreviewShowsWholeText()
    {
        File.WriteAllText(Path.Combine(_project, "outline.ditamap"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<map><title>Издание</title>" +
            "<topicref href=\"concepts/about.dita\"><topicref href=\"reference/settings.dita\"/></topicref>" +
            "<topichead navtitle=\"Раздел\"><topicref href=\"tasks/absent.dita\" navtitle=\"Нет файла\"/></topichead></map>");
        var (window, vm) = await OpenAsync();
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "outline.ditamap"))!;
        Dispatcher.UIThread.RunJobs();

        // «Автор» карты — список топиков: заголовки, вложенность, битая строка красным с причиной.
        var outline = Assert.IsType<Desktop.Authoring.MapOutlineView>(pane.MapOutline);
        pane.Mode = Presentation.Services.EditorMode.Author;
        Dispatcher.UIThread.RunJobs();
        var root = Assert.Single(outline.Roots);
        static IEnumerable<MapTreeNode> All(MapTreeNode n) => n.Children.SelectMany(All).Prepend(n);
        var titles = All(root).Select(n => n.Title).ToList();
        Assert.Contains("О продукте", titles);
        Assert.Contains("Раздел", titles);
        var about = All(root).First(n => n.Item.TargetPath?.EndsWith("about.dita") == true);
        Assert.Single(about.Children); // settings вложен в about
        var broken = All(root).First(n => n.Title == "Нет файла");
        Assert.True(broken.IsBroken);
        Assert.Contains("Файл не найден", broken.ToolTip);
        Assert.Contains(outline.GetLogicalDescendants().OfType<TextBlock>(), t => (t.Text ?? string.Empty).StartsWith("Топиков в карте: 3") && t.Text!.Contains("не найдено файлов: 1"));

        // Двойной щелчок открывает топик; у битой строки — не открывает.
        string? opened = null;
        pane.OpenFileRequested += (_, path) => opened = path;
        var tree = outline.GetLogicalDescendants().OfType<TreeView>().First();
        tree.SelectedItem = about;
        tree.RaiseEvent(new Avalonia.Input.TappedEventArgs(Avalonia.Input.InputElement.DoubleTappedEvent, null!) { Source = tree });
        Assert.EndsWith("about.dita", opened);

        // «Предпросмотр» — текст всего издания: оба топика в одном HTML, заголовок карты, оглавление.
        var file = await pane.Preview.RefreshAsync();
        Assert.NotNull(file);
        var html = File.ReadAllText(file!);
        Assert.Contains("Издание", html);
        Assert.Contains("О продукте", html);
        Assert.Contains("Синтаксис", html);
        Assert.DoesNotContain("<ul class=\"map-list\"", html);

        // У обычного топика «Автор» — прежний редактор.
        var topicPane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, "concepts", "about.dita"))!;
        Assert.Null(topicPane.MapOutline);
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
