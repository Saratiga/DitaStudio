using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Editing;
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
        MapTreeView.AddHandler(DragDrop.DragLeaveEvent, OnMapDragLeave);
        ViewModel.Map.Tree.CollectionChanged += OnMapTreeCollectionChanged;
        ViewModel.Map.RevealRequested += OnMapRevealRequested;
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

    private void OnToggleAttributeNotes(object? sender, RoutedEventArgs e)
    {
        Authoring.AuthorView.AttributeNotesEnabled = AttributeNotesMenu.IsChecked;
        foreach (var pane in ViewModel.Panes.Values)
        {
            pane.ReloadViews(); // пометки рисуются при построении блоков — перестраиваем «Автор» открытых документов
        }
    }

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
            EndMapDrag();
        }
    }

    private void OnMapDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(MapNodeFormat) || _dragged is not { } dragged)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        UpdateMapAutoScroll(e.GetPosition(MapTreeView).Y);
        if (DropTargetAt(e) is not { } drop)
        {
            ShowDropIndicator(null, default);
            e.DragEffects = DragDropEffects.None;
            return;
        }

        if (!MapViewModel.CanDrop(dragged, drop.Node, drop.Position, out var reason))
        {
            ShowDropIndicator(null, default);
            e.DragEffects = DragDropEffects.None;
            ViewModel.StatusText = reason;
            return;
        }

        ShowDropIndicator(drop.Item, drop.Position);
        e.DragEffects = DragDropEffects.Move;
        ViewModel.StatusText = drop.Position switch
        {
            DropPosition.Before => $"Перед «{drop.Node.Title}»",
            DropPosition.After => $"После «{drop.Node.Title}»",
            _ => $"Внутрь «{drop.Node.Title}»"
        };
    }

    private void OnMapDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataTransfer.Contains(MapNodeFormat) || _dragged is not { } dragged || DropTargetAt(e) is not { } drop)
            {
                return;
            }

            ViewModel.Map.MoveByDrag(dragged, drop.Node, drop.Position);
        }
        finally
        {
            EndMapDrag();
        }
    }

    private void OnMapDragLeave(object? sender, DragEventArgs e) => ShowDropIndicator(null, default);

    /// <summary>Строка под указателем и зона в ней (перед / после / внутрь); null — указатель не над строкой.</summary>
    private (TreeViewItem Item, MapTreeNode Node, DropPosition Position)? DropTargetAt(DragEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not { DataContext: MapTreeNode node } item)
        {
            return null;
        }

        // Высота самой строки — «корень» шаблона; у TreeViewItem она включает вложенные строки.
        var header = item.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_LayoutRoot");
        var y = header is null ? e.GetPosition(item).Y : e.GetPosition(header).Y;
        var height = header?.Bounds.Height ?? 24;
        return (item, node, MapDropZones.PositionAt(y, height));
    }

    private TreeViewItem? _dropIndicatorItem;
    private static readonly string[] DropClasses = { "dropBefore", "dropAfter", "dropChild" };

    private void ShowDropIndicator(TreeViewItem? item, DropPosition position)
    {
        if (_dropIndicatorItem is not null && !ReferenceEquals(_dropIndicatorItem, item))
        {
            _dropIndicatorItem.Classes.RemoveAll(DropClasses);
        }

        _dropIndicatorItem = item;
        if (item is null)
        {
            return;
        }

        item.Classes.RemoveAll(DropClasses);
        item.Classes.Add(position switch
        {
            DropPosition.Before => "dropBefore",
            DropPosition.After => "dropAfter",
            _ => "dropChild"
        });
    }

    // ------------------------------------------------ прокрутка списка карты

    private DispatcherTimer? _mapAutoScrollTimer;
    private double _mapAutoScrollStep;

    private ScrollViewer? MapScroll => MapTreeView.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>У верхнего и нижнего края списка во время перетаскивания — плавная прокрутка (по таймеру,
    /// чтобы шла и когда указатель неподвижен); в остальной области список стоит на месте.</summary>
    private void UpdateMapAutoScroll(double y)
    {
        _mapAutoScrollStep = MapDropZones.AutoScrollStep(y, MapTreeView.Bounds.Height);
        if (_mapAutoScrollStep == 0)
        {
            _mapAutoScrollTimer?.Stop();
            return;
        }

        _mapAutoScrollTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) =>
        {
            if (MapScroll is { } scroll)
            {
                var offset = Math.Clamp(scroll.Offset.Y + _mapAutoScrollStep, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
                scroll.Offset = new Vector(scroll.Offset.X, offset);
            }
        });
        _mapAutoScrollTimer.Start();
    }

    private void EndMapDrag()
    {
        _mapAutoScrollTimer?.Stop();
        _mapAutoScrollStep = 0;
        ShowDropIndicator(null, default);
    }

    // Список карты пересоздаётся после каждой правки (Clear + Add) — полоса прокрутки при этом
    // сбрасывалась бы вверх и «уезжала» за перенесённой строкой. Запоминаем смещение и возвращаем.
    private Vector? _mapScrollBeforeRebuild;

    private void OnMapTreeCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            _mapScrollBeforeRebuild = MapScroll?.Offset;
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && _mapScrollBeforeRebuild is { } offset)
        {
            _mapScrollBeforeRebuild = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (MapScroll is { } scroll)
                {
                    scroll.Offset = offset;
                }
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Показывает строку, добавленную командой (она могла оказаться за краем списка).</summary>
    private void OnMapRevealRequested(MapTreeNode node) =>
        Dispatcher.UIThread.Post(() =>
        {
            MapTreeView.GetVisualDescendants().OfType<TreeViewItem>()
                .FirstOrDefault(i => ReferenceEquals(i.DataContext, node))?.BringIntoView();
        }, DispatcherPriority.Loaded);
}
