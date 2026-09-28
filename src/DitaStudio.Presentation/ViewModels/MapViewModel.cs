using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Вкладка «Карта»: список карт проекта, дерево выбранной карты и операции над ним —
// добавление ссылок на топики и разделов, перестановка/вложение (кнопками и
// перетаскиванием), удаление, таблица соответствий. Дерево (Tree) и операции
// использует Avalonia-оболочка; WPF-оболочка пока строит своё дерево сама
// (MainWindow.Map.cs) и берёт отсюда только список карт.
public partial class MapViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<ProjectFile> Maps { get; } = new();

    [ObservableProperty]
    private ProjectFile? selectedMap;

    public ObservableCollection<MapTreeNode> Tree { get; } = new();

    [ObservableProperty]
    private MapTreeNode? selectedNode;

    public MapViewModel(MainViewModel main)
    {
        _main = main;
    }

    public void RefreshMaps()
    {
        var project = _main.Project;
        Maps.Clear();
        if (project is null)
        {
            return;
        }

        foreach (var map in project.Maps)
        {
            Maps.Add(map);
        }

        SelectedMap = Maps.Count > 0 ? Maps[0] : null;
    }

    partial void OnSelectedMapChanged(ProjectFile? value) => _main.RefreshMapTree?.Invoke();

    partial void OnSelectedNodeChanged(MapTreeNode? value)
    {
        if (value is not null)
        {
            _main.StatusText = $"{value.Item.ElementName}: {value.Title}{(value.IsBroken ? " — файл не найден" : string.Empty)}";
        }
    }

    // ------------------------------------------------------------ дерево

    public void RebuildTree()
    {
        Tree.Clear();
        var project = _main.Project;
        if (project is null || SelectedMap is not { } map)
        {
            return;
        }

        MapTree mapTree;
        try
        {
            mapTree = MapTree.Build(project, map.FullPath);
        }
        catch (Exception ex)
        {
            _main.StatusText = $"Карта не читается: {ex.Message}";
            return;
        }

        Tree.Add(BuildNode(mapTree.Root, true));
    }

    private static MapTreeNode BuildNode(MapItem item, bool isRoot)
    {
        var node = new MapTreeNode(item, isRoot);
        foreach (var child in item.Children)
        {
            node.Children.Add(BuildNode(child, false));
        }

        return node;
    }

    /// <summary>Двойной щелчок: открывает топик узла, а если его нет — саму карту.</summary>
    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedNode?.Item.TargetPath is { } target && File.Exists(target))
        {
            _main.OpenDocument?.Invoke(target);
        }
        else if (SelectedMap is { } map)
        {
            _main.OpenDocument?.Invoke(map.FullPath);
        }
    }

    private IDocumentView? OpenMapPane() =>
        SelectedMap is { } map ? _main.OpenDocument?.Invoke(map.FullPath) : null;

    /// <summary>Правка карты открывает её во вкладке: изменения видны, отменяются и сохраняются
    /// как обычные правки документа.</summary>
    private void AfterMapEdit(IDocumentView pane)
    {
        pane.Document.IsDirty = true;
        pane.ReloadViews();
        _main.Documents.RefreshAllTabTitles();
        RebuildTree();
    }

    // ------------------------------------------------------------ добавление

    [RelayCommand]
    private async Task AddTopicref()
    {
        var project = _main.Project;
        if (project is null || SelectedMap is not { } map)
        {
            return;
        }

        var result = await _main.Dialogs.InsertXrefAsync(project, map.FullPath);
        if (result is null)
        {
            return;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        pane.PushUndo("Добавление ссылки в карту");
        var topicref = DitaNode.Element("topicref");
        topicref.SetAttribute("href", RefResolver.MakeRelative(map.FullPath, result.File.FullPath));
        InsertAtSelection(pane, topicref);
        AfterMapEdit(pane);
    }

    /// <summary>
    /// Только что созданный топик — сразу в карту, выбранную на вкладке «Карта»: после выбранного
    /// узла (без выбора — в конец карты); новый узел становится выбранным. false — карты нет.
    /// </summary>
    public bool AddCreatedTopic(string path)
    {
        if (_main.Project is null || SelectedMap is not { } map)
        {
            return false;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return false;
        }

        pane.PushUndo("Добавление нового топика в карту");
        var topicref = DitaNode.Element("topicref");
        topicref.SetAttribute("href", RefResolver.MakeRelative(map.FullPath, path));
        InsertAtSelection(pane, topicref);
        AfterMapEdit(pane);

        var full = Path.GetFullPath(path);
        SelectedNode = Tree.SelectMany(Flatten).FirstOrDefault(n =>
            n.Item.TargetPath is { } target && string.Equals(Path.GetFullPath(target), full, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static IEnumerable<MapTreeNode> Flatten(MapTreeNode node) =>
        node.Children.SelectMany(Flatten).Prepend(node);

    [RelayCommand]
    private void AddTopichead()
    {
        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        pane.PushUndo("Добавление раздела в карту");
        var topichead = DitaNode.Element("topichead");
        var meta = DitaNode.Element("topicmeta");
        var navtitle = DitaNode.Element("navtitle");
        navtitle.SetText("Новый раздел");
        meta.Add(navtitle);
        topichead.Add(meta);
        InsertAtSelection(pane, topichead);
        AfterMapEdit(pane);
    }

    /// <summary>Новый элемент — после выбранного в дереве; без выбора или на корне — в конец карты.</summary>
    private void InsertAtSelection(IDocumentView pane, DitaNode element)
    {
        var target = SelectedNode?.Item.Node ?? pane.Document.Root;
        if (ReferenceEquals(target, pane.Document.Root) || target.Name is "map" or "bookmap")
        {
            target.Add(element);
        }
        else
        {
            target.Parent?.Insert(target.IndexInParent + 1, element);
        }
    }

    // ------------------------------------------------------------ структура

    private void StructureOperation(Func<DitaNode, bool> operation, string description)
    {
        if (SelectedNode?.Item is not { } item)
        {
            return;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        pane.PushUndo(description);
        if (!operation(item.Node))
        {
            _main.StatusText = "Операция здесь недоступна.";
            return;
        }

        AfterMapEdit(pane);
    }

    [RelayCommand]
    private void MoveUp() => StructureOperation(EditCommands.MoveUp, "Перемещение в карте");

    [RelayCommand]
    private void MoveDown() => StructureOperation(EditCommands.MoveDown, "Перемещение в карте");

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedNode?.Item is not { } item ||
            !await _main.Dialogs.ConfirmAsync("Карта", $"Убрать «{item.Title}» из карты?"))
        {
            return;
        }

        StructureOperation(EditCommands.Delete, "Удаление из карты");
    }

    [RelayCommand]
    private void Indent() => StructureOperation(node =>
    {
        var previous = EditCommands.PreviousElement(node);
        if (previous is null || previous.Name is "title" or "topicmeta")
        {
            return false;
        }

        node.RemoveSelf();
        previous.Add(node);
        return true;
    }, "Вложение в карте");

    [RelayCommand]
    private void Outdent() => StructureOperation(node =>
    {
        var parent = node.Parent;
        var grand = parent?.Parent;
        if (parent is null || grand is null)
        {
            return false;
        }

        var index = grand.IndexOf(parent) + 1;
        node.RemoveSelf();
        grand.Insert(index, node);
        return true;
    }, "Вынос из вложения");

    /// <summary>Перетаскивание: ставит <paramref name="dragged"/> перед или после
    /// <paramref name="target"/>. Допустимо только между элементами одного уровня.</summary>
    public void MoveByDrag(MapTreeNode dragged, MapTreeNode target, bool before)
    {
        if (ReferenceEquals(dragged, target))
        {
            return;
        }

        var parent = dragged.Item.Node.Parent;
        if (parent is null || !ReferenceEquals(target.Item.Node.Parent, parent))
        {
            _main.StatusText = "Перетаскивание допустимо только между элементами одного уровня.";
            return;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        pane.PushUndo("Перестановка в карте");
        parent.Remove(dragged.Item.Node);
        var targetIndex = parent.IndexOf(target.Item.Node);
        parent.Insert(before ? targetIndex : targetIndex + 1, dragged.Item.Node);
        AfterMapEdit(pane);
    }

    // ------------------------------------------------ таблица соответствий

    [RelayCommand]
    private async Task EditRelTable()
    {
        var project = _main.Project;
        if (project is null || SelectedMap is not { } map)
        {
            return;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        var existing = pane.Document.Root.FirstElement("reltable");
        var rows = await _main.Dialogs.EditRelTableAsync(project, ParseRelTable(project, existing, map.FullPath));
        if (rows is null)
        {
            return;
        }

        pane.PushUndo("Таблица соответствий");
        var reltable = BuildRelTableNode(rows, map.FullPath);
        if (existing is not null)
        {
            existing.ReplaceWith(reltable);
        }
        else
        {
            pane.Document.Root.Add(reltable);
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        _main.Documents.RefreshAllTabTitles();
        _main.StatusText = "Таблица соответствий обновлена.";
    }

    public static List<List<RelTableCell>> ParseRelTable(DitaProject project, DitaNode? reltable, string mapPath)
    {
        var rows = new List<List<RelTableCell>>();
        if (reltable is null)
        {
            return rows;
        }

        foreach (var relrow in reltable.ElementChildren().Where(n => n.Name == "relrow"))
        {
            var row = new List<RelTableCell>();
            foreach (var relcell in relrow.ElementChildren().Where(n => n.Name == "relcell"))
            {
                var href = relcell.FirstElement("topicref")?.GetAttribute("href");
                var cell = new RelTableCell();
                if (!string.IsNullOrWhiteSpace(href))
                {
                    var reference = RefResolver.Parse(mapPath, href!);
                    if (reference.Path is not null)
                    {
                        cell.File = project.Files.FirstOrDefault(
                            f => string.Equals(f.FullPath, reference.Path, StringComparison.OrdinalIgnoreCase));
                        cell.TopicId = reference.TopicId;
                    }
                }

                row.Add(cell);
            }

            rows.Add(row);
        }

        return rows;
    }

    public static DitaNode BuildRelTableNode(List<List<RelTableCell>> rows, string mapPath)
    {
        var reltable = DitaNode.Element("reltable");
        foreach (var row in rows)
        {
            var relrow = DitaNode.Element("relrow");
            foreach (var cell in row)
            {
                var relcell = DitaNode.Element("relcell");
                if (cell.File is not null)
                {
                    var href = RefResolver.MakeRelative(mapPath, cell.File.FullPath);
                    if (!string.IsNullOrEmpty(cell.TopicId))
                    {
                        href += "#" + cell.TopicId;
                    }

                    var topicref = DitaNode.Element("topicref");
                    topicref.SetAttribute("href", href);
                    relcell.Add(topicref);
                }

                relrow.Add(relcell);
            }

            reltable.Add(relrow);
        }

        return reltable;
    }
}
