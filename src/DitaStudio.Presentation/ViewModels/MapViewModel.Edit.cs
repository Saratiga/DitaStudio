using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Services;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

// Правка структуры карты: добавление ссылок и разделов, перестановка, вложение, дублирование, перетаскивание.
public partial class MapViewModel
{
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
        var project = _workspace.Project;
        if (project is null || SelectedMap is not { } map)
        {
            return;
        }

        var result = await _ui.Dialogs.InsertXrefAsync(project, map.FullPath);
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
            _shell.StatusText = reason;
            return;
        }

        pane.PushUndo(Loc.T("Msg_AddReferenceToTheMap"));
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
        if (_workspace.Project is null || SelectedMap is not { } map)
        {
            return false;
        }

        var ownerMap = SelectedNode?.Item.MapPath ?? map.FullPath;
        var pane = OpenMapPane(mapPath: ownerMap);
        if (pane is null)
        {
            return false;
        }

        pane.PushUndo(Loc.T("Msg_AddNewTopicToTheMap"));
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
            _shell.StatusText = reason;
            return;
        }

        pane.PushUndo(Loc.T("Msg_AddSectionToTheMap"));
        var topichead = DitaNode.Element("topichead");
        var meta = DitaNode.Element("topicmeta");
        var navtitle = DitaNode.Element("navtitle");
        navtitle.SetText(Loc.T("Msg_NewSection"));
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
            _shell.StatusText = reason;
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
    private void MoveUp() => StructureOperation(EditCommands.MoveUp, Loc.T("Msg_MoveInTheMap"));

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
    private void MoveDown() => StructureOperation(EditCommands.MoveDown, Loc.T("Msg_MoveInTheMap"));

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
    private async Task Delete()
    {
        if (SelectedNode?.Item is not { } item ||
            !await _ui.Dialogs.ConfirmAsync(Loc.T("Tab_Map"), Loc.T("Msg_Remove0FromTheMap", item.Title)))
        {
            return;
        }

        StructureOperation(EditCommands.Delete, Loc.T("Msg_RemoveFromTheMap"));
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
        }, Loc.T("Msg_PageBreakBeforeTheTopic"));
        _shell.StatusText = value switch
        {
            true => Loc.T("Msg_TheTopicWillStartOnA"),
            false => Loc.T("Msg_TheTopicWillNotStartOn"),
            _ => Loc.T("Msg_ThePageBreakBeforeTheTopic")
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
    }, Loc.T("Msg_DuplicateInTheMap"));

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
    }, Loc.T("Msg_NestInTheMap"));

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
    }, Loc.T("Msg_MoveOutOfTheNesting"));

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
            _shell.StatusText = reason;
            return;
        }

        var pane = OpenMapPane(mapPath: dragged.Item.MapPath);
        if (pane is null)
        {
            return;
        }

        var moved = dragged.Item.Node;
        var container = target.Item.Node;
        pane.PushUndo(Loc.T("Msg_ReorderInTheMap"));
        MapMoves.Move(moved, container, position);
        AfterMapEdit(pane);
        if (position == DropPosition.Child && Tree.SelectMany(Flatten).FirstOrDefault(n => ReferenceEquals(n.Item.Node, container)) is { } parent)
        {
            parent.IsExpanded = true;
        }

        Select(moved);
        _shell.StatusText = position == DropPosition.Child
            ? Loc.T("Msg_0IsNowNestedIn1", dragged.Title, target.Title)
            : Loc.T("Msg_0WasMoved", dragged.Title);
    }
}
