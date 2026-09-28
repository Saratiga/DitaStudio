using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Editing;

/// <summary>Операции над строками и столбцами таблицы.</summary>
public enum TableOperation
{
    InsertRowAbove,
    InsertRowBelow,
    DeleteRow,
    InsertColumnLeft,
    InsertColumnRight,
    DeleteColumn,
    SplitCell
}

/// <summary>
/// Строки и столбцы таблиц. CALS (<c>table/tgroup</c>) — по сетке с учётом объединений
/// (<c>namest</c>/<c>nameend</c>, <c>morerows</c>) и привязки к столбцу (<c>colname</c>);
/// <c>simpletable</c> — строки и столбцы, её специализации (<c>properties</c>, <c>choicetable</c>) —
/// только строки: число их столбцов задано моделью. Операция возвращает ячейку для курсора или
/// null, если здесь невозможна (модель тогда не меняется).
/// </summary>
public static class TableCommands
{
    public static string Describe(TableOperation operation) => operation switch
    {
        TableOperation.InsertRowAbove => "Вставка строки выше",
        TableOperation.InsertRowBelow => "Вставка строки ниже",
        TableOperation.DeleteRow => "Удаление строки",
        TableOperation.InsertColumnLeft => "Вставка столбца слева",
        TableOperation.InsertColumnRight => "Вставка столбца справа",
        TableOperation.DeleteColumn => "Удаление столбца",
        _ => "Разделение ячейки"
    };

    /// <summary>Ячейка таблицы, внутри которой узел (entry, stentry, proptype…), или null.</summary>
    public static DitaNode? CellOf(DitaNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current.Kind == NodeKind.Element &&
                (current.Name == "entry" && current.Parent?.Name == "row" ||
                 current.Parent is { } row && IsSimpleRow(row)))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>Сама таблица ячейки (table, simpletable, properties, choicetable).</summary>
    public static DitaNode? TableOf(DitaNode cell) =>
        cell.Name == "entry"
            ? Ancestor(cell, "table") ?? Ancestor(cell, "tgroup")
            : cell.Parent?.Parent;

    public static DitaNode? Apply(DitaNode node, TableOperation operation)
    {
        if (CellOf(node) is not { } cell)
        {
            return null;
        }

        return cell.Name == "entry" ? ApplyCals(cell, operation) : ApplySimple(cell, operation);
    }

    // ================================================================ CALS

    private sealed record Cell(DitaNode Entry, int Row, int Col, int ColSpan, int RowSpan);

    /// <summary>Сетка одной секции tgroup (thead или tbody): какая ячейка занимает каждое место.</summary>
    private sealed class Section
    {
        public Section(DitaNode node, IReadOnlyList<string> names)
        {
            Node = node;
            Rows = node.ElementChildren().Where(r => r.Name == "row").ToList();
            At = new Cell?[Rows.Count, names.Count];
            for (var r = 0; r < Rows.Count; r++)
            {
                var cursor = 0;
                foreach (var entry in Rows[r].ElementChildren().Where(e => e.Name == "entry"))
                {
                    var start = IndexOf(names, entry.GetAttribute("namest")) ?? IndexOf(names, entry.GetAttribute("colname"));
                    if (start is null)
                    {
                        while (cursor < names.Count && At[r, cursor] is not null)
                        {
                            cursor++;
                        }

                        start = cursor;
                    }

                    var end = IndexOf(names, entry.GetAttribute("nameend")) ?? start.Value;
                    if (start.Value >= names.Count)
                    {
                        continue; // лишняя ячейка за пределами cols — в сетку не попадает
                    }

                    end = Math.Clamp(end, start.Value, names.Count - 1);
                    var rowSpan = Math.Clamp(MoreRows(entry) + 1, 1, Rows.Count - r);
                    var cell = new Cell(entry, r, start.Value, end - start.Value + 1, rowSpan);
                    Cells.Add(cell);
                    for (var rr = r; rr < r + rowSpan; rr++)
                    {
                        for (var c = start.Value; c <= end; c++)
                        {
                            At[rr, c] ??= cell;
                        }
                    }

                    cursor = end + 1;
                }
            }
        }

        public DitaNode Node { get; }

        public List<DitaNode> Rows { get; }

        public List<Cell> Cells { get; } = new();

        public Cell?[,] At { get; }

        /// <summary>Вставляет ячейку в строку перед первой ячейкой, что начинается правее столбца.</summary>
        public void InsertEntry(int row, int col, DitaNode entry)
        {
            var anchor = Cells.Where(c => c.Row == row && c.Col > col).OrderBy(c => c.Col).FirstOrDefault();
            if (anchor is not null && ReferenceEquals(anchor.Entry.Parent, Rows[row]))
            {
                Rows[row].Insert(Rows[row].IndexOf(anchor.Entry), entry);
            }
            else
            {
                Rows[row].Add(entry);
            }
        }
    }

    private static int? IndexOf(IReadOnlyList<string> names, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == name)
            {
                return i;
            }
        }

        return null;
    }

    private static int MoreRows(DitaNode entry) =>
        int.TryParse(entry.GetAttribute("morerows"), out var value) && value > 0 ? value : 0;

    private static void SetMoreRows(DitaNode entry, int value)
    {
        if (value > 0)
        {
            entry.SetAttribute("morerows", value.ToString());
        }
        else
        {
            entry.RemoveAttribute("morerows");
        }
    }

    private static DitaNode NewEntry(string? colname)
    {
        var entry = DitaNode.Element("entry");
        if (colname is not null)
        {
            entry.SetAttribute("colname", colname);
        }

        return entry;
    }

    private static DitaNode? ApplyCals(DitaNode entry, TableOperation operation)
    {
        if (Ancestor(entry, "tgroup") is not { } tgroup || entry.Parent?.Parent is not { } sectionNode)
        {
            return null;
        }

        var names = EditCommands.EnsureColumnNames(tgroup);
        var sections = tgroup.ElementChildren()
            .Where(e => e.Name is "thead" or "tbody")
            .Select(e => new Section(e, names))
            .ToList();
        var section = sections.First(s => ReferenceEquals(s.Node, sectionNode));
        if (section.Cells.FirstOrDefault(c => ReferenceEquals(c.Entry, entry)) is not { } cell)
        {
            return null;
        }

        return operation switch
        {
            TableOperation.InsertRowAbove => InsertCalsRow(section, cell, cell.Row, names),
            TableOperation.InsertRowBelow => InsertCalsRow(section, cell, cell.Row + cell.RowSpan, names),
            TableOperation.DeleteRow => DeleteCalsRow(section, cell, names),
            TableOperation.InsertColumnLeft => InsertCalsColumn(tgroup, sections, section, cell, cell.Col, names),
            TableOperation.InsertColumnRight => InsertCalsColumn(tgroup, sections, section, cell, cell.Col + cell.ColSpan, names),
            TableOperation.DeleteColumn => DeleteCalsColumn(tgroup, sections, section, cell, names),
            _ => SplitCalsCell(section, cell, names)
        };
    }

    private static DitaNode? InsertCalsRow(Section section, Cell cell, int at, IReadOnlyList<string> names)
    {
        // Ячейки, растянутые через место вставки, растягиваются ещё на строку, а не делятся.
        var spanning = new List<Cell>();
        var row = DitaNode.Element("row");
        var skipped = false;
        DitaNode? focus = null;
        for (var c = 0; c < names.Count; c++)
        {
            var over = at > 0 && at < section.Rows.Count ? section.At[at, c] : null;
            if (over is not null && over.Row < at)
            {
                if (!spanning.Contains(over))
                {
                    spanning.Add(over);
                }

                skipped = true;
                continue;
            }

            var created = NewEntry(skipped ? names[c] : null);
            row.Add(created);
            if (c >= cell.Col && focus is null)
            {
                focus = created;
            }
        }

        if (row.Children.Count == 0)
        {
            return null;
        }

        foreach (var over in spanning)
        {
            SetMoreRows(over.Entry, MoreRows(over.Entry) + 1);
        }

        if (at < section.Rows.Count)
        {
            section.Node.Insert(section.Node.IndexOf(section.Rows[at]), row);
        }
        else
        {
            section.Node.Insert(section.Node.IndexOf(section.Rows[^1]) + 1, row);
        }

        return focus ?? row.ElementChildren().First();
    }

    private static DitaNode? DeleteCalsRow(Section section, Cell cell, IReadOnlyList<string> names)
    {
        if (section.Rows.Count <= 1)
        {
            return null; // секции нужна хотя бы одна строка
        }

        var r = cell.Row;
        foreach (var other in section.Cells)
        {
            if (other.Row < r && other.Row + other.RowSpan > r)
            {
                SetMoreRows(other.Entry, MoreRows(other.Entry) - 1);
            }
        }

        // Ячейки, растянутые вниз из удаляемой строки, переезжают в следующую — с содержимым.
        foreach (var moving in section.Cells.Where(c => c.Row == r && c.RowSpan > 1).OrderBy(c => c.Col))
        {
            moving.Entry.RemoveSelf();
            SetMoreRows(moving.Entry, moving.RowSpan - 2);
            if (moving.Entry.GetAttribute("namest") is null)
            {
                moving.Entry.SetAttribute("colname", names[moving.Col]);
            }

            section.InsertEntry(r + 1, moving.Col, moving.Entry);
        }

        var target = r + 1 < section.Rows.Count ? section.Rows[r + 1] : section.Rows[r - 1];
        section.Rows[r].RemoveSelf();
        return target.ElementChildren().FirstOrDefault(e => e.Name == "entry");
    }

    private static DitaNode? InsertCalsColumn(DitaNode tgroup, List<Section> sections, Section own, Cell cell, int at,
        List<string> names)
    {
        var colspecs = tgroup.ElementChildren().Where(e => e.Name == "colspec").ToList();
        var name = UniqueColumnName(names);
        var colspec = DitaNode.Element("colspec");
        colspec.SetAttribute("colname", name);
        var neighbour = colspecs[Math.Min(at, colspecs.Count - 1)];
        if (neighbour.GetAttribute("colwidth") is { Length: > 0 } width)
        {
            colspec.SetAttribute("colwidth", width.Contains('*') ? "1*" : width);
        }

        tgroup.Insert(at < colspecs.Count ? tgroup.IndexOf(colspecs[at]) : tgroup.IndexOf(colspecs[^1]) + 1, colspec);

        DitaNode? focus = null;
        foreach (var section in sections)
        {
            for (var r = 0; r < section.Rows.Count; r++)
            {
                // Объединение через место вставки расширяется само: оно задано именами столбцов.
                if (at > 0 && at < names.Count && section.At[r, at - 1] is { } left && ReferenceEquals(left, section.At[r, at]))
                {
                    continue;
                }

                var created = NewEntry(name);
                section.InsertEntry(r, at - 1, created);
                if (ReferenceEquals(section, own) && r == cell.Row)
                {
                    focus = created;
                }
            }
        }

        UpdateColumnCount(tgroup);
        return focus ?? cell.Entry;
    }

    private static DitaNode? DeleteCalsColumn(DitaNode tgroup, List<Section> sections, Section own, Cell cell, List<string> names)
    {
        if (names.Count <= 1)
        {
            return null;
        }

        var c = cell.Col;
        foreach (var section in sections)
        {
            foreach (var other in section.Cells.Where(o => o.Col <= c && c < o.Col + o.ColSpan))
            {
                if (other.ColSpan == 1)
                {
                    other.Entry.RemoveSelf();
                    continue;
                }

                var start = other.Col == c ? c + 1 : other.Col;
                var end = other.Col + other.ColSpan - 1 == c ? c - 1 : other.Col + other.ColSpan - 1;
                if (start == end)
                {
                    other.Entry.RemoveAttribute("namest");
                    other.Entry.RemoveAttribute("nameend");
                    other.Entry.SetAttribute("colname", names[start]);
                }
                else
                {
                    other.Entry.SetAttribute("namest", names[start]);
                    other.Entry.SetAttribute("nameend", names[end]);
                }
            }

            // Строка, где не осталось ячеек (остальные места заняты растянутыми сверху), уходит
            // целиком — растянутые через неё ячейки становятся на строку короче.
            for (var r = section.Rows.Count - 1; r >= 0; r--)
            {
                if (section.Rows[r].ElementChildren().Any(e => e.Name == "entry"))
                {
                    continue;
                }

                foreach (var over in section.Cells.Where(o => o.Row < r && o.Row + o.RowSpan > r && o.Entry.Parent is not null))
                {
                    SetMoreRows(over.Entry, MoreRows(over.Entry) - 1);
                }

                section.Rows[r].RemoveSelf();
            }
        }

        tgroup.ElementChildren().Where(e => e.Name == "colspec").ElementAt(c).RemoveSelf();
        UpdateColumnCount(tgroup);

        var remaining = EditCommands.EnsureColumnNames(tgroup);
        var rebuilt = new Section(own.Node, remaining);
        var row = Math.Min(cell.Row, rebuilt.Rows.Count - 1);
        return row < 0 ? null : rebuilt.At[row, Math.Min(c, remaining.Count - 1)]?.Entry ?? rebuilt.Rows[row].ElementChildren().FirstOrDefault();
    }

    private static DitaNode? SplitCalsCell(Section section, Cell cell, IReadOnlyList<string> names)
    {
        if (cell.ColSpan == 1 && cell.RowSpan == 1)
        {
            return null;
        }

        var entry = cell.Entry;
        if (cell.ColSpan > 1)
        {
            entry.RemoveAttribute("namest");
            entry.RemoveAttribute("nameend");
            entry.SetAttribute("colname", names[cell.Col]);
            var index = entry.Parent!.IndexOf(entry);
            for (var c = cell.Col + 1; c < cell.Col + cell.ColSpan; c++)
            {
                entry.Parent.Insert(++index, NewEntry(names[c]));
            }
        }

        if (cell.RowSpan > 1)
        {
            entry.RemoveAttribute("morerows");
            for (var r = cell.Row + 1; r < cell.Row + cell.RowSpan; r++)
            {
                for (var c = cell.Col; c < cell.Col + cell.ColSpan; c++)
                {
                    section.InsertEntry(r, c, NewEntry(names[c]));
                }
            }
        }

        return entry;
    }

    private static string UniqueColumnName(IReadOnlyCollection<string> names)
    {
        for (var i = names.Count + 1; ; i++)
        {
            var candidate = $"c{i}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>@cols и @colnum у colspec — по новому числу и порядку столбцов.</summary>
    private static void UpdateColumnCount(DitaNode tgroup)
    {
        var colspecs = tgroup.ElementChildren().Where(e => e.Name == "colspec").ToList();
        tgroup.SetAttribute("cols", colspecs.Count.ToString());
        if (colspecs.Any(c => c.HasAttribute("colnum")))
        {
            for (var i = 0; i < colspecs.Count; i++)
            {
                colspecs[i].SetAttribute("colnum", (i + 1).ToString());
            }
        }
    }

    // ================================================================ simpletable

    private static bool HasClass(DitaNode node, string token) =>
        DitaCatalog.Default.Get(node.Name)?.ClassAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(token) == true;

    private static bool IsSimpleRow(DitaNode row) => HasClass(row, "topic/strow") || HasClass(row, "topic/sthead");

    private static DitaNode? ApplySimple(DitaNode cell, TableOperation operation)
    {
        var row = cell.Parent!;
        var table = row.Parent;
        if (table is null)
        {
            return null;
        }

        var rows = table.ElementChildren().Where(IsSimpleRow).ToList();
        var bodyRows = rows.Where(r => HasClass(r, "topic/strow")).ToList();
        var isHead = HasClass(row, "topic/sthead");
        var column = row.ElementChildren().ToList().IndexOf(cell);

        switch (operation)
        {
            case TableOperation.InsertRowAbove when isHead:
                return null; // шапка всегда первая
            case TableOperation.InsertRowAbove:
            case TableOperation.InsertRowBelow:
            {
                var template = isHead ? bodyRows.FirstOrDefault() : row;
                var created = NewSimpleRow(table, template, rows);
                if (created is null)
                {
                    return null;
                }

                var index = table.IndexOf(row) + (operation == TableOperation.InsertRowBelow ? 1 : 0);
                table.Insert(index, created);
                var cells = created.ElementChildren().ToList();
                return cells.Count == 0 ? null : cells[Math.Clamp(column, 0, cells.Count - 1)];
            }

            case TableOperation.DeleteRow:
            {
                if (!isHead && bodyRows.Count <= 1)
                {
                    return null; // хотя бы одна строка данных обязательна
                }

                var next = rows.SkipWhile(r => !ReferenceEquals(r, row)).Skip(1).FirstOrDefault()
                           ?? rows.TakeWhile(r => !ReferenceEquals(r, row)).LastOrDefault();
                row.RemoveSelf();
                return next?.ElementChildren().FirstOrDefault();
            }
        }

        // Столбцы — только у самой simpletable: у специализаций набор ячеек задан моделью.
        if (table.Name != "simpletable" || rows.Any(r => r.ElementChildren().Any(c => c.Name != "stentry")))
        {
            return null;
        }

        switch (operation)
        {
            case TableOperation.InsertColumnLeft:
            case TableOperation.InsertColumnRight:
            {
                var at = column + (operation == TableOperation.InsertColumnRight ? 1 : 0);
                DitaNode? focus = null;
                foreach (var r in rows)
                {
                    var created = DitaNode.Element("stentry");
                    var cells = r.ElementChildren().ToList();
                    if (at < cells.Count)
                    {
                        r.Insert(r.IndexOf(cells[at]), created);
                    }
                    else
                    {
                        r.Add(created);
                    }

                    if (ReferenceEquals(r, row))
                    {
                        focus = created;
                    }
                }

                EditColumnList(table, "relcolwidth", list => list.Insert(Math.Min(at, list.Count), "1*"));
                if (int.TryParse(table.GetAttribute("keycol"), out var key) && key > at)
                {
                    table.SetAttribute("keycol", (key + 1).ToString());
                }

                return focus;
            }

            case TableOperation.DeleteColumn:
            {
                if (rows.Max(r => r.ElementChildren().Count()) <= 1)
                {
                    return null;
                }

                foreach (var r in rows)
                {
                    var cells = r.ElementChildren().ToList();
                    if (column < cells.Count && cells.Count > 1)
                    {
                        cells[column].RemoveSelf();
                    }
                }

                EditColumnList(table, "relcolwidth", list =>
                {
                    if (column < list.Count)
                    {
                        list.RemoveAt(column);
                    }
                });
                if (int.TryParse(table.GetAttribute("keycol"), out var key))
                {
                    if (key == column + 1)
                    {
                        table.RemoveAttribute("keycol");
                    }
                    else if (key > column + 1)
                    {
                        table.SetAttribute("keycol", (key - 1).ToString());
                    }
                }

                var remaining = row.ElementChildren().ToList();
                return remaining[Math.Min(column, remaining.Count - 1)];
            }
        }

        return null;
    }

    /// <summary>Новая строка данных по образцу: те же ячейки, пустые (у simpletable — по числу столбцов).</summary>
    private static DitaNode? NewSimpleRow(DitaNode table, DitaNode? template, List<DitaNode> rows)
    {
        var name = template?.Name ?? table.Name switch
        {
            "simpletable" => "strow",
            "properties" => "property",
            "choicetable" => "chrow",
            _ => null
        };
        if (name is null)
        {
            return null;
        }

        var row = DitaNode.Element(name);
        if (name == "strow")
        {
            var count = Math.Max(1, rows.Max(r => r.ElementChildren().Count()));
            for (var i = 0; i < count; i++)
            {
                row.Add(DitaNode.Element("stentry"));
            }
        }
        else if (template is not null)
        {
            foreach (var child in template.ElementChildren())
            {
                row.Add(DitaNode.Element(child.Name));
            }
        }
        else
        {
            row = DitaCatalog.Default.CreateElement(name);
        }

        return row;
    }

    private static void EditColumnList(DitaNode table, string attribute, Action<List<string>> edit)
    {
        if (table.GetAttribute(attribute) is not { Length: > 0 } value)
        {
            return;
        }

        var list = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        edit(list);
        table.SetAttribute(attribute, string.Join(' ', list));
    }

    private static DitaNode? Ancestor(DitaNode node, string name)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current.Name == name)
            {
                return current;
            }
        }

        return null;
    }
}
