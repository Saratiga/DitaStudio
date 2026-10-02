using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.ViewModels;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Authoring;

/// <summary>
/// «Автор» карты: список всех топиков по иерархии — заголовок, файл, значок; «битые» строки красным с причиной в подсказке.
/// Двойной щелчок открывает топик (у «битой» строки — объясняет ошибку через карту слева). Структуру правят кнопками
/// и меню панели «Карта»; здесь она только показывается и сразу обновляется.
/// </summary>
public sealed class MapOutlineView : UserControl
{
    private readonly DitaProject _project;
    private readonly DitaDocument _document;
    private readonly TreeView _tree = new();
    private readonly TextBlock _summary = new() { Margin = new Thickness(12, 8, 12, 6), FontSize = 12 };

    public MapOutlineView(DitaProject project, DitaDocument document)
    {
        _project = project;
        _document = document;
        Name = "MapOutline";

        _tree.ItemTemplate = new FuncTreeDataTemplate<MapTreeNode>(BuildRow, node => node.Children);
        _tree.DoubleTapped += (_, _) =>
        {
            if (_tree.SelectedItem is MapTreeNode { Item: { TargetPath: { } target, IsBroken: false } })
            {
                OpenRequested?.Invoke(target);
            }
        };
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _tree.SelectedItem is MapTreeNode { Item: { TargetPath: { } target, IsBroken: false } })
            {
                OpenRequested?.Invoke(target);
                e.Handled = true;
            }
        };

        _summary.Bind(TextBlock.ForegroundProperty, _summary.GetResourceObservable("TextMuted"));
        DockPanel.SetDock(_summary, Dock.Top);
        Content = new DockPanel { Children = { _summary, _tree } };
        Refresh();
    }

    /// <summary>Просьба открыть файл топика (двойной щелчок по строке).</summary>
    public event Action<string>? OpenRequested;

    /// <summary>Строки верхнего уровня (корень карты) — для тестов.</summary>
    public IReadOnlyList<MapTreeNode> Roots => (_tree.ItemsSource as IReadOnlyList<MapTreeNode>) ?? Array.Empty<MapTreeNode>();

    /// <summary>Построить список заново из текущего состояния карты (в том числе ещё не сохранённого).</summary>
    public void Refresh()
    {
        MapTree tree;
        try
        {
            tree = _document.FilePath is { } path ? MapTree.Build(_project, path) : throw new InvalidOperationException(Loc.T("Author_TheMapIsNotSaved"));
        }
        catch (Exception ex)
        {
            _tree.ItemsSource = null;
            _summary.Text = Loc.T("Author_TheMapCannotBeRead") + ex.Message;
            return;
        }

        var selected = (_tree.SelectedItem as MapTreeNode)?.Item.Node;
        var root = Convert(tree.Root, isRoot: true);
        _tree.ItemsSource = new List<MapTreeNode> { root };

        var items = tree.Items.Where(i => !i.IsResourceOnly).ToList();
        var topics = items.Count(i => i.TargetPath is not null);
        var broken = items.Count(i => i.IsBroken);
        _summary.Text = Loc.T("Author_TopicsInTheMap0", topics) + (broken > 0 ? Loc.T("Author_FilesNotFound0", broken) : string.Empty) +
                        Loc.T("Author_DoubleClickOpensATopicEdit");
        if (selected is not null)
        {
            _tree.SelectedItem = Flatten(root).FirstOrDefault(n => ReferenceEquals(n.Item.Node, selected));
        }
    }

    private static MapTreeNode Convert(MapItem item, bool isRoot)
    {
        var node = new MapTreeNode(item, isRoot);
        foreach (var child in item.Children)
        {
            node.Children.Add(Convert(child, isRoot: false));
        }

        return node;
    }

    private static IEnumerable<MapTreeNode> Flatten(MapTreeNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static Control BuildRow(MapTreeNode node, Avalonia.Controls.INameScope scope)
    {
        var title = new TextBlock { Text = node.Title, VerticalAlignment = VerticalAlignment.Center };
        if (node.IsBroken)
        {
            title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("Danger"));
            title.TextDecorations = TextDecorations.Underline;
        }
        else if (node.IsExcluded)
        {
            title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("TextDisabled"));
            title.TextDecorations = TextDecorations.Strikethrough;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = Brushes.Transparent };
        row.Children.Add(new TextBlock { Text = node.Icon, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(title);
        if (node.Item.TargetPath is { } target)
        {
            var file = new TextBlock { Text = Path.GetFileName(target), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            file.Bind(TextBlock.ForegroundProperty, file.GetResourceObservable("TextMuted"));
            row.Children.Add(file);
        }

        if (node.ToolTip is { Length: > 0 } reason)
        {
            ToolTip.SetTip(row, reason);
        }

        return row;
    }
}
