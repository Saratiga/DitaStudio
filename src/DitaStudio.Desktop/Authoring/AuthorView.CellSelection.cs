using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Authoring;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Authoring;

// Выделение ячеек CALS-таблицы мышью (прямоугольник, как в Word): подсветка, протяжка от ячейки к ячейке, Shift+щелчок, Delete и меню
// действий. Сами действия (строки, столбцы, объединение, границы…) — в AuthorSurfaceBase.Cells.
public sealed partial class AuthorView
{
    // Подсветка выделенной ячейки: поверх содержимого — полупрозрачная заливка акцентным цветом. Рамка ячейки и её фон не трогаются,
    // поэтому снять подсветку — вернуть содержимое на место.
    private sealed class CellTint
    {
        public required Border Cell { get; init; }

        public required Control Content { get; init; }

        public required Grid Host { get; init; }
    }

    private readonly List<CellTint> _tints = new();
    private DitaNode? _cellTable;
    private CellRange _cellRange;
    private bool _cellDragging;

    /// <summary>Выделенные ячейки: таблица и прямоугольник; null — ячейки не выделены.</summary>
    public (DitaNode Table, CellRange Range)? SelectedCells => _cellTable is { } table ? (table, _cellRange) : null;

    /// <summary>Выделенные ячейки (узлы <c>entry</c>) по порядку; пусто — не выделены.</summary>
    public IReadOnlyList<DitaNode> SelectedCellEntries =>
        _cellTable is { } table && CellGrid.Of(table) is { } grid ? grid.CellsIn(_cellRange).Select(c => c.Entry).ToList() : Array.Empty<DitaNode>();

    /// <summary>Выделяет прямоугольник ячеек (расширяется до целых объединённых ячеек). false — нет такой таблицы или ячеек.</summary>
    public bool SelectCellRange(DitaNode table, CellRange range, bool focus = false)
    {
        if (CellGrid.Of(table) is not { } grid)
        {
            return false;
        }

        range = grid.Expand(range);
        var cells = grid.CellsIn(range);
        var borders = cells.Select(c => (Cell: c, Border: FindBlockBorder(c.Entry))).Where(x => x.Border is not null).ToList();
        if (borders.Count == 0)
        {
            return false;
        }

        ClearCellSelection();
        ClearOutline();
        foreach (var editor in _order)
        {
            editor.TextArea.ClearSelection();
        }

        foreach (var (cell, border) in borders)
        {
            var content = border!.Child;
            if (content is null)
            {
                continue;
            }

            border.Child = null;
            var host = new Grid();
            host.Children.Add(content);
            var overlay = new Border { IsHitTestVisible = false, Opacity = 0.28, Margin = new Thickness(-7, -5) };
            Themed(overlay, Border.BackgroundProperty, "Accent");
            host.Children.Add(overlay);
            border.Child = host;
            _tints.Add(new CellTint { Cell = border, Content = content, Host = host });
        }

        _cellTable = table;
        _cellRange = range;
        CurrentNode = cells[0].Entry;
        if (focus)
        {
            Focus();
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty); // панель инструментов и атрибуты обновляются
        return true;
    }

    /// <summary>Снимает выделение ячеек (подсветка уходит, содержимое возвращается на место).</summary>
    public void ClearCellSelection()
    {
        foreach (var tint in _tints)
        {
            tint.Host.Children.Remove(tint.Content);
            tint.Cell.Child = tint.Content;
        }

        _tints.Clear();
        _cellTable = null;
        _cellRange = default;
    }

    // Ячейка CALS-таблицы (entry), в которой лежит узел; null — узел не в ячейке обычной таблицы.
    private static DitaNode? CalsCellOf(DitaNode node) =>
        TableCommands.CellOf(node) is { Name: "entry" } cell && cell.Closest("table") is not null ? cell : null;

    // Ячейка таблицы, над которой указатель; вне таблицы (если уже тянем по ячейкам) — ближайшая.
    private DitaNode? CellAtPoint(DitaNode table, Point point, bool nearestIfOutside)
    {
        if (CellGrid.Of(table) is not { } grid)
        {
            return null;
        }

        DitaNode? nearest = null;
        var nearestDistance = double.MaxValue;
        foreach (var cell in grid.Cells)
        {
            if (FindBlockBorder(cell.Entry) is not { } border || border.TranslatePoint(new Point(0, 0), this) is not { } topLeft)
            {
                continue;
            }

            var rect = new Rect(topLeft, border.Bounds.Size);
            if (rect.Contains(point))
            {
                return cell.Entry;
            }

            var dx = Math.Max(Math.Max(rect.Left - point.X, 0), point.X - rect.Right);
            var dy = Math.Max(Math.Max(rect.Top - point.Y, 0), point.Y - rect.Bottom);
            var distance = dx * dx + dy * dy;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = cell.Entry;
            }
        }

        return nearestIfOutside ? nearest : null;
    }

    private bool IsOverSelectedCell(Point position) =>
        _tints.Any(t => t.Cell.TranslatePoint(new Point(0, 0), this) is { } top && new Rect(top, t.Cell.Bounds.Size).Contains(position));

    // Протяжка и Shift+щелчок: true — это выделение ячеек, событие обработано.
    private bool TryDragCells(PointerEventArgs e, BlockEditor start)
    {
        if (CalsCellOf(start.Node) is not { } startCell || startCell.Closest("table") is not { } table)
        {
            return false;
        }

        var point = e.GetPosition(this);
        if (CellAtPoint(table, point, nearestIfOutside: _cellDragging) is not { } target || ReferenceEquals(target, startCell) ||
            CellGrid.Of(table)?.RangeBetween(startCell, target) is not { } range)
        {
            if (_cellDragging)
            {
                _cellDragging = false;
                ClearCellSelection(); // указатель вернулся в начальную ячейку — снова выделение текста
            }

            return false;
        }

        _cellDragging = true;
        SelectCellRange(table, range);
        return true;
    }

    private void MenuStatus(string message) => StatusRequested?.Invoke(this, message);

    private static MenuItem Item(string header, Action click, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Меню по правой кнопке на выделенных ячейках: строки и столбцы, объединение, очистка, выравнивание, границы.</summary>
    public void ShowCellMenu() => BuildCellMenu().Open(this);

    /// <summary>Меню действий над выделенными ячейками (для окна и тестов).</summary>
    public ContextMenu BuildCellMenu()
    {
        var selection = SelectedCells;
        var grid = selection is { } s ? CellGrid.Of(s.Table) : null;
        var range = grid is not null && selection is { } sel ? grid.Expand(sel.Range) : default;
        var rows = grid is null ? 0 : range.Rows;
        var columns = grid is null ? 0 : range.Columns;
        var canMerge = grid is not null && TableRanges.CanMerge(grid.Tgroup, range);
        var single = grid is not null && grid.CellsIn(range).Count == 1 && grid.CellsIn(range)[0] is { ColSpan: > 1 } or { RowSpan: > 1 };

        void Edit(TableOperation operation, string done)
        {
            if (Surface.EditCurrentTable(operation))
            {
                MenuStatus(done);
            }
            else
            {
                MenuStatus(Loc.T("Author_ThisIsNotPossibleTheTable"));
            }
        }

        var align = new MenuItem
        {
            Header = Loc.T("Author_Align"),
            ItemsSource = TextFormatting.Alignments.Select(a => (Control)Item(a.Label, () =>
            {
                Surface.SetCurrentBlockFormat(TextFormatting.AlignPrefix, a.Token);
                MenuStatus(Loc.T("Author_CellAlignment") + a.Label.ToLowerInvariant() + ".");
            })).ToList()
        };

        var items = new List<Control>
        {
            Item(Loc.T("Menu_InsertRowAbove"), () => Edit(TableOperation.InsertRowAbove, rows > 1 ? Loc.T("Author_RowsInsertedAbove0", rows) : Loc.T("Author_RowInsertedAbove"))),
            Item(Loc.T("Menu_InsertRowBelow"), () => Edit(TableOperation.InsertRowBelow, rows > 1 ? Loc.T("Author_RowsInsertedBelow0", rows) : Loc.T("Author_RowInsertedBelow"))),
            Item(Loc.T("Menu_InsertColumnLeft"), () => Edit(TableOperation.InsertColumnLeft, columns > 1 ? Loc.T("Author_ColumnsInsertedToTheLeft0", columns) : Loc.T("Author_ColumnInsertedToTheLeft"))),
            Item(Loc.T("Menu_InsertColumnRight"), () => Edit(TableOperation.InsertColumnRight, columns > 1 ? Loc.T("Author_ColumnsInsertedToTheRight0", columns) : Loc.T("Author_ColumnInsertedToTheRight"))),
            new Separator(),
            Item(rows > 1 ? Loc.T("Author_DeleteRows0", rows) : Loc.T("Menu_DeleteRow"), () => Edit(TableOperation.DeleteRow, Loc.T("Author_RowsDeleted"))),
            Item(columns > 1 ? Loc.T("Author_DeleteColumns0", columns) : Loc.T("Menu_DeleteColumn"), () => Edit(TableOperation.DeleteColumn, Loc.T("Author_ColumnsDeleted"))),
            new Separator(),
            Item(Loc.T("Author_MergeCells"), () =>
            {
                MenuStatus(Surface.MergeCurrentCellRight() ? Loc.T("Author_CellsMerged") : Loc.T("Author_TheseCellsCannotBeMergedA"));
            }, canMerge),
            Item(Loc.T("Menu_SplitCell"), () => Edit(TableOperation.SplitCell, Loc.T("Author_CellSplit")), single),
            Item(Loc.T("Author_ClearContents"), () =>
            {
                Surface.ClearSelectedCells();
                MenuStatus(Loc.T("Author_CellContentsCleared"));
            }),
            align,
            new MenuItem { Header = Loc.T("Menu_Borders"), ItemsSource = BorderMenuItems((edges, label) => ApplyBorders(edges, label)) },
            new Separator(),
            Item(Loc.T("Menu_SelectRow"), () => Surface.SelectCurrentRow()),
            Item(Loc.T("Menu_SelectColumn"), () => Surface.SelectCurrentColumn()),
            Item(Loc.T("Author_ClearSelection"), Deselect)
        };
        return new ContextMenu { ItemsSource = items };
    }

    private void ApplyBorders(BorderEdges edges, string label)
    {
        switch (Surface.SetCellBorders(edges))
        {
            case null:
                MenuStatus(Loc.T("Author_BordersTheCursorMustBeIn"));
                break;
            case false:
                MenuStatus(Loc.T("Author_0ATableEdgeIsSet", label));
                break;
            default:
                MenuStatus(Loc.T("Author_0Done", label));
                break;
        }
    }

    /// <summary>Пункты меню «Границы» как в Word: нижняя, верхняя, левая, правая; нет, все, внешние, внутренние; внутренняя горизонтальная и вертикальная.</summary>
    public static List<Control> BorderMenuItems(Action<BorderEdges, string> apply)
    {
        MenuItem Edge(string label, BorderEdges edges) => Item(label, () => apply(edges, label));
        return new List<Control>
        {
            Edge(Loc.T("Author_BottomBorder"), BorderEdges.Bottom),
            Edge(Loc.T("Author_TopBorder"), BorderEdges.Top),
            Edge(Loc.T("Author_LeftBorder"), BorderEdges.Left),
            Edge(Loc.T("Author_RightBorder"), BorderEdges.Right),
            new Separator(),
            Edge(Loc.T("Author_NoBorder"), BorderEdges.None),
            Edge(Loc.T("Menu_AllBorders"), BorderEdges.All),
            Edge(Loc.T("Author_OutsideBorders"), BorderEdges.Outer),
            Edge(Loc.T("Author_InsideBorders"), BorderEdges.Inner),
            new Separator(),
            Edge(Loc.T("Author_InsideHorizontalBorder"), BorderEdges.InnerHorizontal),
            Edge(Loc.T("Author_InsideVerticalBorder"), BorderEdges.InnerVertical)
        };
    }
}
