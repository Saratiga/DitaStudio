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

    /// <summary>
    /// Строит дерево заново. Раскрытые и свёрнутые ветки и выделенная строка запоминаются по узлам
    /// документа и возвращаются — правка карты (флажок «публиковать», перестановка) не должна
    /// раскрывать всё дерево и сбрасывать выделение. Узел, которого больше нет, раскрыт по умолчанию.
    /// </summary>
    public void RebuildTree()
    {
        var expanded = new Dictionary<DitaNode, bool>(ReferenceEqualityComparer.Instance);
        foreach (var node in Tree.SelectMany(Flatten))
        {
            expanded[node.Item.Node] = node.IsExpanded;
        }

        var selected = SelectedNode?.Item.Node;
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

        Tree.Add(BuildNode(mapTree.Root, true, expanded));
        if (selected is not null)
        {
            SelectedNode = Tree.SelectMany(Flatten).FirstOrDefault(n => ReferenceEquals(n.Item.Node, selected));
        }
    }

    private static MapTreeNode BuildNode(MapItem item, bool isRoot, Dictionary<DitaNode, bool> expanded)
    {
        var node = new MapTreeNode(item, isRoot);
        if (!isRoot && expanded.TryGetValue(item.Node, out var wasExpanded))
        {
            node.IsExpanded = wasExpanded;
        }

        foreach (var child in item.Children)
        {
            node.Children.Add(BuildNode(child, false, expanded));
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

    /// <param name="activate">false — карта открывается во вкладке, но вкладка не выбирается: правка
    /// вроде флажка «публиковать» не должна уводить пользователя от документа, над которым он работает.</param>
    private IDocumentView? OpenMapPane(bool activate = true)
    {
        if (SelectedMap is not { } map)
        {
            return null;
        }

        var previous = _main.Documents.SelectedTab;
        var pane = _main.OpenDocument?.Invoke(map.FullPath);
        if (!activate && previous is not null)
        {
            _main.Documents.SelectedTab = previous;
        }

        return pane;
    }

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

    /// <summary>Куда вставлять относительно выбранного узла карты.</summary>
    private enum Place
    {
        After,
        Before,
        Child
    }

    [RelayCommand]
    private Task AddTopicref() => AddTopicrefAsync(Place.After);

    [RelayCommand]
    private Task AddTopicrefBefore() => AddTopicrefAsync(Place.Before);

    [RelayCommand]
    private Task AddChildTopicref() => AddTopicrefAsync(Place.Child);

    private async Task AddTopicrefAsync(Place place)
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
        InsertAtSelection(pane, topicref, place);
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

    /// <summary>
    /// Новый элемент — после выбранного в дереве (или перед ним, или последним дочерним);
    /// без выбора или на корне — в конец карты.
    /// </summary>
    private void InsertAtSelection(IDocumentView pane, DitaNode element, Place place = Place.After)
    {
        var target = SelectedNode?.Item.Node ?? pane.Document.Root;
        if (IsMapRoot(target, pane) || place == Place.Child)
        {
            target.Add(element);
        }
        else
        {
            target.Parent?.Insert(target.IndexInParent + (place == Place.After ? 1 : 0), element);
        }
    }

    private static bool IsMapRoot(DitaNode node, IDocumentView pane) =>
        ReferenceEquals(node, pane.Document.Root) || node.Name is "map" or "bookmap" || node.Parent is null;

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
    private void Duplicate() => StructureOperation(node =>
    {
        if (node.Parent is null || node.Name is "map" or "bookmap")
        {
            return false;
        }

        node.Parent.Insert(node.IndexInParent + 1, node.CloneDeep());
        return true;
    }, "Дублирование в карте");

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

    // ------------------------------------------------ контекстное меню

    // Буфер карты: копия узла и карта, относительно которой записаны его ссылки.
    private (DitaNode Node, string MapPath)? _clipboard;

    public bool CanPaste => _clipboard is not null;

    private ProjectFile? SelectedFile =>
        SelectedNode?.Item.TargetPath is { } target ? _main.Project?.FindFile(target) : null;

    /// <summary>«Свойства»: карта открывается с выделенной строкой — её атрибуты на панели «Атрибуты».</summary>
    [RelayCommand]
    private void ShowProperties()
    {
        if (SelectedNode?.Item.Node is { } node && OpenMapPane() is { } pane)
        {
            pane.FocusNode(node);
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (SelectedNode?.Item.Node is not { Parent: not null } node || node.Name is "map" or "bookmap" || SelectedMap is not { } map)
        {
            return;
        }

        _clipboard = (node.CloneDeep(), map.FullPath);
        OnPropertyChanged(nameof(CanPaste));
        _main.StatusText = $"Скопировано: {SelectedNode.Title}";
    }

    [RelayCommand]
    private void Cut()
    {
        Copy();
        if (_clipboard is not null)
        {
            StructureOperation(EditCommands.Delete, "Вырезание из карты");
        }
    }

    [RelayCommand]
    private void Paste() => PasteAt(Place.After);

    [RelayCommand]
    private void PasteBefore() => PasteAt(Place.Before);

    [RelayCommand]
    private void PasteAsChild() => PasteAt(Place.Child);

    private void PasteAt(Place place)
    {
        if (_clipboard is not { } clip || SelectedMap is not { } map || OpenMapPane() is not { } pane)
        {
            return;
        }

        pane.PushUndo("Вставка в карту");
        var copy = clip.Node.CloneDeep();
        RebaseHrefs(copy, clip.MapPath, map.FullPath);
        InsertAtSelection(pane, copy, place);
        AfterMapEdit(pane);
    }

    /// <summary>Ссылки узла из другой карты пересчитываются от папки новой карты.</summary>
    private static void RebaseHrefs(DitaNode node, string fromMap, string toMap)
    {
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(fromMap)), Path.GetDirectoryName(Path.GetFullPath(toMap)), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var element in node.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element))
        {
            var href = element.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || RefResolver.IsExternal(href!) || element.GetAttribute("scope") is "external" or "peer")
            {
                continue;
            }

            var hash = href!.IndexOf('#');
            var filePart = hash >= 0 ? href[..hash] : href;
            if (filePart.Length == 0)
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fromMap))!, filePart));
            element.SetAttribute("href", RefResolver.MakeRelative(toMap, full) + (hash >= 0 ? href[hash..] : string.Empty));
        }
    }

    /// <summary>Все ссылки проекта на файл выбранной строки — на вкладку «Поиск».</summary>
    [RelayCommand]
    private void FindReferences()
    {
        if (_main.Project is not { } project || SelectedNode?.Item.TargetPath is not { } target)
        {
            return;
        }

        foreach (var pane in _main.Panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var hits = project.FindReferencesTo(target);
        _main.Search.ShowResults(hits);
        _main.BottomTabIndex = 1;
        _main.StatusText = $"Ссылок на {Path.GetFileName(target)}: {hits.Count}";
    }

    [RelayCommand]
    private async Task RenameFile()
    {
        if (SelectedFile is { } file)
        {
            await _main.ProjectPanel.MoveFileAsync(file);
            RebuildTree();
        }
    }

    /// <summary>Удаляет файл топика с диска и все строки этой карты, которые на него ссылаются.</summary>
    [RelayCommand]
    private async Task DeleteFile()
    {
        if (_main.Project is not { } project || SelectedFile is not { } file || SelectedMap is not { } map)
        {
            return;
        }

        var target = Path.GetFullPath(file.FullPath);
        var elsewhere = project.FindReferencesTo(target)
            .Where(h => !string.Equals(h.File.FullPath, map.FullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var warning = elsewhere.Count == 0
            ? string.Empty
            : $"\n\nЕщё {elsewhere.Count} ссыл. в других файлах ({string.Join(", ", elsewhere.Select(h => h.File.RelativePath).Distinct().Take(5))}) станут битыми.";
        if (!await _main.Dialogs.ConfirmAsync("Удаление файла",
                $"Удалить файл {file.RelativePath} с диска? Отменить это будет нельзя. Строки этой карты, которые на него ссылаются, будут убраны.{warning}"))
        {
            return;
        }

        if (OpenMapPane() is { } pane)
        {
            var rows = pane.Document.Root.DescendantsAndSelf()
                .Where(n => n.Kind == NodeKind.Element && n.GetAttribute("href") is { } href && !RefResolver.IsExternal(href) &&
                            RefResolver.Parse(map.FullPath, href).Path is { } path &&
                            string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (rows.Count > 0)
            {
                pane.PushUndo("Удаление файла из карты");
                foreach (var row in rows)
                {
                    // Дочерние строки удаляемой не теряются — поднимаются на её место.
                    var parent = row.Parent!;
                    var index = parent.IndexOf(row);
                    foreach (var child in row.ElementChildren().Where(c => c.Name is not "topicmeta").ToList())
                    {
                        child.RemoveSelf();
                        parent.Insert(++index, child);
                    }

                    row.RemoveSelf();
                }

                AfterMapEdit(pane);
            }
        }

        if (_main.ProjectPanel.DeleteFile(file) is { } error)
        {
            await _main.Dialogs.MessageAsync("Удаление файла", error);
            return;
        }

        RebuildTree();
        _main.StatusText = $"Файл {file.RelativePath} удалён.";
    }

    /// <summary>
    /// Флажок у строки карты: снят — топик (и вся ветка) не публикуется ни в один формат
    /// (processing-role="resource-only", ключи и conref из него работают); установлен — снова
    /// публикуется (атрибут снимается, а если ветка выше исключена — "normal").
    /// </summary>
    [RelayCommand]
    private void TogglePublished(MapTreeNode? node)
    {
        if (node is not { CanExclude: true } || OpenMapPane(activate: false) is not { } pane)
        {
            return;
        }

        var publish = !node.IsPublished;
        var element = node.Item.Node;
        pane.PushUndo(publish ? "Включение топика в публикацию" : "Исключение топика из публикации");
        if (publish)
        {
            element.RemoveAttribute("processing-role");
            if (node.Item.IsResourceOnly)
            {
                element.SetAttribute("processing-role", "normal"); // исключена ветка выше
            }
        }
        else
        {
            element.SetAttribute("processing-role", "resource-only");
        }

        AfterMapEdit(pane);
        _main.StatusText = publish
            ? $"«{node.Title}» снова публикуется."
            : $"«{node.Title}» не публикуется (ни в HTML, ни в PDF, ни в DOCX); ссылки и ключи из него работают.";
    }

    [RelayCommand]
    private void ExpandAll() => SetExpanded(true);

    [RelayCommand]
    private void CollapseAll() => SetExpanded(false);

    private void SetExpanded(bool expanded)
    {
        foreach (var root in Tree)
        {
            foreach (var node in Flatten(root))
            {
                // Корень карты не сворачивается — иначе дерево выглядит пустым.
                node.IsExpanded = expanded || ReferenceEquals(node, root);
            }
        }
    }

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
