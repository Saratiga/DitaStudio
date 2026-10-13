using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;

namespace DitaStudio.Tests;

// Ячейки таблицы в буфере обмена: текст Excel/Word ⇄ ячейки CALS, вставка прямоугольником, разметка внутри ячеек, блоки в ячейку.
internal static partial class CoreChecks
{
    private static DitaNode TableOf(string[][] rows)
    {
        var cols = rows.Max(r => r.Length);
        var body = string.Concat(rows.Select(r => "<row>" + string.Concat(r.Select(c => $"<entry>{c}</entry>")) + "</row>"));
        var colspecs = string.Concat(Enumerable.Range(1, cols).Select(i => $"<colspec colname=\"c{i}\" colnum=\"{i}\"/>"));
        return DitaDocument.Parse($"<topic id=\"t\"><title>T</title><body><table><tgroup cols=\"{cols}\">{colspecs}<tbody>{body}</tbody></tgroup></table></body></topic>")
            .Root.Descendants().First(n => n.Name == "table");
    }

    private static string[][] CellTexts(DitaNode table) =>
        table.Descendants().Where(n => n.Name == "row").Select(r => r.ElementChildren().Select(e => e.InnerText).ToArray()).ToArray();

    internal static void TableClipboardTests()
    {
        Section("Буфер обмена ячеек таблицы");

        // --- разбор текста Excel/Word
        Check(TableClipboard.Parse("один") is null && TableClipboard.Parse("") is null && TableClipboard.Parse(null) is null, "одна ячейка — это обычный текст");
        var two = TableClipboard.Parse("a\tb\r\nc\td\r\n")!;
        Check(two.Count == 2 && two[0].Count == 2 && two[1][1].Text == "d", "Excel: две строки по две ячейки, перевод строки в конце новой строки не даёт");
        var column = TableClipboard.Parse("a\nb\nc")!;
        Check(column.Count == 3 && column.All(r => r.Count == 1), "несколько строк без табуляций — столбец");
        var quoted = TableClipboard.Parse("\"в ячейке\nдве строки\"\t\"с \"\"кавычкой\"\"\"\r\nx\ty")!;
        Check(quoted.Count == 2 && quoted[0][0].Text == "в ячейке\nдве строки" && quoted[0][1].Text == "с \"кавычкой\"", "ячейки в кавычках: переводы строк и удвоенные кавычки");
        var ragged = TableClipboard.Parse("a\tb\tc\nd")!;
        Check(ragged.All(r => r.Count == 3) && ragged[1][2].Text == "", "короткие строки дополняются пустыми ячейками");
        Check(TableClipboard.ToText(new[] { new[] { "a", "б\tв" }, new[] { "\"", "" } }) == "a\t\"б\tв\"\r\n\"\"\"\"\t", "обратная запись: кавычки там, где табуляция или кавычка");

        // --- вставка: прямоугольник от ячейки, таблица удлиняется, лишние столбцы отбрасываются
        var table = TableOf(new[] { new[] { "1", "2", "3" }, new[] { "4", "5", "6" } });
        var result = TableClipboard.Paste(table, 1, 1, TableClipboard.Parse("a\tb\tc\nd\te\tf\ng\th\ti")!);
        var texts = CellTexts(table);
        Check(result.Written == 6 && result.AddedRows == 2 && result.SkippedColumns == 1, $"записано 6, строк добавлено 2, столбец отброшен: {result}");
        Check(texts.Length == 4 && texts[0].SequenceEqual(new[] { "1", "2", "3" }) && texts[1].SequenceEqual(new[] { "4", "a", "b" }), "строки: " + string.Join(" | ", texts.Select(r => string.Join(",", r))));
        Check(texts[2].SequenceEqual(new[] { "", "d", "e" }) && texts[3].SequenceEqual(new[] { "", "g", "h" }), "добавленные строки заполнены с нужного столбца");

        // --- копирование и вставка с разметкой внутри ячеек
        var source = TableOf(new[] { new[] { "<p>абзац <b>жирный</b></p>", "просто" }, new[] { "три", "четыре" } });
        var grid = CellGrid.Of(source)!;
        var copied = TableClipboard.Copy(grid, new CellRange(0, 1, 0, 1));
        Check(copied.Text == "абзац жирный\tпросто\r\nтри\tчетыре" && copied.Rows[0][0].Content is { Count: 1 }, "копия: текст для буфера и разметка ячейки");
        var target = TableOf(new[] { new[] { "", "" }, new[] { "", "" } });
        TableClipboard.LastCopied = copied;
        TableClipboard.Paste(target, 0, 0, copied.Rows);
        var pasted = target.Descendants().First(n => n.Name == "entry");
        Check(pasted.FirstElement("p")?.FirstElement("b")?.InnerText == "жирный", "разметка внутри ячейки (p, b) переехала целиком");
        Check(!ReferenceEquals(pasted.FirstElement("p"), copied.Rows[0][0].Content![0]), "вставляется копия: одну скопированную ячейку можно вставить много раз");
        TableClipboard.LastCopied = null;

        // --- объединённая ячейка копируется один раз
        var merged = DitaDocument.Parse("<topic id=\"t\"><title>T</title><body><table><tgroup cols=\"2\"><colspec colname=\"c1\" colnum=\"1\"/><colspec colname=\"c2\" colnum=\"2\"/><tbody>" +
            "<row><entry namest=\"c1\" nameend=\"c2\">широкая</entry></row><row><entry>л</entry><entry>п</entry></row></tbody></tgroup></table></body></topic>").Root.Descendants().First(n => n.Name == "table");
        var mergedCopy = TableClipboard.Copy(CellGrid.Of(merged)!, new CellRange(0, 1, 0, 1));
        Check(mergedCopy.Text == "широкая\t\r\nл\tп", "объединённая ячейка — в левой верхней клетке, вторая клетка пустая: " + mergedCopy.Text.Replace("\r\n", "|"));

        // --- блоки внутрь ячейки, а не после таблицы
        var cellTable = TableOf(new[] { new[] { "текст" } });
        var entry = cellTable.Descendants().First(n => n.Name == "entry");
        var blocks = BlockClipboard.Parse("<p>вставленный</p>")!;
        var place = BlockClipboard.FindInsertion(entry, blocks);
        Check(place is not null && ReferenceEquals(place.Parent, entry), "блок встаёт внутрь ячейки, где курсор, а не после всей таблицы");
    }
}
