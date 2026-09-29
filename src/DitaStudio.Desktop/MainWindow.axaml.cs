using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DitaStudio.Desktop.Services;
using DitaStudio.Desktop.Preview;
using DitaStudio.Desktop.Views;
using DitaStudio.Presentation;
using DitaStudio.Presentation.Services;
using DitaStudio.Presentation.ViewModels;

namespace DitaStudio.Desktop;

/// <summary>
/// Главное окно Avalonia-оболочки. Логика — в общих ViewModel'ях (DitaStudio.Presentation);
/// здесь связь с ними: сервисы оболочки, обновление панелей, жесты без команд (двойной
/// щелчок, перетаскивание в карте), тема, недавние проекты, закрытие с подтверждением.
/// </summary>
public partial class MainWindow : Window
{
    // Перетаскивание внутри дерева карты: сам узел держим в поле (в DataTransfer кладутся
    // только строки и байты), а формат-метка отличает наше перетаскивание от чужого.
    private static readonly DataFormat<string> MapNodeFormat = DataFormat.CreateStringApplicationFormat("dita-studio-map-node");
    private MapTreeNode? _dragged;

    private bool _closeConfirmed;
    private MapTreeNode? _dragCandidate;
    private Point _dragStart;

    public MainWindow()
    {
        var files = new AvaloniaFilePicker(() => this);
        var pdfPrinter = new CefPdfPrinter();
        ViewModel = new MainViewModel(new UiServices(
            new AvaloniaDialogService(() => this, files),
            files,
            new AvaloniaUiPlatform(),
            pdfPrinter,
            (project, document) => CreateDocumentView(project, document, pdfPrinter)));

        ViewModel.OpenDocument = path => ViewModel.Documents.OpenDocument(path);
        ViewModel.UpdateTabHeaders = ViewModel.Documents.RefreshAllTabTitles;
        ViewModel.RefreshEditorContext = () =>
        {
            ViewModel.SidePanels.Refresh();
            ViewModel.SidePanels.RefreshOutline();
        };
        ViewModel.RefreshProjectTree = ViewModel.ProjectPanel.RebuildTree;
        ViewModel.RefreshMapSelector = ViewModel.Map.RefreshMaps;
        ViewModel.RefreshMapTree = ViewModel.Map.RebuildTree;
        ViewModel.RefreshRecentProjectsMenu = RefreshRecentProjectsMenu;
        ViewModel.RefreshOutline = ViewModel.SidePanels.RefreshOutline;
        ViewModel.RefreshAttributePanel = ViewModel.SidePanels.RefreshAttributes;
        ViewModel.ApplyRefactorResult = ViewModel.ApplyRefactorResultToDocuments;

        DataContext = ViewModel;
        InitializeComponent();

        ThemeMenuItem.IsChecked = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        RefreshRecentProjectsMenu();
        ViewModel.StatusText = "Откройте папку с проектом DITA: Файл → Открыть папку проекта.";

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        MapTreeView.AddHandler(PointerPressedEvent, OnMapPointerPressed, RoutingStrategies.Tunnel);
        MapTreeView.AddHandler(DoubleTappedEvent, OnMapTreeDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        MapTreeView.AddHandler(PointerMovedEvent, OnMapPointerMoved, RoutingStrategies.Tunnel);
        MapTreeView.AddHandler(DragDrop.DragOverEvent, OnMapDragOver);
        MapTreeView.AddHandler(DragDrop.DropEvent, OnMapDrop);
        MapTreeView.AddHandler(Button.ClickEvent, OnMapPublishClick);
        BuildColorPalette();
    }

    public MainViewModel ViewModel { get; }

    // ------------------------------------------------------------ окно

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            ViewModel.Recovery.Detach();
            ViewModel.ExternalChanges.Detach();
            base.OnClosing(e);
            return;
        }

        // Подтверждение асинхронное (диалог Avalonia) — отменяем закрытие, спрашиваем, и если
        // всё сохранено или пользователь отказался от правок, закрываем снова.
        e.Cancel = true;
        if (await ViewModel.Documents.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // Вернулись в окно после другой программы — сверяем открытые файлы с диском
    // (подстраховка к FileSystemWatcher, который может пропустить событие).
    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _ = ViewModel.ExternalChanges.CheckNowAsync();
    }

    /// <summary>Немедленная копия несохранённых правок — из обработчика непредвиденных ошибок.</summary>
    public void SnapshotForRecovery() => ViewModel.Recovery.SnapshotAll();

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.E)
        {
            FocusPalette();
            e.Handled = true;
        }
        else if (ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.F)
        {
            FocusSearch();
            e.Handled = true;
        }
    }

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        var dark = ThemeMenuItem.IsChecked;
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        ThemeSettings.SaveDark(dark);
    }

    private void RefreshRecentProjectsMenu()
    {
        var items = new List<MenuItem>();
        foreach (var path in RecentProjects.Load())
        {
            var item = new MenuItem { Header = path };
            item.Click += (_, _) => _ = ViewModel.ProjectPanel.LoadProjectAsync(path);
            items.Add(item);
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = "(пусто)", IsEnabled = false });
        }

        RecentProjectsMenu.ItemsSource = items;
    }

    private void FocusSearch()
    {
        BottomTabs.SelectedIndex = 1;
        SearchBox.Focus();
    }

    private void OnFocusSearch(object? sender, RoutedEventArgs e) => FocusSearch();

    private void FocusPalette()
    {
        RightTabs.SelectedIndex = 1;
        PaletteFilter.Focus();
    }

    private void OnFocusPalette(object? sender, RoutedEventArgs e) => FocusPalette();

    // ------------------------------------------------------------ двойной щелчок

    private void OnProjectTreeDoubleTapped(object? sender, TappedEventArgs e) =>
        ViewModel.ProjectPanel.OpenSelectedFileCommand.Execute(null);

    /// <summary>
    /// Двойной щелчок по строке карты открывает её топик — и у строки с дочерними тоже: обработчик
    /// стоит в туннеле, раньше самой строки дерева, которая иначе забирает двойной щелчок себе
    /// (раскрывает или сворачивает ветку). Ветка раскрывается стрелкой и клавишами. Щелчок по флажку
    /// «публиковать» и по стрелке раскрытия ничего не открывает.
    /// </summary>
    private void OnMapTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        if (source.FindAncestorOfType<CheckBox>(includeSelf: true) is not null)
        {
            e.Handled = true;
            return;
        }

        if (source.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null || MapNodeAt(source) is not { } node)
        {
            return;
        }

        ViewModel.Map.SelectedNode = node;
        ViewModel.Map.OpenSelectedCommand.Execute(null);
        e.Handled = true;

        // Строка дерева сама сворачивает или раскрывает ветку по двойному щелчку — возвращаем
        // состояние, которое было в момент второго нажатия.
        if (_doubleClickExpansion is { } saved && ReferenceEquals(saved.Node, node))
        {
            node.IsExpanded = saved.Expanded;
        }

        _doubleClickExpansion = null;
    }

    private (MapTreeNode Node, bool Expanded)? _doubleClickExpansion;

    private void OnToggleEnterSuggestions(object? sender, RoutedEventArgs e) =>
        Authoring.AuthorView.EnterSuggestionsEnabled = EnterSuggestionsMenu.IsChecked;

    /// <summary>Флажок «публиковать» у строки карты.</summary>
    private void OnMapPublishClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is CheckBox { DataContext: MapTreeNode node } box && box.Classes.Contains("publish"))
        {
            ViewModel.Map.TogglePublishedCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnKeyDoubleTapped(object? sender, TappedEventArgs e) =>
        ViewModel.ProjectPanel.OpenSelectedKeyCommand.Execute(null);

    private void OnIssueDoubleTapped(object? sender, TappedEventArgs e) =>
        ViewModel.Validation.OpenSelectedIssueCommand.Execute(null);

    private void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e) =>
        ViewModel.Search.OpenSelectedResultCommand.Execute(null);

    private void OnPaletteDoubleTapped(object? sender, TappedEventArgs e) =>
        ViewModel.SidePanels.InsertSelectedPaletteEntryCommand.Execute(null);

    // ------------------------------------------------------------ атрибуты

    private void OnAttributeEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AttributeRowViewModel row)
        {
            row.ApplyCommand.Execute(null);
        }
    }

    private void OnAttributeComboSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string selected, DataContext: AttributeRowViewModel row })
        {
            row.Value = selected;
            row.ApplyCommand.Execute(null);
        }
    }

    // ------------------------------------------------------------ перетаскивание в карте

    private static MapTreeNode? MapNodeAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as MapTreeNode;

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Нажатие на флажок — не начало перетаскивания строки.
        if ((e.Source as Visual)?.FindAncestorOfType<CheckBox>(includeSelf: true) is not null)
        {
            _dragCandidate = null;
            return;
        }

        _doubleClickExpansion = e.ClickCount == 2 && MapNodeAt(e.Source) is { } pressed ? (pressed, pressed.IsExpanded) : null;

        if (e.GetCurrentPoint(MapTreeView).Properties.IsLeftButtonPressed)
        {
            _dragStart = e.GetPosition(MapTreeView);
            _dragCandidate = MapNodeAt(e.Source);
        }
    }

    private async void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is null || !e.GetCurrentPoint(MapTreeView).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(MapTreeView);
        if (Math.Abs(position.X - _dragStart.X) < 4 && Math.Abs(position.Y - _dragStart.Y) < 4)
        {
            return;
        }

        var dragged = _dragCandidate;
        _dragCandidate = null;
        if (dragged.Item.Node.Parent is null)
        {
            return;
        }

        _dragged = dragged;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(MapNodeFormat, dragged.Title));
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            _dragged = null;
        }
    }

    private static void OnMapDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(MapNodeFormat) ? DragDropEffects.Move : DragDropEffects.None;

    private void OnMapDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(MapNodeFormat) || _dragged is not { } dragged ||
            (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not { DataContext: MapTreeNode target } targetItem)
        {
            return;
        }

        // Высота TreeViewItem включает раскрытые вложенные — сравниваем с высотой строки заголовка.
        var before = e.GetPosition(targetItem).Y < 12;
        ViewModel.Map.MoveByDrag(dragged, target, before);
    }
}
