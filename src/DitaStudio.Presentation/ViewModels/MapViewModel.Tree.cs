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

// Дерево выбранной карты: построение, флажок «публиковать», раскрытие.
public partial class MapViewModel
{
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
        var project = _workspace.Project;
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
            _shell.StatusText = $"Карта не читается: {ex.Message}";
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
        _shell.StatusText = publish
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
}
