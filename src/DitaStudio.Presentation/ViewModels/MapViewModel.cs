using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Templates;
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
            _main.StatusText = value.IsBroken
                ? $"{value.Item.ElementName}: {value.Title} — {value.Item.BrokenReason}"
                : $"{value.Item.ElementName}: {value.Title}";
        }

        // Что доступно строке, зависит от её вида: у раздела нет файла, у корня карты нет родителя.
        OnPropertyChanged(nameof(SelectedHasFile));
        OnPropertyChanged(nameof(SelectedIsBroken));
        CreateMissingFileCommand.NotifyCanExecuteChanged();
        ReplaceFileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedPageBreakBefore));
        OnPropertyChanged(nameof(SelectedPageBreakNone));
        OnPropertyChanged(nameof(HasStructureSelection));
        FindReferencesCommand.NotifyCanExecuteChanged();
        TogglePageBreakBeforeCommand.NotifyCanExecuteChanged();
        ToggleNoPageBreakCommand.NotifyCanExecuteChanged();
        RenameFileCommand.NotifyCanExecuteChanged();
        DeleteFileCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        IndentCommand.NotifyCanExecuteChanged();
        OutdentCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
    }

    /// <summary>У выбранной строки есть файл проекта (топик, вложенная карта). У раздела
    /// (<c>topichead</c>), группы, ключа без файла и битой ссылки его нет — команды «Найти ссылки»,
    /// «Переименовать / переместить файл», «Удалить файл» им недоступны, а раздел убирается из карты
    /// командой «Убрать из карты».</summary>
    public bool SelectedHasFile => SelectedFile is not null;

    /// <summary>Выбрана строка, которую можно переставить, скопировать, дублировать и убрать:
    /// любая, кроме корня карты.</summary>
    public bool HasStructureSelection => SelectedNode is { IsRoot: false };

    /// <summary>Окну нужно показать строку (она может быть за краем прокрутки): после добавления новой строки.</summary>
    public event Action<MapTreeNode>? RevealRequested;

    /// <summary>Выбирает в дереве строку по узлу документа (после перестроения дерева).</summary>
    private void Select(DitaNode node, bool reveal = false)
    {
        SelectedNode = Tree.SelectMany(Flatten).FirstOrDefault(n => ReferenceEquals(n.Item.Node, node));
        if (reveal && SelectedNode is { } selected)
        {
            RevealRequested?.Invoke(selected);
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

    /// <summary>Строка ссылается на файл, которого нет (красная в дереве).</summary>
    public bool SelectedIsBroken => SelectedNode?.IsBroken == true;

    /// <summary>
    /// Двойной щелчок: открывает топик узла. У «битой» строки (файла нет) карту молча не открывает, а объясняет причину и
    /// предлагает исправить: создать файл по ссылке, выбрать другой файл или убрать строку. Если топика нет и строка не
    /// битая (раздел), открывается сама карта.
    /// </summary>
    [RelayCommand]
    private async Task OpenSelected()
    {
        if (SelectedNode?.Item is { IsBroken: true })
        {
            await FixBrokenAsync();
        }
        else if (SelectedNode?.Item.TargetPath is { } target && File.Exists(target))
        {
            _main.OpenDocument?.Invoke(target);
        }
        else if (SelectedMap is { } map)
        {
            _main.OpenDocument?.Invoke(map.FullPath);
        }
    }

    private const string ChooseCreate = "Создать файл по ссылке";
    private const string ChooseReplace = "Выбрать другой файл…";
    private const string ChooseRemove = "Убрать строку из карты";

    /// <summary>Диалог исправления «битой» строки: причина ошибки и три способа её убрать.</summary>
    private async Task FixBrokenAsync()
    {
        if (SelectedNode?.Item is not { IsBroken: true } item)
        {
            return;
        }

        var options = new List<string>();
        if (item.TargetPath is not null && !File.Exists(item.TargetPath))
        {
            options.Add(ChooseCreate);
        }

        options.Add(ChooseReplace);
        options.Add(ChooseRemove);
        var choice = await _main.Dialogs.PickOneAsync("Топик не найден", item.BrokenReason + "\n\nЧто сделать со строкой «" + item.Title + "»?", options, o => o);
        switch (choice)
        {
            case ChooseCreate:
                await CreateMissingFileAsync();
                break;
            case ChooseReplace:
                await ReplaceFileAsync();
                break;
            case ChooseRemove:
                await DeleteCommand.ExecuteAsync(null);
                break;
        }
    }

    /// <summary>Создаёт пустой топик по ссылке битой строки (файла по <c>href</c> нет) и открывает его.</summary>
    [RelayCommand(CanExecute = nameof(SelectedIsBroken))]
    private async Task CreateMissingFileAsync()
    {
        var project = _main.Project;
        if (project is null || SelectedNode?.Item is not { IsBroken: true, TargetPath: { } target } item || File.Exists(target))
        {
            await _main.Dialogs.MessageAsync("Создание файла", "Файл по ссылке создать нельзя: у строки нет пути к файлу (ключ не определён). Выберите другой файл.");
            return;
        }

        // Название строки в карте (navtitle) — заголовок нового топика; без него — имя файла словами.
        var title = item.Node.GetAttribute("navtitle") is { Length: > 0 } navAttribute ? navAttribute
            : item.Node.FirstElement("topicmeta")?.FirstElement("navtitle")?.InnerText.Trim() is { Length: > 0 } navtitle ? navtitle
            : Path.GetFileNameWithoutExtension(target).Replace('_', ' ').Replace('-', ' ');
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            DocumentTemplates.Create("topic", title, DocumentTemplates.SuggestId(title, "topic")).Save(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _main.Dialogs.MessageAsync("Создание файла", $"Не удалось создать {target}: {ex.Message}");
            return;
        }

        project.AddFile(target);
        RebuildTree();
        _main.RefreshProjectTree?.Invoke();
        _main.StatusText = $"Создан файл по ссылке: {Path.GetFileName(target)}.";
        _main.OpenDocument?.Invoke(target);
    }

    /// <summary>Заменяет ссылку битой строки на выбранный файл проекта (<c>href</c> пересчитывается от файла карты).</summary>
    [RelayCommand(CanExecute = nameof(SelectedIsBroken))]
    private async Task ReplaceFileAsync()
    {
        var project = _main.Project;
        if (project is null || SelectedNode?.Item is not { IsBroken: true } item)
        {
            return;
        }

        var file = await _main.Files.OpenFileAsync("Выберите файл для строки «" + item.Title + "»",
            new[] { new FileFilter("Топики и карты DITA", "*.dita", "*.xml", "*.ditamap"), FileFilter.All }, project.RootPath);
        if (file is null)
        {
            return;
        }

        var ownerMap = item.MapPath;
        StructureOperation(node =>
        {
            node.SetAttribute("href", RefResolver.MakeRelative(ownerMap, file));
            node.RemoveAttribute("keyref");
            return true;
        }, "Замена файла строки карты");
        _main.StatusText = $"Строка «{item.Title}» теперь ссылается на {Path.GetFileName(file)}.";
    }

    /// <param name="activate">false — карта открывается во вкладке, но вкладка не выбирается: правка
    /// вроде флажка «публиковать» не должна уводить пользователя от документа, над которым он работает.</param>
    /// <param name="mapPath">Файл карты, в котором лежит правимая строка: у строк вложенной карты
    /// (<c>mapref</c>) это файл вложенной карты, а не выбранной. Правка идёт в его документ — иначе
    /// изменился бы узел, которого нет в открытой карте, и правка пропала бы. null — выбранная карта.</param>
    private IDocumentView? OpenMapPane(bool activate = true, string? mapPath = null)
    {
        if (SelectedMap is not { } map)
        {
            return null;
        }

        var previous = _main.Documents.SelectedTab;
        var pane = _main.OpenDocument?.Invoke(mapPath ?? map.FullPath);
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

        var ownerMap = SelectedNode?.Item.MapPath ?? map.FullPath;
        var pane = OpenMapPane(mapPath: ownerMap);
        if (pane is null)
        {
            return;
        }

        if (!CanInsertAtSelection(pane, "topicref", place, out var reason))
        {
            _main.StatusText = reason;
            return;
        }

        pane.PushUndo("Добавление ссылки в карту");
        var topicref = DitaNode.Element("topicref");
        topicref.SetAttribute("href", RefResolver.MakeRelative(ownerMap, result.File.FullPath));
        InsertAtSelection(pane, topicref, place);
        AfterMapEdit(pane);
        Select(topicref, reveal: true);
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

        var ownerMap = SelectedNode?.Item.MapPath ?? map.FullPath;
        var pane = OpenMapPane(mapPath: ownerMap);
        if (pane is null)
        {
            return false;
        }

        pane.PushUndo("Добавление нового топика в карту");
        var topicref = DitaNode.Element("topicref");
        topicref.SetAttribute("href", RefResolver.MakeRelative(ownerMap, path));
        InsertAtSelection(pane, topicref);
        AfterMapEdit(pane);

        Select(topicref, reveal: true);
        return true;
    }

    private static IEnumerable<MapTreeNode> Flatten(MapTreeNode node) =>
        node.Children.SelectMany(Flatten).Prepend(node);

    [RelayCommand]
    private void AddTopichead()
    {
        var pane = OpenMapPane(mapPath: SelectedNode?.Item.MapPath);
        if (pane is null)
        {
            return;
        }

        if (!CanInsertAtSelection(pane, "topichead", Place.After, out var reason))
        {
            _main.StatusText = reason;
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
        Select(topichead, reveal: true); // следующая команда меню («Убрать из карты», «Дублировать») действует на новый раздел
    }

    /// <summary>
    /// Новый элемент — после выбранного в дереве (или перед ним, или последним дочерним);
    /// без выбора или на корне — в конец карты.
    /// </summary>
    /// <summary>Можно ли поставить элемент в то место, куда его поставит <see cref="InsertAtSelection"/>:
    /// по контент-модели карты. Причина отказа — для строки состояния.</summary>
    private bool CanInsertAtSelection(IDocumentView pane, string elementName, Place place, out string reason)
    {
        var target = SelectedNode?.Item.Node ?? pane.Document.Root;
        var position = IsMapRoot(target, pane) || place == Place.Child ? DropPosition.Child
            : place == Place.After ? DropPosition.After : DropPosition.Before;
        return MapMoves.CanPlace(elementName, target, position, null, out reason);
    }

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

        if (!MapMoves.Try(item.Node, operation, out var reason))
        {
            _main.StatusText = reason;
            return;
        }

        var pane = OpenMapPane(mapPath: item.MapPath);
        if (pane is null)
        {
            return;
        }

        pane.PushUndo(description);
        operation(item.Node);
        AfterMapEdit(pane);
    }

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
    private void MoveUp() => StructureOperation(EditCommands.MoveUp, "Перемещение в карте");

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
    private void MoveDown() => StructureOperation(EditCommands.MoveDown, "Перемещение в карте");

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
    private async Task Delete()
    {
        if (SelectedNode?.Item is not { } item ||
            !await _main.Dialogs.ConfirmAsync("Карта", $"Убрать «{item.Title}» из карты?"))
        {
            return;
        }

        StructureOperation(EditCommands.Delete, "Удаление из карты");
    }

    /// <summary>Выбранный топик начинается с новой страницы (отметка в меню).</summary>
    public bool SelectedPageBreakBefore => TopicPageBreak.Of(SelectedNode?.Item.Node) == true;

    /// <summary>Выбранный топик не начинается с новой страницы, даже если так задано в оформлении DOCX.</summary>
    public bool SelectedPageBreakNone => TopicPageBreak.Of(SelectedNode?.Item.Node) == false;

    [RelayCommand(CanExecute = nameof(SelectedHasFile))]
    private void TogglePageBreakBefore() => SetPageBreak(SelectedPageBreakBefore ? null : true);

    [RelayCommand(CanExecute = nameof(SelectedHasFile))]
    private void ToggleNoPageBreak() => SetPageBreak(SelectedPageBreakNone ? null : false);

    private void SetPageBreak(bool? value)
    {
        StructureOperation(node =>
        {
            TopicPageBreak.Set(node, value);
            return true;
        }, "Разрыв страницы перед топиком");
        _main.StatusText = value switch
        {
            true => "Топик будет начинаться с новой страницы (DOCX, PDF, единый HTML).",
            false => "Топик не будет начинаться с новой страницы, даже если так задано в оформлении DOCX.",
            _ => "Разрыв страницы перед топиком снят: как задано в оформлении DOCX."
        };
    }

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
    private void Duplicate() => StructureOperation(node =>
    {
        if (node.Parent is null || node.Name is "map" or "bookmap")
        {
            return false;
        }

        node.Parent.Insert(node.IndexInParent + 1, node.CloneDeep());
        return true;
    }, "Дублирование в карте");

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
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

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
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
        if (SelectedNode?.Item.Node is { } node && OpenMapPane(mapPath: SelectedNode.Item.MapPath) is { } pane)
        {
            pane.FocusNode(node);
        }
    }

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
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

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
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
        var ownerMap = SelectedNode?.Item.MapPath ?? SelectedMap?.FullPath;
        if (_clipboard is not { } clip || ownerMap is null || OpenMapPane(mapPath: ownerMap) is not { } pane)
        {
            return;
        }

        if (!CanInsertAtSelection(pane, clip.Node.Name, place, out var reason))
        {
            _main.StatusText = reason;
            return;
        }

        pane.PushUndo("Вставка в карту");
        var copy = clip.Node.CloneDeep();
        RebaseHrefs(copy, clip.MapPath, ownerMap);
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
    [RelayCommand(CanExecute = nameof(SelectedHasFile))]
    private void FindReferences()
    {
        if (_main.Project is not { } project || SelectedNode?.Item.TargetPath is not { } target)
        {
            NoFileMessage();
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

    /// <summary>Команда, которой нужен файл, запущена на строке без файла (не из меню — там она
    /// недоступна): объясняем, а не молчим.</summary>
    private void NoFileMessage() =>
        _main.StatusText = SelectedNode is null
            ? "Выберите строку карты."
            : $"У строки «{SelectedNode.Title}» нет файла. Чтобы убрать её из карты, выберите «Убрать из карты».";

    [RelayCommand(CanExecute = nameof(SelectedHasFile))]
    private async Task RenameFile()
    {
        if (SelectedFile is { } file)
        {
            await _main.ProjectPanel.MoveFileAsync(file);
            RebuildTree();
        }
    }

    /// <summary>Удаляет файл топика с диска и все строки этой карты, которые на него ссылаются.</summary>
    [RelayCommand(CanExecute = nameof(SelectedHasFile))]
    private async Task DeleteFile()
    {
        if (_main.Project is not { } project || SelectedFile is not { } file || SelectedMap is not { } map)
        {
            NoFileMessage();
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
        if (node is not { CanExclude: true } || OpenMapPane(activate: false, mapPath: node.Item.MapPath) is not { } pane)
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

    /// <summary>Можно ли бросить <paramref name="dragged"/> на <paramref name="target"/> в указанное
    /// место — по контент-модели, без переноса в собственную ветку и между разными файлами карты.
    /// Причина отказа — для подсказки у указателя.</summary>
    public static bool CanDrop(MapTreeNode dragged, MapTreeNode target, DropPosition position, out string reason) =>
        MapMoves.CanMove(dragged.Item.Node, target.Item.Node, position, out reason);

    /// <summary>
    /// Перетаскивание: строка встаёт перед или после цели на её уровне либо становится последней
    /// дочерней цели (перенос в другого родителя и на другой уровень). Перенос — одна правка карты,
    /// один шаг отмены; перенесённая строка остаётся выделенной, а цель раскрывается, если в неё вложили.
    /// </summary>
    public void MoveByDrag(MapTreeNode dragged, MapTreeNode target, DropPosition position)
    {
        if (ReferenceEquals(dragged, target))
        {
            return;
        }

        if (!CanDrop(dragged, target, position, out var reason))
        {
            _main.StatusText = reason;
            return;
        }

        var pane = OpenMapPane(mapPath: dragged.Item.MapPath);
        if (pane is null)
        {
            return;
        }

        var moved = dragged.Item.Node;
        var container = target.Item.Node;
        pane.PushUndo("Перестановка в карте");
        MapMoves.Move(moved, container, position);
        AfterMapEdit(pane);
        if (position == DropPosition.Child && Tree.SelectMany(Flatten).FirstOrDefault(n => ReferenceEquals(n.Item.Node, container)) is { } parent)
        {
            parent.IsExpanded = true;
        }

        Select(moved);
        _main.StatusText = position == DropPosition.Child
            ? $"«{dragged.Title}» теперь вложен в «{target.Title}»."
            : $"«{dragged.Title}» перенесён.";
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
