using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Authoring;

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
                MenuStatus("Так сделать нельзя: в таблице должна остаться хотя бы одна строка и один столбец.");
            }
        }

        var align = new MenuItem
        {
            Header = "Выровнять",
            ItemsSource = TextFormatting.Alignments.Select(a => (Control)Item(a.Label, () =>
            {
                Surface.SetCurrentBlockFormat(TextFormatting.AlignPrefix, a.Token);
                MenuStatus("Выравнивание ячеек: " + a.Label.ToLowerInvariant() + ".");
            })).ToList()
        };

        var items = new List<Control>
        {
            Item("Вставить строку выше", () => Edit(TableOperation.InsertRowAbove, rows > 1 ? $"Строк вставлено выше: {rows}." : "Строка вставлена выше.")),
            Item("Вставить строку ниже", () => Edit(TableOperation.InsertRowBelow, rows > 1 ? $"Строк вставлено ниже: {rows}." : "Строка вставлена ниже.")),
            Item("Вставить столбец слева", () => Edit(TableOperation.InsertColumnLeft, columns > 1 ? $"Столбцов вставлено слева: {columns}." : "Столбец вставлен слева.")),
            Item("Вставить столбец справа", () => Edit(TableOperation.InsertColumnRight, columns > 1 ? $"Столбцов вставлено справа: {columns}." : "Столбец вставлен справа.")),
            new Separator(),
            Item(rows > 1 ? $"Удалить строки: {rows}" : "Удалить строку", () => Edit(TableOperation.DeleteRow, "Строки удалены.")),
            Item(columns > 1 ? $"Удалить столбцы: {columns}" : "Удалить столбец", () => Edit(TableOperation.DeleteColumn, "Столбцы удалены.")),
            new Separator(),
            Item("Объединить ячейки", () =>
            {
                MenuStatus(Surface.MergeCurrentCellRight() ? "Ячейки объединены." : "Эти ячейки объединить нельзя: строка таблицы должна сохранить хотя бы одну ячейку, шапка и тело не объединяются.");
            }, canMerge),
            Item("Разделить ячейку", () => Edit(TableOperation.SplitCell, "Ячейка разделена."), single),
            Item("Очистить содержимое", () =>
            {
                Surface.ClearSelectedCells();
                MenuStatus("Содержимое ячеек удалено.");
            }),
            align,
            new MenuItem { Header = "Границы", ItemsSource = BorderMenuItems((edges, label) => ApplyBorders(edges, label)) },
            new Separator(),
            Item("Выделить строку", () => Surface.SelectCurrentRow()),
            Item("Выделить столбец", () => Surface.SelectCurrentColumn()),
            Item("Снять выделение", Deselect)
        };
        return new ContextMenu { ItemsSource = items };
    }

    private void ApplyBorders(BorderEdges edges, string label)
    {
        switch (Surface.SetCellBorders(edges))
        {
            case null:
                MenuStatus("Границы: курсор должен быть в ячейке обычной таблицы.");
                break;
            case false:
                MenuStatus($"{label}: край таблицы задаётся для всей его длины сразу — выделите всю строку или весь столбец; остальные линии применены.");
                break;
            default:
                MenuStatus($"{label} — выполнено.");
                break;
        }
    }

    /// <summary>Пункты меню «Границы» как в Word: нижняя, верхняя, левая, правая; нет, все, внешние, внутренние; внутренняя горизонтальная и вертикальная.</summary>
    public static List<Control> BorderMenuItems(Action<BorderEdges, string> apply)
    {
        MenuItem Edge(string label, BorderEdges edges) => Item(label, () => apply(edges, label));
        return new List<Control>
        {
            Edge("Нижняя граница", BorderEdges.Bottom),
            Edge("Верхняя граница", BorderEdges.Top),
            Edge("Левая граница", BorderEdges.Left),
            Edge("Правая граница", BorderEdges.Right),
            new Separator(),
            Edge("Нет границы", BorderEdges.None),
            Edge("Все границы", BorderEdges.All),
            Edge("Внешние границы", BorderEdges.Outer),
            Edge("Внутренние границы", BorderEdges.Inner),
            new Separator(),
            Edge("Внутренняя горизонтальная граница", BorderEdges.InnerHorizontal),
            Edge("Внутренняя вертикальная граница", BorderEdges.InnerVertical)
        };
    }
}
