using DitaStudio.Core.Editing;
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

/// <summary>Стороны выделенных ячеек, на которые действует «Границы» (как в меню Word).</summary>
[Flags]
public enum BorderEdges
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8,

    /// <summary>Линии между строками внутри выделения.</summary>
    InnerHorizontal = 16,

    /// <summary>Линии между столбцами внутри выделения.</summary>
    InnerVertical = 32,

    Outer = Top | Bottom | Left | Right,
    Inner = InnerHorizontal | InnerVertical,
    All = Outer | Inner
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

    // Классы рамки по сторонам в outputclass таблицы: «frame-top frame-left»… — когда нужной рамки нет среди значений frame
    // (только левая линия, верх и бока без низа). Пока хоть один есть, рамку задают они; frame ставится ближайшим стандартным значением.
    private static readonly string[] FrameTokens = { "frame-top", "frame-bottom", "frame-left", "frame-right" };

    private static List<string> OutputClasses(DitaNode table) =>
        (table.GetAttribute("outputclass") ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Какие стороны таблицы обведены рамкой (верх, низ, лево, право): по классам <c>frame-…</c> или по <c>frame</c>.</summary>
    public static (bool Top, bool Bottom, bool Left, bool Right) FrameFlags(DitaNode table)
    {
        var tokens = OutputClasses(table);
        if (tokens.Any(FrameTokens.Contains))
        {
            return (tokens.Contains("frame-top"), tokens.Contains("frame-bottom"), tokens.Contains("frame-left"), tokens.Contains("frame-right"));
        }

        var frame = FrameOf(table);
        return (frame is "all" or "top" or "topbot", frame is "all" or "bottom" or "topbot", frame is "all" or "sides", frame is "all" or "sides");
    }

    /// <summary>Ставит рамку по сторонам: стандартное сочетание — значением <c>frame</c>, любое другое — классами <c>frame-…</c> (и ближайшим <c>frame</c>).</summary>
    public static void SetFrameFlags(DitaNode table, bool top, bool bottom, bool left, bool right)
    {
        var tokens = OutputClasses(table).Where(t => !FrameTokens.Contains(t)).ToList();
        var standard = (top, bottom, left, right) switch
        {
            (true, true, true, true) => "all",
            (true, true, false, false) => "topbot",
            (false, false, true, true) => "sides",
            (true, false, false, false) => "top",
            (false, true, false, false) => "bottom",
            (false, false, false, false) => "none",
            _ => null
        };

        if (standard is not null)
        {
            if (standard == "all")
            {
                table.RemoveAttribute("frame");
            }
            else
            {
                table.SetAttribute("frame", standard);
            }
        }
        else
        {
            // Ближайшее стандартное значение для других программ DITA; точное — в классах.
            table.SetAttribute("frame", top && bottom ? "topbot" : left && right ? "sides" : top ? "top" : bottom ? "bottom" : "all");
            if (top)
            {
                tokens.Add("frame-top");
            }

            if (bottom)
            {
                tokens.Add("frame-bottom");
            }

            if (left)
            {
                tokens.Add("frame-left");
            }

            if (right)
            {
                tokens.Add("frame-right");
            }

        }

        if (tokens.Count == 0)
        {
            table.RemoveAttribute("outputclass");
        }
        else
        {
            table.SetAttribute("outputclass", string.Join(' ', tokens));
        }
    }

    /// <summary>Таблица оформлена не по умолчанию: рамка не «all» или где-то линия отключена.</summary>
    public static bool IsCustom(DitaNode table) =>
        FrameOf(table) != "all" || OutputClasses(table).Any(t => t.StartsWith("frame-", StringComparison.Ordinal)) ||
        table.DescendantsAndSelf().Any(n => n.Kind == NodeKind.Element && n.Name is "table" or "tgroup" or "colspec" or "row" or "entry" &&
                                            (n.GetAttribute("rowsep") == "0" || n.GetAttribute("colsep") == "0"));

    /// <summary>Расположение ячеек на сетке tgroup: строки, число столбцов, ячейки с позицией и размером, владелец каждой клетки.</summary>
    internal sealed record Layout(List<DitaNode> Rows, List<DitaNode> Colspecs, int Columns,
        List<(DitaNode Entry, DitaNode RowNode, int R, int C, int ColSpan, int RowSpan)> Placed, DitaNode?[,] Owner);

    /// <summary>Расположение ячеек группы на сетке (для <c>CellGrid</c>).</summary>
    internal static Layout PlaceLayout(DitaNode tgroup) => Place(tgroup);

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
        var (frameTop, frameBottom, frameLeft, frameRight) = FrameFlags(table);

        foreach (var tgroup in table.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            var layout = Place(tgroup);
            DitaNode? ColspecAt(int column) => column < layout.Colspecs.Count ? layout.Colspecs[column] : null;

            foreach (var (entry, row, r, c, colSpan, rowSpan) in layout.Placed)
            {
                var lastColumn = c + colSpan - 1;
                var lastRow = r + rowSpan - 1;
                var right = lastColumn >= layout.Columns - 1 ? frameRight : Sep("colsep", entry, ColspecAt(lastColumn), tgroup, table);
                var bottom = lastRow >= layout.Rows.Count - 1 ? frameBottom : Sep("rowsep", entry, row, ColspecAt(c), tgroup, table);
                result[entry] = new CellBorders(r == 0 ? frameTop : true, right, bottom, c == 0 ? frameLeft : true);
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
        SetFrameFlags(table, true, true, true, true); // прежние классы рамки по сторонам снимаются; ниже frame ставится по режиму
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

    // ------------------------------------------------------------------ границы выделенных ячеек (как «Границы» в Word)

    private readonly record struct Segment(bool Horizontal, int Boundary, int Index);

    // Участки линий, которых касается команда: горизонтальная линия номер b — между строками b-1 и b (в столбце Index), вертикальная — между
    // столбцами b-1 и b (в строке Index).
    private static IEnumerable<Segment> Segments(CellRange r, BorderEdges edges)
    {
        for (var c = r.Col0; c <= r.Col1; c++)
        {
            if (edges.HasFlag(BorderEdges.Top))
            {
                yield return new Segment(true, r.Row0, c);
            }

            if (edges.HasFlag(BorderEdges.Bottom))
            {
                yield return new Segment(true, r.Row1 + 1, c);
            }

            if (edges.HasFlag(BorderEdges.InnerHorizontal))
            {
                for (var b = r.Row0 + 1; b <= r.Row1; b++)
                {
                    yield return new Segment(true, b, c);
                }
            }
        }

        for (var row = r.Row0; row <= r.Row1; row++)
        {
            if (edges.HasFlag(BorderEdges.Left))
            {
                yield return new Segment(false, r.Col0, row);
            }

            if (edges.HasFlag(BorderEdges.Right))
            {
                yield return new Segment(false, r.Col1 + 1, row);
            }

            if (edges.HasFlag(BorderEdges.InnerVertical))
            {
                for (var b = r.Col0 + 1; b <= r.Col1; b++)
                {
                    yield return new Segment(false, b, row);
                }
            }
        }
    }

    // Есть ли линия на участке; null — линии нет места (участок внутри объединённой ячейки или пустое место сетки).
    private static bool? Visible(CellGrid grid, Dictionary<DitaNode, CellBorders> borders, (bool Top, bool Bottom, bool Left, bool Right) frame, Segment s)
    {
        if (s.Horizontal)
        {
            if (s.Boundary == 0)
            {
                return frame.Top;
            }

            if (s.Boundary == grid.Rows)
            {
                return frame.Bottom;
            }

            return grid.At(s.Boundary - 1, s.Index) is { } upper && grid.At(s.Boundary, s.Index) is { } lower && !ReferenceEquals(upper.Entry, lower.Entry) &&
                   borders.TryGetValue(upper.Entry, out var above)
                ? above.Bottom
                : null;
        }

        if (s.Boundary == 0)
        {
            return frame.Left;
        }

        if (s.Boundary == grid.Columns)
        {
            return frame.Right;
        }

        return grid.At(s.Index, s.Boundary - 1) is { } left && grid.At(s.Index, s.Boundary) is { } right && !ReferenceEquals(left.Entry, right.Entry) &&
               borders.TryGetValue(left.Entry, out var beside)
            ? beside.Right
            : null;
    }

    /// <summary>Есть ли сейчас линия на всех участках выбранных сторон диапазона (для «переключателя»: все есть — команда снимает, иначе ставит).</summary>
    public static bool AreVisible(DitaNode table, CellRange range, BorderEdges edges)
    {
        if (CellGrid.Of(table) is not { } grid)
        {
            return false;
        }

        var borders = Compute(table);
        var frame = FrameFlags(table);
        var any = false;
        foreach (var segment in Segments(range, edges))
        {
            if (Visible(grid, borders, frame, segment) is not { } visible)
            {
                continue;
            }

            any = true;
            if (!visible)
            {
                return false;
            }
        }

        return any;
    }

    /// <summary>
    /// Показывает или убирает линии выбранных сторон диапазона. Линия между ячейками — <c>rowsep</c>/<c>colsep</c> ячейки над ней
    /// или слева от неё; линия по краю таблицы — рамка таблицы целиком (<see cref="SetFrameFlags"/>), поэтому край ставится или
    /// снимается, только если выделена вся его длина (весь верх, весь левый край…). Возвращает false, если из-за этого часть краёв
    /// пропущена: у CALS нет границы у части внешней стороны.
    /// </summary>
    public static bool SetEdges(DitaNode table, CellRange range, BorderEdges edges, bool visible)
    {
        if (CellGrid.Of(table) is not { } grid)
        {
            return false;
        }

        var frame = FrameFlags(table);
        var complete = true;
        var value = visible ? "1" : "0";
        var framed = new HashSet<(bool Horizontal, int Boundary)>();
        foreach (var segment in Segments(range, edges))
        {
            var onFrame = segment.Horizontal ? segment.Boundary == 0 || segment.Boundary == grid.Rows : segment.Boundary == 0 || segment.Boundary == grid.Columns;
            if (onFrame)
            {
                framed.Add((segment.Horizontal, segment.Boundary));
                continue;
            }

            if (segment.Horizontal)
            {
                if (grid.At(segment.Boundary - 1, segment.Index) is { } upper && grid.At(segment.Boundary, segment.Index) is { } lower && !ReferenceEquals(upper.Entry, lower.Entry))
                {
                    upper.Entry.SetAttribute("rowsep", value);
                }
            }
            else if (grid.At(segment.Index, segment.Boundary - 1) is { } left && grid.At(segment.Index, segment.Boundary) is { } right && !ReferenceEquals(left.Entry, right.Entry))
            {
                left.Entry.SetAttribute("colsep", value);
            }
        }

        // Край таблицы: рамка по стороне меняется, только когда выделена вся длина стороны.
        foreach (var (horizontal, boundary) in framed)
        {
            var whole = horizontal
                ? range.Col0 == 0 && range.Col1 == grid.Columns - 1
                : range.Row0 == 0 && range.Row1 == grid.Rows - 1;
            if (!whole)
            {
                complete = false;
                continue;
            }

            frame = (horizontal, boundary == 0) switch
            {
                (true, true) => frame with { Top = visible },
                (true, false) => frame with { Bottom = visible },
                (false, true) => frame with { Left = visible },
                _ => frame with { Right = visible }
            };
        }

        SetFrameFlags(table, frame.Top, frame.Bottom, frame.Left, frame.Right);
        NormalizeSeparators(table);
        return complete;
    }

    /// <summary>Убирает у ячеек лишние <c>rowsep="1"</c>/<c>colsep="1"</c>, которые ничего не меняют (линия и так есть по умолчанию выше по цепочке).</summary>
    private static void NormalizeSeparators(DitaNode table)
    {
        foreach (var tgroup in table.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            var layout = Place(tgroup);
            DitaNode? ColspecAt(int column) => column < layout.Colspecs.Count ? layout.Colspecs[column] : null;
            foreach (var (entry, row, _, c, colSpan, _) in layout.Placed)
            {
                if (entry.GetAttribute("rowsep") == "1" && Sep("rowsep", row, ColspecAt(c), tgroup, table))
                {
                    entry.RemoveAttribute("rowsep");
                }

                if (entry.GetAttribute("colsep") == "1" && Sep("colsep", ColspecAt(c + colSpan - 1), tgroup, table))
                {
                    entry.RemoveAttribute("colsep");
                }
            }
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
