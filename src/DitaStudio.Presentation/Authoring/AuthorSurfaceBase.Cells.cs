using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Presentation.Authoring;

// Действия над выделенными ячейками CALS-таблицы (прямоугольник, выбранный мышью): строки и столбцы по числу выделенного, объединение,
// очистка, выравнивание, границы как в Word. Само выделение (какие ячейки и как подсвечены) хранит оболочка.
public abstract partial class AuthorSurfaceBase
{
    /// <summary>Выделенные ячейки: таблица и прямоугольник (строки и столбцы с 0). Null — ячейки не выделены.</summary>
    public virtual (DitaNode Table, CellRange Range)? CellSelection => null;

    public bool HasCellSelection => CellSelection is not null;

    /// <summary>Выделяет ячейки прямоугольника (null — снимает выделение). Оболочка подсвечивает их; без неё — ничего не делает.</summary>
    protected virtual void ReselectCells(DitaNode table, CellRange? range)
    {
    }

    /// <summary>Ячейки, на которые действует команда: выделенный прямоугольник или (если выделения нет) ячейка под курсором как диапазон из одной ячейки.</summary>
    private (DitaNode Table, DitaNode Tgroup, CellGrid Grid, CellRange Range)? CellTarget()
    {
        if (CellSelection is { } selection && CellGrid.Of(selection.Table) is { } selectedGrid)
        {
            return (selection.Table, selectedGrid.Tgroup, selectedGrid, selectedGrid.Expand(selection.Range));
        }

        if (CurrentNode is { } node && TableCommands.CellOf(node) is { Name: "entry" } cell && cell.Closest("table") is { } table &&
            CellGrid.Of(cell) is { } grid && grid.CellOf(cell) is { } gridCell)
        {
            return (table, grid.Tgroup, grid, new CellRange(gridCell.Row, gridCell.LastRow, gridCell.Col, gridCell.LastCol));
        }

        return null;
    }

    /// <summary>Строки и столбцы по выделенным ячейкам (столько, сколько выделено). Null — выделения нет (работает обычная команда по ячейке под курсором).</summary>
    private bool? EditSelectedCells(TableOperation operation)
    {
        if (Document is null || CellSelection is not { } selection || CellGrid.Of(selection.Table) is not { } grid)
        {
            return null;
        }

        var range = grid.Expand(selection.Range);
        if (operation == TableOperation.SplitCell)
        {
            return null; // разделяется одна ячейка — обычной командой по ячейке под курсором
        }

        FlushPendingEdits();
        BeforeStructuralEdit(TableCommands.Describe(operation) + (range.Rows > 1 && operation is TableOperation.InsertRowAbove or TableOperation.InsertRowBelow or TableOperation.DeleteRow ||
                                                                  range.Columns > 1 && operation is TableOperation.InsertColumnLeft or TableOperation.InsertColumnRight or TableOperation.DeleteColumn
            ? " (выделенные)"
            : string.Empty));
        var count = operation switch
        {
            TableOperation.InsertRowAbove => TableRanges.InsertRows(grid.Tgroup, range, above: true),
            TableOperation.InsertRowBelow => TableRanges.InsertRows(grid.Tgroup, range, above: false),
            TableOperation.DeleteRow => TableRanges.DeleteRows(grid.Tgroup, range),
            TableOperation.InsertColumnLeft => TableRanges.InsertColumns(grid.Tgroup, range, left: true),
            TableOperation.InsertColumnRight => TableRanges.InsertColumns(grid.Tgroup, range, left: false),
            _ => TableRanges.DeleteColumns(grid.Tgroup, range)
        };
        if (count == 0)
        {
            return false;
        }

        var holder = selection.Table.Parent;
        var focus = TableGrid(selection.Table)?.Cells.FirstOrDefault()?.Entry ?? selection.Table;
        CurrentNode = focus;
        Changed(FirstEditable(focus), holder, selection.Table);
        ReselectCells(selection.Table, null); // строки и столбцы сдвинулись — выделение снимается, курсор в таблице
        return true;
    }

    private static CellGrid? TableGrid(DitaNode table) => CellGrid.Of(table);

    /// <summary>Объединяет выделенные ячейки в одну. Null — выделено меньше двух ячеек (работает обычное «объединить с соседней»); false — нельзя.</summary>
    public bool? MergeSelectedCells()
    {
        if (Document is null || CellSelection is not { } selection || CellGrid.Of(selection.Table) is not { } grid)
        {
            return null;
        }

        var range = grid.Expand(selection.Range);
        if (grid.CellsIn(range).Count < 2)
        {
            return null;
        }

        FlushPendingEdits();
        BeforeStructuralEdit("Объединение выделенных ячеек");
        if (TableRanges.Merge(grid.Tgroup, range) is not { } merged)
        {
            return false;
        }

        CurrentNode = merged;
        Changed(FirstEditable(merged), selection.Table.Parent, selection.Table);
        ReselectCells(selection.Table, CellGrid.Of(selection.Table)?.CellOf(merged) is { } cell ? new CellRange(cell.Row, cell.LastRow, cell.Col, cell.LastCol) : null);
        return true;
    }

    /// <summary>Очищает выделенные ячейки (Delete): содержимое уходит, ячейки остаются. false — ячейки не выделены.</summary>
    public bool ClearSelectedCells()
    {
        if (Document is null || CellSelection is not { } selection || CellGrid.Of(selection.Table) is not { } grid)
        {
            return false;
        }

        var cells = grid.CellsIn(grid.Expand(selection.Range)).Select(c => c.Entry).ToList();
        FlushPendingEdits();
        BeforeStructuralEdit("Очистка ячеек");
        TableRanges.Clear(cells);
        Changed(FirstEditable(cells[0]), selection.Table.Parent, selection.Table);
        ReselectCells(selection.Table, selection.Range);
        return true;
    }

    /// <summary>
    /// Границы выделенных ячеек (или ячейки под курсором) как в меню «Границы» Word: выбранные стороны показываются, а если они уже все есть —
    /// убираются. Возвращает: null — курсор не в CALS-таблице; true — применено; false — применено не полностью (край таблицы
    /// ставится только для всей его длины).
    /// </summary>
    public bool? SetCellBorders(BorderEdges edges)
    {
        if (Document is null || CellTarget() is not { } target)
        {
            return null;
        }

        var (table, _, _, range) = target;
        var show = edges == BorderEdges.None || !CalsBorders.AreVisible(table, range, edges);
        FlushPendingEdits();
        BeforeStructuralEdit(edges == BorderEdges.All || edges == BorderEdges.None ? "Границы ячеек" : "Границы выделенных ячеек");
        var complete = edges == BorderEdges.None
            ? CalsBorders.SetEdges(table, range, BorderEdges.All, visible: false)
            : CalsBorders.SetEdges(table, range, edges, show);
        CurrentNode = CurrentNode ?? FirstEditable(table);
        Changed(FirstEditable(table), table.Parent, table);
        ReselectCells(table, CellSelection is not null ? range : null);
        return complete;
    }

    /// <summary>Выделяет строку таблицы, в которой курсор.</summary>
    public bool SelectCurrentRow() => SelectWhole(row: true);

    /// <summary>Выделяет столбец таблицы, в котором курсор.</summary>
    public bool SelectCurrentColumn() => SelectWhole(row: false);

    private bool SelectWhole(bool row)
    {
        if (CurrentNode is not { } node || TableCommands.CellOf(node) is not { Name: "entry" } cell || cell.Closest("table") is not { } table ||
            CellGrid.Of(cell) is not { } grid || grid.CellOf(cell) is not { } gridCell)
        {
            return false;
        }

        ReselectCells(table, row ? grid.WholeRow(gridCell.Row) : grid.WholeColumn(gridCell.Col));
        return true;
    }
}
