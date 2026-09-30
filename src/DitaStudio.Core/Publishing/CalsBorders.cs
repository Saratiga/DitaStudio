using DitaStudio.Core.Model;

namespace DitaStudio.Core.Publishing;

/// <summary>Границы одной ячейки таблицы: есть ли линия сверху, справа, снизу и слева.</summary>
public readonly record struct CellBorders(bool Top, bool Right, bool Bottom, bool Left)
{
    public static readonly CellBorders All = new(true, true, true, true);
}

/// <summary>Способ провести границы таблицы одной командой.</summary>
public enum TableBorderMode
{
    /// <summary>Рамка и все внутренние линии.</summary>
    All,

    /// <summary>Только внешняя рамка, без внутренних линий.</summary>
    OuterOnly,

    /// <summary>Только горизонтальные линии (между строками) и верх/низ.</summary>
    HorizontalOnly,

    /// <summary>Без границ вообще.</summary>
    None
}

/// <summary>
/// Границы таблицы CALS — стандартными атрибутами DITA: <c>frame</c> таблицы (<c>all</c>, <c>top</c>, <c>bottom</c>,
/// <c>topbot</c>, <c>sides</c>, <c>none</c>) и <c>rowsep</c>/<c>colsep</c> (<c>0</c>/<c>1</c>) у таблицы, <c>tgroup</c>, строки,
/// <c>colspec</c> и ячейки. Ближайший к ячейке атрибут главнее; по умолчанию линии есть. Для каждой ячейки выводится, с каких
/// сторон у неё линия, — с учётом объединённых ячеек; этим пользуются DOCX, HTML/PDF и «Автор».
/// </summary>
public static class CalsBorders
{
    /// <summary>Значение <c>frame</c> таблицы; по умолчанию <c>all</c>.</summary>
    public static string FrameOf(DitaNode table) => table.GetAttribute("frame") switch
    {
        "top" or "bottom" or "topbot" or "sides" or "none" => table.GetAttribute("frame")!,
        _ => "all"
    };

    /// <summary>Таблица оформлена не по умолчанию: рамка не «all» или где-то линия отключена.</summary>
    public static bool IsCustom(DitaNode table) =>
        FrameOf(table) != "all" ||
        table.DescendantsAndSelf().Any(n => n.Kind == NodeKind.Element && n.Name is "table" or "tgroup" or "colspec" or "row" or "entry" &&
                                            (n.GetAttribute("rowsep") == "0" || n.GetAttribute("colsep") == "0"));

    /// <summary>Расположение ячеек на сетке tgroup: строки, число столбцов, ячейки с позицией и размером, владелец каждой клетки.</summary>
    private sealed record Layout(List<DitaNode> Rows, List<DitaNode> Colspecs, int Columns,
        List<(DitaNode Entry, DitaNode RowNode, int R, int C, int ColSpan, int RowSpan)> Placed, DitaNode?[,] Owner);

    private static Layout Place(DitaNode tgroup)
    {
        var rows = new List<DitaNode>();
        foreach (var part in tgroup.ElementChildren().Where(e => e.Name is "thead" or "tbody"))
        {
            rows.AddRange(part.ElementChildren().Where(r => r.Name == "row"));
        }

        var colspecs = tgroup.ElementChildren().Where(e => e.Name == "colspec").ToList();
        var colNames = colspecs.Count > 0
            ? colspecs.Select((spec, i) => spec.GetAttribute("colname") is { Length: > 0 } name ? name : $"c{i + 1}").ToList()
            : Enumerable.Range(1, ColumnCount(tgroup, rows)).Select(i => $"c{i}").ToList();
        var columns = Math.Max(colNames.Count, 1);

        var owner = new DitaNode?[rows.Count, columns];
        var placed = new List<(DitaNode, DitaNode, int, int, int, int)>();
        for (var r = 0; r < rows.Count; r++)
        {
            var cursor = 0;
            foreach (var entry in rows[r].ElementChildren().Where(e => e.Name == "entry"))
            {
                while (cursor < columns && owner[r, cursor] is not null)
                {
                    cursor++;
                }

                if (cursor >= columns)
                {
                    break;
                }

                var colSpan = 1;
                var start = colNames.IndexOf(entry.GetAttribute("namest") ?? string.Empty);
                var end = colNames.IndexOf(entry.GetAttribute("nameend") ?? string.Empty);
                if (start >= 0 && end >= start)
                {
                    colSpan = end - start + 1;
                }

                colSpan = Math.Min(colSpan, columns - cursor);
                var rowSpan = int.TryParse(entry.GetAttribute("morerows"), out var more) && more > 0 ? Math.Min(more + 1, rows.Count - r) : 1;
                for (var rr = r; rr < r + rowSpan; rr++)
                {
                    for (var cc = cursor; cc < cursor + colSpan; cc++)
                    {
                        owner[rr, cc] = entry;
                    }
                }

                placed.Add((entry, rows[r], r, cursor, colSpan, rowSpan));
                cursor += colSpan;
            }
        }

        return new Layout(rows, colspecs, columns, placed, owner);
    }

    /// <summary>Границы всех ячеек таблицы (по <c>entry</c>). Для таблицы по умолчанию — у всех все четыре линии.</summary>
    public static Dictionary<DitaNode, CellBorders> Compute(DitaNode table)
    {
        var result = new Dictionary<DitaNode, CellBorders>(ReferenceEqualityComparer.Instance);
        var frame = FrameOf(table);
        var frameTop = frame is "all" or "top" or "topbot";
        var frameBottom = frame is "all" or "bottom" or "topbot";
        var frameSides = frame is "all" or "sides";

        foreach (var tgroup in table.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            var layout = Place(tgroup);
            DitaNode? ColspecAt(int column) => column < layout.Colspecs.Count ? layout.Colspecs[column] : null;

            foreach (var (entry, row, r, c, colSpan, rowSpan) in layout.Placed)
            {
                var lastColumn = c + colSpan - 1;
                var lastRow = r + rowSpan - 1;
                var right = lastColumn >= layout.Columns - 1 ? frameSides : Sep("colsep", entry, ColspecAt(lastColumn), tgroup, table);
                var bottom = lastRow >= layout.Rows.Count - 1 ? frameBottom : Sep("rowsep", entry, row, ColspecAt(c), tgroup, table);
                result[entry] = new CellBorders(r == 0 ? frameTop : true, right, bottom, c == 0 ? frameSides : true);
            }

            // Левая и верхняя линия ячейки — правая и нижняя линия соседа: выравниваем по итоговым значениям.
            foreach (var (entry, _, r, c, _, _) in layout.Placed)
            {
                var current = result[entry];
                if (c > 0 && layout.Owner[r, c - 1] is { } left && result.TryGetValue(left, out var leftFinal))
                {
                    current = current with { Left = leftFinal.Right };
                }

                if (r > 0 && layout.Owner[r - 1, c] is { } upper && result.TryGetValue(upper, out var upperFinal))
                {
                    current = current with { Top = upperFinal.Bottom };
                }

                result[entry] = current;
            }
        }

        return result;
    }

    // Ближайший заданный атрибут (0/1) из цепочки; по умолчанию линия есть.
    private static bool Sep(string attribute, params DitaNode?[] chain)
    {
        foreach (var node in chain)
        {
            if (node?.GetAttribute(attribute) is { } value)
            {
                return value != "0";
            }
        }

        return true;
    }

    private static int ColumnCount(DitaNode tgroup, List<DitaNode> rows) =>
        int.TryParse(tgroup.GetAttribute("cols"), out var cols) && cols > 0
            ? cols
            : rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.ElementChildren().Count(c => c.Name == "entry")));

    // ------------------------------------------------------------------ правка

    /// <summary>Границы всей таблицы одним выбором: ставит <c>frame</c>, <c>rowsep</c>, <c>colsep</c> у таблицы и снимает
    /// прежние исключения у групп, строк, столбцов и ячеек.</summary>
    public static void SetMode(DitaNode table, TableBorderMode mode)
    {
        foreach (var node in table.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element && n.Name is "tgroup" or "colspec" or "row" or "entry"))
        {
            node.RemoveAttribute("rowsep");
            node.RemoveAttribute("colsep");
        }

        switch (mode)
        {
            case TableBorderMode.All:
                table.RemoveAttribute("frame");
                table.RemoveAttribute("rowsep");
                table.RemoveAttribute("colsep");
                break;
            case TableBorderMode.OuterOnly:
                table.SetAttribute("frame", "all");
                table.SetAttribute("rowsep", "0");
                table.SetAttribute("colsep", "0");
                break;
            case TableBorderMode.HorizontalOnly:
                table.SetAttribute("frame", "topbot");
                table.SetAttribute("rowsep", "1");
                table.SetAttribute("colsep", "0");
                break;
            default:
                table.SetAttribute("frame", "none");
                table.SetAttribute("rowsep", "0");
                table.SetAttribute("colsep", "0");
                break;
        }
    }

    /// <summary>Линия под строкой: <c>rowsep</c> строки (и снятие исключений у её ячеек).</summary>
    public static void SetRowSeparator(DitaNode row, bool visible)
    {
        foreach (var entry in row.ElementChildren().Where(e => e.Name == "entry"))
        {
            entry.RemoveAttribute("rowsep");
        }

        row.SetAttribute("rowsep", visible ? "1" : "0");
    }

    /// <summary>Номер (с 0) последнего столбца, который занимает ячейка; -1 — ячейка не в таблице.</summary>
    public static int LastColumnOf(DitaNode entry)
    {
        var tgroup = entry.Closest("tgroup");
        if (tgroup is null)
        {
            return -1;
        }

        foreach (var (e, _, _, c, colSpan, _) in Place(tgroup).Placed)
        {
            if (ReferenceEquals(e, entry))
            {
                return c + colSpan - 1;
            }
        }

        return -1;
    }

    /// <summary>Линия справа от столбца с номером <paramref name="column"/> (с 0): <c>colsep</c> ячеек, заканчивающихся в нём
    /// (исключения у других ячеек не трогаются). false — в таблице нет такого столбца.</summary>
    public static bool SetColumnSeparator(DitaNode tgroup, int column, bool visible)
    {
        var changed = false;
        foreach (var (entry, _, _, c, colSpan, _) in Place(tgroup).Placed)
        {
            if (c + colSpan - 1 == column)
            {
                entry.SetAttribute("colsep", visible ? "1" : "0");
                changed = true;
            }
        }

        return changed;
    }
}
