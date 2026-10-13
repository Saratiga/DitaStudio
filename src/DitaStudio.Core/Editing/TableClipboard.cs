using System.Text;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Editing;

/// <summary>Ячейка для вставки: содержимое-узлы (копия из «Автора») или просто текст (из Word/Excel).</summary>
public sealed record PastedCell(string Text, IReadOnlyList<DitaNode>? Content = null);

/// <summary>Скопированные ячейки: матрица (строки × столбцы) и тот же набор текстом для буфера обмена (табуляции и переводы строк, как у Excel).</summary>
public sealed record CopiedCells(IReadOnlyList<IReadOnlyList<PastedCell>> Rows, string Text);

/// <summary>Итог вставки в таблицу.</summary>
public readonly record struct CellPasteResult(int Written, int AddedRows, int SkippedColumns);

/// <summary>
/// Копирование и вставка ячеек CALS-таблицы. В буфер обмена ячейки идут текстом с табуляциями и переводами строк — так их понимают Excel
/// и Word и так же приходят оттуда; своё содержимое (разметка внутри ячеек) редактор помнит сам, пока в буфере тот же текст.
/// Вставка ложится прямоугольником от левой верхней ячейки; не хватает строк — таблица удлиняется, лишние столбцы отбрасываются.
/// </summary>
public static class TableClipboard
{
    /// <summary>Что редактор последним положил в буфер обмена как ячейки: если в буфере тот же текст, вставляется разметка ячеек, а не одна строка текста.</summary>
    public static CopiedCells? LastCopied { get; set; }

    /// <summary>Копирует прямоугольник ячеек (содержимое — копии узлов).</summary>
    public static CopiedCells Copy(CellGrid grid, CellRange range)
    {
        var rows = new List<IReadOnlyList<PastedCell>>();
        for (var r = range.Row0; r <= range.Row1; r++)
        {
            var row = new List<PastedCell>();
            for (var c = range.Col0; c <= range.Col1; c++)
            {
                // Объединённая ячейка копируется в своей верхней левой клетке, остальные клетки её площади пустые.
                if (grid.At(r, c) is { } cell && cell.Row == r && cell.Col == c)
                {
                    row.Add(new PastedCell(PlainText(cell.Entry), cell.Entry.Children.Select(n => n.CloneDeep()).ToList()));
                }
                else
                {
                    row.Add(new PastedCell(string.Empty));
                }
            }

            rows.Add(row);
        }

        return new CopiedCells(rows, ToText(rows.Select(r => r.Select(c => c.Text).ToList()).ToList()));
    }

    /// <summary>Текст ячейки одной строкой: без табуляций и переводов строк, которые в буфере разделяют ячейки.</summary>
    private static string PlainText(DitaNode entry) =>
        string.Join(" ", entry.InnerText.Split(new[] { '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

    /// <summary>Матрица как текст буфера обмена (Excel): ячейки — табуляция, строки — перевод строки; ячейка с кавычкой берётся в кавычки.</summary>
    public static string ToText(IReadOnlyList<IReadOnlyList<string>> rows) =>
        string.Join("\r\n", rows.Select(r => string.Join("\t", r.Select(Quote))));

    private static string Quote(string cell) => cell.Contains('"') || cell.Contains('\t') || cell.Contains('\n') ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;

    /// <summary>Похож ли текст на таблицу: есть табуляция или несколько строк. Одна строка без табуляций — обычный текст.</summary>
    public static bool LooksTabular(string? text) =>
        !string.IsNullOrEmpty(text) && (text.Contains('\t') || text.TrimEnd('\r', '\n').Contains('\n'));

    /// <summary>
    /// Разбирает текст из буфера обмена (Excel, Word, этот же редактор) в ячейки. Ячейка в кавычках может содержать табуляции, переводы строк
    /// и удвоенные кавычки. Пустой последний перевод строки новой строки не даёт. Null — в тексте одна ячейка.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PastedCell>>? Parse(string? text)
    {
        if (!LooksTabular(text))
        {
            return null;
        }

        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var atStart = true; // ячейка ещё пуста: открывающая кавычка допустима только здесь
        for (var i = 0; i < text!.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"' when atStart:
                    quoted = true;
                    atStart = false;
                    break;
                case '\t':
                    row.Add(cell.ToString());
                    cell.Clear();
                    atStart = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString());
                    rows.Add(row);
                    row = new List<string>();
                    cell.Clear();
                    atStart = true;
                    break;
                default:
                    cell.Append(ch);
                    atStart = false;
                    break;
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        if (rows.Count == 0 || rows.Count == 1 && rows[0].Count <= 1)
        {
            return null;
        }

        var width = rows.Max(r => r.Count);
        return rows.Select(r => (IReadOnlyList<PastedCell>)Enumerable.Range(0, width).Select(c => new PastedCell(c < r.Count ? r[c].Trim() : string.Empty)).ToList()).ToList();
    }

    /// <summary>
    /// Кладёт ячейки в таблицу от места (<paramref name="row"/>, <paramref name="column"/>): прежнее содержимое ячеек заменяется. Не хватает
    /// строк — добавляются снизу; столбцы за правым краем не вставляются (их число в результате).
    /// </summary>
    public static CellPasteResult Paste(DitaNode table, int row, int column, IReadOnlyList<IReadOnlyList<PastedCell>> cells)
    {
        if (CellGrid.Of(table) is not { } grid || cells.Count == 0)
        {
            return default;
        }

        var added = 0;
        for (var missing = row + cells.Count - grid.Rows; missing > 0; missing--)
        {
            // Строка вставляется под последней: каждый раз по свежей сетке, чтобы объединённые ячейки не сбили счёт.
            grid = CellGrid.Of(table)!;
            if (TableRanges.InsertRows(grid.Tgroup, new CellRange(grid.Rows - 1, grid.Rows - 1, 0, grid.Columns - 1), above: false) == 0)
            {
                break;
            }

            added++;
        }

        grid = CellGrid.Of(table)!;

        var written = 0;
        var skipped = 0;
        var done = new HashSet<DitaNode>(ReferenceEqualityComparer.Instance);
        for (var r = 0; r < cells.Count; r++)
        {
            for (var c = 0; c < cells[r].Count; c++)
            {
                if (column + c >= grid.Columns)
                {
                    skipped = Math.Max(skipped, cells[r].Count - c);
                    break;
                }

                if (grid.At(row + r, column + c) is not { } target || !done.Add(target.Entry))
                {
                    continue;
                }

                Fill(target.Entry, cells[r][c]);
                written++;
            }
        }

        return new CellPasteResult(written, added, skipped);
    }

    private static void Fill(DitaNode entry, PastedCell cell)
    {
        entry.SetText(string.Empty);
        if (cell.Content is { Count: > 0 })
        {
            foreach (var node in cell.Content)
            {
                entry.Add(node.CloneDeep()); // ту же ячейку можно вставить несколько раз
            }
        }
        else if (cell.Text.Length > 0)
        {
            entry.SetText(cell.Text);
        }
    }
}
