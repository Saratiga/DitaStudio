using DitaStudio.Core.Model;

namespace DitaStudio.Core.Editing;

/// <summary>
/// Действия над выделенным прямоугольником ячеек CALS-таблицы (<see cref="CellRange"/>): вставка и удаление строк и столбцов (столько,
/// сколько выделено), объединение в одну ячейку, очистка, выравнивание. Каждое действие возвращает, получилось ли оно; если нет,
/// модель не меняется.
/// </summary>
public static class TableRanges
{
    /// <summary>Удаляет строки диапазона (снизу вверх); возвращает, сколько удалено. Последнюю строку секции не удалить.</summary>
    public static int DeleteRows(DitaNode tgroup, CellRange range)
    {
        var deleted = 0;
        for (var row = range.Row1; row >= range.Row0; row--)
        {
            if (CellGrid.Of(tgroup) is not { } grid || grid.Cells.FirstOrDefault(c => c.Row == row) is not { } cell)
            {
                continue;
            }

            if (TableCommands.Apply(cell.Entry, TableOperation.DeleteRow) is not null)
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>Удаляет столбцы диапазона (справа налево); возвращает, сколько удалено.</summary>
    public static int DeleteColumns(DitaNode tgroup, CellRange range)
    {
        var deleted = 0;
        for (var column = range.Col1; column >= range.Col0; column--)
        {
            if (CellGrid.Of(tgroup) is not { } grid || grid.Cells.FirstOrDefault(c => c.Col == column) is not { } cell)
            {
                continue;
            }

            if (TableCommands.Apply(cell.Entry, TableOperation.DeleteColumn) is not null)
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>Вставляет столько новых строк выше (или ниже) диапазона, сколько в нём строк; возвращает, сколько вставлено.</summary>
    public static int InsertRows(DitaNode tgroup, CellRange range, bool above)
    {
        if (CellGrid.Of(tgroup) is not { } grid)
        {
            return 0;
        }

        // Ячейка, от которой считается место: начинающаяся в первой строке (выше) или заканчивающаяся в последней (ниже).
        var anchor = above
            ? grid.Cells.Where(c => c.Row == range.Row0).OrderBy(c => c.Col).FirstOrDefault()
            : grid.Cells.Where(c => c.LastRow == range.Row1).OrderBy(c => c.Col).FirstOrDefault();
        if (anchor is null)
        {
            return 0;
        }

        var inserted = 0;
        for (var i = 0; i < range.Rows; i++)
        {
            if (TableCommands.Apply(anchor.Entry, above ? TableOperation.InsertRowAbove : TableOperation.InsertRowBelow) is not null)
            {
                inserted++;
            }
        }

        return inserted;
    }

    /// <summary>Вставляет столько новых столбцов слева (или справа) от диапазона, сколько в нём столбцов; возвращает, сколько вставлено.</summary>
    public static int InsertColumns(DitaNode tgroup, CellRange range, bool left)
    {
        if (CellGrid.Of(tgroup) is not { } grid)
        {
            return 0;
        }

        var anchor = left
            ? grid.Cells.Where(c => c.Col == range.Col0).OrderBy(c => c.Row).FirstOrDefault()
            : grid.Cells.Where(c => c.LastCol == range.Col1).OrderBy(c => c.Row).FirstOrDefault();
        if (anchor is null)
        {
            return 0;
        }

        var inserted = 0;
        for (var i = 0; i < range.Columns; i++)
        {
            if (TableCommands.Apply(anchor.Entry, left ? TableOperation.InsertColumnLeft : TableOperation.InsertColumnRight) is not null)
            {
                inserted++;
            }
        }

        return inserted;
    }

    /// <summary>Можно ли объединить диапазон в одну ячейку: больше одной ячейки, всё в одной секции (шапка или тело), ни в одной строке не остаётся без ячеек.</summary>
    public static bool CanMerge(DitaNode tgroup, CellRange range) => MergeCheck(tgroup, range) is not null;

    /// <summary>
    /// Объединяет ячейки диапазона в левую верхнюю (<c>namest</c>/<c>nameend</c>, <c>morerows</c>): содержимое остальных переносится в неё.
    /// Возвращает объединённую ячейку; null — нельзя (см. <see cref="CanMerge"/>), модель не меняется.
    /// </summary>
    public static DitaNode? Merge(DitaNode tgroup, CellRange range)
    {
        if (MergeCheck(tgroup, range) is not { } check)
        {
            return null;
        }

        var (grid, main, others) = check;
        var names = EditCommands.EnsureColumnNames(tgroup);
        var entry = main.Entry;
        if (range.Columns > 1)
        {
            entry.SetAttribute("namest", names[range.Col0]);
            entry.SetAttribute("nameend", names[range.Col1]);
            entry.RemoveAttribute("colname");
        }

        if (range.Rows > 1)
        {
            entry.SetAttribute("morerows", (range.Rows - 1).ToString());
        }

        foreach (var other in others)
        {
            EditCommands.MergeEntryContent(entry, other.Entry);
            other.Entry.RemoveSelf();
        }

        _ = grid;
        return entry;
    }

    private static (CellGrid Grid, GridCell Main, List<GridCell> Others)? MergeCheck(DitaNode tgroup, CellRange range)
    {
        if (CellGrid.Of(tgroup) is not { } grid)
        {
            return null;
        }

        range = grid.Expand(range);
        var cells = grid.CellsIn(range);
        if (cells.Count < 2 || grid.At(range.Row0, range.Col0) is not { } main || main.Row != range.Row0 || main.Col != range.Col0)
        {
            return null;
        }

        // Все строки диапазона — в одной секции: объединение (morerows) не переходит из шапки в тело.
        var firstRow = main.RowNode;
        var lastRowNode = grid.At(range.Row1, range.Col0)?.RowNode;
        if (lastRowNode is null || !ReferenceEquals(firstRow.Parent, lastRowNode.Parent))
        {
            return null;
        }

        var others = cells.Where(c => !ReferenceEquals(c.Entry, main.Entry)).ToList();
        foreach (var row in others.Select(c => c.RowNode).Distinct())
        {
            // Строка CALS обязана содержать хотя бы одну ячейку.
            if (row.ElementChildren().Count(e => e.Name == "entry") <= others.Count(c => ReferenceEquals(c.RowNode, row)) && !ReferenceEquals(row, firstRow))
            {
                return null;
            }
        }

        return (grid, main, others);
    }

    /// <summary>Очищает содержимое ячеек (текст и блоки уходят, сами ячейки остаются).</summary>
    public static void Clear(IEnumerable<DitaNode> entries)
    {
        foreach (var entry in entries)
        {
            foreach (var child in entry.Children.ToList())
            {
                child.RemoveSelf();
            }
        }
    }

    /// <summary>Выравнивание текста ячеек (<c>align</c>: left, center, right, justify); null — снять.</summary>
    public static void SetAlign(IEnumerable<DitaNode> entries, string? align)
    {
        foreach (var entry in entries)
        {
            if (align is null or "left")
            {
                entry.RemoveAttribute("align");
            }
            else
            {
                entry.SetAttribute("align", align);
            }
        }
    }
}
