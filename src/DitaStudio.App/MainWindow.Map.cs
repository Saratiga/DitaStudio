using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DitaStudio.App.Authoring;
using DitaStudio.App.Views;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.App;

// Вкладка «Карта»: дерево карты, перестановка/вложение через кнопки и drag-and-drop,
// добавление ссылок на топики и разделов.
public partial class MainWindow
{
    private void BuildMapSelector()
    {
        if (_project is null)
        {
            return;
        }

        var maps = _project.Maps.ToList();
        MapSelector.ItemsSource = maps;
        if (maps.Count > 0)
        {
            MapSelector.SelectedIndex = 0;
        }
        else
        {
            MapTreeView.Items.Clear();
        }
    }

    private void OnMapSelected(object sender, SelectionChangedEventArgs e) => BuildMapTree();

    private void BuildMapTree()
    {
        MapTreeView.Items.Clear();
        if (_project is null || MapSelector.SelectedItem is not ProjectFile map)
        {
            return;
        }

        try
        {
            _mapTree = MapTree.Build(_project, map.FullPath);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Карта не читается: {ex.Message}");
            return;
        }

        var rootItem = BuildMapItem(_mapTree.Root, true);
        MapTreeView.Items.Add(rootItem);
    }

    private TreeViewItem BuildMapItem(MapItem item, bool isRoot)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };

        header.Children.Add(new TextBlock
        {
            Text = isRoot ? "🗺" : item.IsResourceOnly ? "🔑" : item.TargetPath is null ? "▸" : "📄",
            Margin = new Thickness(0, 0, 6, 0)
        });

        header.Children.Add(new TextBlock
        {
            Text = item.Title,
            Foreground = item.IsBroken
                ? ThemeManager.Brush("Danger")
                : ThemeManager.Brush("TextPrimary")
        });

        header.Children.Add(new TextBlock
        {
            Text = "  " + item.ElementName,
            FontFamily = InlineStyles.Mono,
            FontSize = 10,
            Foreground = ThemeManager.Brush("TagBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var node = new TreeViewItem { Header = header, Tag = item, IsExpanded = true };
        foreach (var child in item.Children)
        {
            node.Items.Add(BuildMapItem(child, false));
        }

        return node;
    }

    private MapItem? SelectedMapItem => MapTreeView.SelectedItem is TreeViewItem { Tag: MapItem item } ? item : null;

    private void OnMapTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var item = SelectedMapItem;
        if (item is not null)
        {
            UpdateStatus($"{item.ElementName}: {item.Title}{(item.IsBroken ? " — файл не найден" : string.Empty)}");
        }
    }

    private void OnMapTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = SelectedMapItem;
        if (item?.TargetPath is not null && File.Exists(item.TargetPath))
        {
            OpenDocument(item.TargetPath);
        }
        else if (MapSelector.SelectedItem is ProjectFile map)
        {
            OpenDocument(map.FullPath);
        }
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
    {
        while (source is not null && source is not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as TreeViewItem;
    }

    private void OnMapTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _mapDragStart = e.GetPosition(MapTreeView);
        _mapDragCandidate = FindTreeViewItem(e.OriginalSource as DependencyObject)?.Tag as MapItem;
    }

    private void OnMapTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _mapDragCandidate is null)
        {
            return;
        }

        var position = e.GetPosition(MapTreeView);
        if (Math.Abs(position.X - _mapDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _mapDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _mapDragCandidate;
        _mapDragCandidate = null;
        if (dragged.Node.Parent is null)
        {
            return;
        }

        DragDrop.DoDragDrop(MapTreeView, dragged, DragDropEffects.Move);
    }

    private void OnMapTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(MapItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnMapTreeDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(MapItem)) is not MapItem dragged)
        {
            return;
        }

        var targetItem = FindTreeViewItem(e.OriginalSource as DependencyObject);
        if (targetItem?.Tag is not MapItem target || ReferenceEquals(target, dragged))
        {
            return;
        }

        var parent = dragged.Node.Parent;
        if (parent is null || !ReferenceEquals(target.Node.Parent, parent))
        {
            UpdateStatus("Перетаскивание допустимо только между элементами одного уровня.");
            return;
        }

        // ActualHeight включает вложенные раскрытые элементы, поэтому сравниваем с примерной
        // высотой одной строки заголовка, а не с половиной всей высоты TreeViewItem.
        var before = e.GetPosition(targetItem).Y < 12;

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        pane.PushUndo("Перестановка в карте");
        parent.Remove(dragged.Node);
        var targetIndex = parent.IndexOf(target.Node);
        parent.Insert(before ? targetIndex : targetIndex + 1, dragged.Node);

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        UpdateTabHeaders();
        BuildMapTree();
    }

    private DocumentPane? OpenMapPane()
    {
        if (MapSelector.SelectedItem is not ProjectFile map)
        {
            return null;
        }

        return OpenDocument(map.FullPath);
    }

    private void OnMapAddTopicref(object sender, RoutedEventArgs e)
    {
        if (_project is null || MapSelector.SelectedItem is not ProjectFile map)
        {
            return;
        }

        var result = Dialogs.InsertXref(_project, map.FullPath);
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

        var target = SelectedMapItem?.Node ?? pane.Document.Root;
        if (ReferenceEquals(target, pane.Document.Root) || target.Name is "map" or "bookmap")
        {
            target.Add(topicref);
        }
        else
        {
            target.Parent?.Insert(target.IndexInParent + 1, topicref);
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        UpdateTabHeaders();
        BuildMapTree();
    }

    private void OnMapAddTopichead(object sender, RoutedEventArgs e)
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

        var target = SelectedMapItem?.Node ?? pane.Document.Root;
        if (ReferenceEquals(target, pane.Document.Root) || target.Name is "map" or "bookmap")
        {
            target.Add(topichead);
        }
        else
        {
            target.Parent?.Insert(target.IndexInParent + 1, topichead);
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        UpdateTabHeaders();
        BuildMapTree();
    }

    private void MapStructureOperation(Func<DitaNode, bool> operation, string description)
    {
        var item = SelectedMapItem;
        if (item is null)
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
            UpdateStatus("Операция здесь недоступна.");
            return;
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        UpdateTabHeaders();
        BuildMapTree();
    }

    private void OnMapMoveUp(object sender, RoutedEventArgs e) =>
        MapStructureOperation(EditCommands.MoveUp, "Перемещение в карте");

    private void OnMapMoveDown(object sender, RoutedEventArgs e) =>
        MapStructureOperation(EditCommands.MoveDown, "Перемещение в карте");

    private void OnMapDelete(object sender, RoutedEventArgs e)
    {
        var item = SelectedMapItem;
        if (item is null || !Dialogs.Confirm("Карта", $"Убрать «{item.Title}» из карты?"))
        {
            return;
        }

        MapStructureOperation(EditCommands.Delete, "Удаление из карты");
    }

    private void OnMapIndent(object sender, RoutedEventArgs e) =>
        MapStructureOperation(node =>
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

    private void OnMapOutdent(object sender, RoutedEventArgs e) =>
        MapStructureOperation(node =>
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

    // ------------------------------------------------ таблица соответствий

    private void OnEditRelTable(object sender, RoutedEventArgs e)
    {
        if (_project is null || MapSelector.SelectedItem is not ProjectFile map)
        {
            return;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        var existingReltable = pane.Document.Root.FirstElement("reltable");
        var initialRows = ParseRelTable(_project, existingReltable, map.FullPath);

        var rows = Dialogs.EditRelTable(_project, initialRows);
        if (rows is null)
        {
            return;
        }

        pane.PushUndo("Таблица соответствий");

        var newReltable = BuildRelTableNode(rows, map.FullPath);
        if (existingReltable is not null)
        {
            existingReltable.ReplaceWith(newReltable);
        }
        else
        {
            pane.Document.Root.Add(newReltable);
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        UpdateTabHeaders();
        UpdateStatus("Таблица соответствий обновлена.");
    }

    private static List<List<Dialogs.RelTableCell>> ParseRelTable(DitaProject project, DitaNode? reltable, string mapPath)
    {
        var rows = new List<List<Dialogs.RelTableCell>>();
        if (reltable is null)
        {
            return rows;
        }

        foreach (var relrow in reltable.ElementChildren().Where(n => n.Name == "relrow"))
        {
            var row = new List<Dialogs.RelTableCell>();
            foreach (var relcell in relrow.ElementChildren().Where(n => n.Name == "relcell"))
            {
                var topicref = relcell.FirstElement("topicref");
                var href = topicref?.GetAttribute("href");
                var cell = new Dialogs.RelTableCell();

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

    private static DitaNode BuildRelTableNode(List<List<Dialogs.RelTableCell>> rows, string mapPath)
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
                    var topicref = DitaNode.Element("topicref");
                    var href = RefResolver.MakeRelative(mapPath, cell.File.FullPath);
                    if (!string.IsNullOrEmpty(cell.TopicId))
                    {
                        href += "#" + cell.TopicId;
                    }

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
