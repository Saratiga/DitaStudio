using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Core.Editing;

/// <summary>Прямоугольник клеток таблицы: строки <see cref="Row0"/>…<see cref="Row1"/> и столбцы <see cref="Col0"/>…<see cref="Col1"/> (с 0, включительно).</summary>
public readonly record struct CellRange(int Row0, int Row1, int Col0, int Col1)
{
    public int Rows => Row1 - Row0 + 1;

    public int Columns => Col1 - Col0 + 1;

    public bool Contains(int row, int column) => row >= Row0 && row <= Row1 && column >= Col0 && column <= Col1;
}

/// <summary>Ячейка на сетке: узел <c>entry</c>, его строка и положение с учётом объединений.</summary>
public sealed record GridCell(DitaNode Entry, DitaNode RowNode, int Row, int Col, int ColSpan, int RowSpan)
{
    public int LastRow => Row + RowSpan - 1;

    public int LastCol => Col + ColSpan - 1;
}

/// <summary>
/// Сетка одной группы <c>tgroup</c> CALS-таблицы (строки шапки и тела подряд): какая ячейка занимает каждое место с учётом объединений
/// (<c>namest</c>/<c>nameend</c>, <c>morerows</c>). По ней выделяются ячейки мышью (прямоугольник, расширенный до целых ячеек),
/// находятся ячейки диапазона и считаются границы.
/// </summary>
public sealed class CellGrid
{
    private readonly DitaNode?[,] _owner;
    private readonly Dictionary<DitaNode, GridCell> _byEntry = new(ReferenceEqualityComparer.Instance);

    private CellGrid(DitaNode tgroup, int rows, int columns, DitaNode?[,] owner, IReadOnlyList<GridCell> cells)
    {
        Tgroup = tgroup;
        Rows = rows;
        Columns = columns;
        _owner = owner;
        Cells = cells;
        foreach (var cell in cells)
        {
            _byEntry[cell.Entry] = cell;
        }
    }

    public DitaNode Tgroup { get; }

    public int Rows { get; }

    public int Columns { get; }

    public IReadOnlyList<GridCell> Cells { get; }

    /// <summary>Сетка группы, в которой лежит узел (ячейка CALS, строка, <c>tgroup</c> или таблица — первая её группа); null — не CALS.</summary>
    public static CellGrid? Of(DitaNode node)
    {
        var tgroup = node.Name == "tgroup" ? node : node.Closest("tgroup") ?? node.ElementChildren().FirstOrDefault(e => e.Name == "tgroup");
        if (tgroup is null)
        {
            return null;
        }

        var layout = CalsBorders.PlaceLayout(tgroup);
        var cells = layout.Placed.Select(p => new GridCell(p.Entry, p.RowNode, p.R, p.C, p.ColSpan, p.RowSpan)).ToList();
        return new CellGrid(tgroup, layout.Rows.Count, layout.Columns, layout.Owner, cells);
    }

    /// <summary>Ячейка, занимающая место (<paramref name="row"/>, <paramref name="column"/>); null — места нет или оно пустое.</summary>
    public GridCell? At(int row, int column) =>
        row >= 0 && row < Rows && column >= 0 && column < Columns && _owner[row, column] is { } entry ? _byEntry[entry] : null;

    /// <summary>Положение ячейки на сетке; null — ячейки нет в этой группе.</summary>
    public GridCell? CellOf(DitaNode entry) => _byEntry.TryGetValue(entry, out var cell) ? cell : null;

    /// <summary>Прямоугольник между двумя ячейками, расширенный до целых ячеек (объединённая ячейка не режется пополам). Null — одной из ячеек нет.</summary>
    public CellRange? RangeBetween(DitaNode a, DitaNode b)
    {
        if (CellOf(a) is not { } first || CellOf(b) is not { } second)
        {
            return null;
        }

        var range = new CellRange(Math.Min(first.Row, second.Row), Math.Max(first.LastRow, second.LastRow),
            Math.Min(first.Col, second.Col), Math.Max(first.LastCol, second.LastCol));
        return Expand(range);
    }

    /// <summary>Расширяет прямоугольник, пока в него не войдут целиком все задетые ячейки.</summary>
    public CellRange Expand(CellRange range)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var cell in CellsIn(range))
            {
                var expanded = new CellRange(Math.Min(range.Row0, cell.Row), Math.Max(range.Row1, cell.LastRow),
                    Math.Min(range.Col0, cell.Col), Math.Max(range.Col1, cell.LastCol));
                if (expanded != range)
                {
                    range = expanded;
                    changed = true;
                }
            }
        }
        while (changed);

        return range;
    }

    /// <summary>Ячейки, которых касается прямоугольник, по порядку (строка за строкой), каждая один раз.</summary>
    public IReadOnlyList<GridCell> CellsIn(CellRange range)
    {
        var seen = new HashSet<DitaNode>(ReferenceEqualityComparer.Instance);
        var result = new List<GridCell>();
        for (var r = Math.Max(0, range.Row0); r <= Math.Min(Rows - 1, range.Row1); r++)
        {
            for (var c = Math.Max(0, range.Col0); c <= Math.Min(Columns - 1, range.Col1); c++)
            {
                if (_owner[r, c] is { } entry && seen.Add(entry))
                {
                    result.Add(_byEntry[entry]);
                }
            }
        }

        return result;
    }

    public CellRange WholeRow(int row) => Expand(new CellRange(row, row, 0, Columns - 1));

    public CellRange WholeColumn(int column) => Expand(new CellRange(0, Rows - 1, column, column));

    public CellRange WholeTable => new(0, Rows - 1, 0, Columns - 1);
}
