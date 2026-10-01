using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Validation;

namespace DitaStudio.Tests;

// Д10: сетка CALS-таблицы, диапазон ячеек и действия над ним (строки, столбцы, объединение, очистка, выравнивание).
internal static partial class CoreChecks
{
    private static DitaDocument GridTopic(int bodyRows = 3, int columns = 4)
    {
        var colspecs = string.Concat(Enumerable.Range(1, columns).Select(c => $"<colspec colname=\"c{c}\" colnum=\"{c}\"/>"));
        string Row(int r) => "<row>" + string.Concat(Enumerable.Range(1, columns).Select(c => $"<entry>r{r}c{c}</entry>")) + "</row>";
        return DitaDocument.Parse($"<concept id=\"c\"><title>Т</title><conbody><table><tgroup cols=\"{columns}\">{colspecs}" +
                                  $"<thead>{Row(0)}</thead><tbody>{string.Concat(Enumerable.Range(1, bodyRows).Select(Row))}</tbody></tgroup></table></conbody></concept>");
    }

    private static string CellText(CellGrid grid, int row, int column) => grid.At(row, column)?.Entry.InnerText ?? "—";

    private static int ErrorCount(DitaDocument document) =>
        new DitaValidator { CheckStyleRules = false }.Validate(document).Count(i => i.Severity == IssueSeverity.Error);

    internal static void TableRangeTests()
    {
        Section("Выделение ячеек таблицы: сетка и действия над диапазоном");

        // Сетка: 4 строки (шапка + 3) × 4 столбца.
        var document = GridTopic();
        var tgroup = document.Root.DescendantsAndSelf().First(n => n.Name == "tgroup");
        var grid = CellGrid.Of(tgroup)!;
        Check(grid.Rows == 4 && grid.Columns == 4 && grid.Cells.Count == 16 && CellText(grid, 2, 2) == "r2c3", "сетка: 4 строки × 4 столбца, 16 ячеек, ячейка (2;2) — r2c3");
        Check(CellGrid.Of(grid.At(1, 1)!.Entry)!.Cells.Count == 16 && CellGrid.Of(tgroup.Parent!)!.Cells.Count == 16, "сетку можно получить от ячейки, tgroup и таблицы");
        var range = grid.RangeBetween(grid.At(2, 2)!.Entry, grid.At(1, 1)!.Entry)!.Value;
        Check(range == new CellRange(1, 2, 1, 2) && grid.CellsIn(range).Count == 4 && range.Rows == 2 && range.Columns == 2, "прямоугольник между двумя ячейками (в любом порядке) — 2×2, четыре ячейки");
        Check(grid.WholeRow(1) == new CellRange(1, 1, 0, 3) && grid.WholeColumn(2) == new CellRange(0, 3, 2, 2) && grid.WholeTable == new CellRange(0, 3, 0, 3), "строка, столбец и вся таблица");

        // Объединение 2×2 в теле: содержимое в левой верхней, остальные ячейки ушли, документ допустим.
        Check(TableRanges.CanMerge(tgroup, range), "объединение 2×2 возможно");
        var merged = TableRanges.Merge(tgroup, range)!;
        var after = CellGrid.Of(tgroup)!;
        Check(merged.GetAttribute("namest") == "c2" && merged.GetAttribute("nameend") == "c3" && merged.GetAttribute("morerows") == "1", "объединённая ячейка: namest/nameend и morerows");
        Check(after.At(2, 2) is { } cell && ReferenceEquals(cell.Entry, merged) && cell.RowSpan == 2 && cell.ColSpan == 2, "на сетке ячейка занимает 2×2");
        Check(merged.InnerText.Contains("r1c2") && merged.InnerText.Contains("r2c3") && after.Cells.Count == 13, "содержимое всех четырёх ячеек собрано в одной; ячеек стало 13");
        Check(ErrorCount(document) == 0, "документ после объединения допустим");

        // Прямоугольник, задевающий объединённую ячейку, расширяется до неё целиком.
        var grown = after.RangeBetween(after.At(1, 0)!.Entry, after.At(1, 1)!.Entry)!.Value; // (1;0)–(1;1): вторая — объединённая
        Check(grown == new CellRange(1, 2, 0, 2), $"выделение, задевшее объединённую ячейку, расширяется до неё целиком ({grown})");

        // Нельзя: одна ячейка, через границу шапки и тела, опустошив строку.
        var fresh = GridTopic();
        var fg = fresh.Root.DescendantsAndSelf().First(n => n.Name == "tgroup");
        var fgrid = CellGrid.Of(fg)!;
        Check(!TableRanges.CanMerge(fg, new CellRange(1, 1, 1, 1)), "одну ячейку не объединить");
        Check(!TableRanges.CanMerge(fg, new CellRange(0, 1, 0, 1)) && TableRanges.Merge(fg, new CellRange(0, 1, 0, 1)) is null, "через границу шапки и тела нельзя (morerows не переходит секцию)");
        Check(!TableRanges.CanMerge(fg, new CellRange(1, 2, 0, 3)), "две строки целиком нельзя: у строки CALS должна остаться хотя бы одна ячейка");
        Check(TableRanges.CanMerge(fg, new CellRange(1, 1, 0, 3)) && TableRanges.Merge(fg, new CellRange(1, 1, 0, 3)) is { } wholeRow &&
              wholeRow.GetAttribute("namest") == "c1" && wholeRow.GetAttribute("nameend") == "c4" && wholeRow.GetAttribute("morerows") is null && ErrorCount(fresh) == 0,
            "строку целиком можно объединить в одну ячейку на всю ширину");

        // Строки: вставка выше и ниже (столько, сколько выделено), удаление.
        var d2 = GridTopic();
        var g2 = d2.Root.DescendantsAndSelf().First(n => n.Name == "tgroup");
        Check(TableRanges.InsertRows(g2, new CellRange(1, 2, 0, 3), above: true) == 2 && CellGrid.Of(g2)!.Rows == 6 && CellText(CellGrid.Of(g2)!, 3, 0) == "r1c1",
            "две выделенные строки → две новые строки выше, прежние сдвинулись");
        Check(TableRanges.InsertRows(g2, new CellRange(3, 3, 0, 3), above: false) == 1 && CellGrid.Of(g2)!.Rows == 7, "одна строка ниже — одна новая");
        Check(TableRanges.DeleteRows(g2, new CellRange(1, 2, 0, 3)) == 2 && CellGrid.Of(g2)!.Rows == 5 && ErrorCount(d2) == 0, "удаление двух строк, документ допустим");

        // Столбцы.
        var d3 = GridTopic();
        var g3 = d3.Root.DescendantsAndSelf().First(n => n.Name == "tgroup");
        Check(TableRanges.InsertColumns(g3, new CellRange(0, 3, 1, 2), left: false) == 2 && CellGrid.Of(g3)!.Columns == 6 && CellText(CellGrid.Of(g3)!, 1, 5) == "r1c4",
            "два выделенных столбца → два новых справа");
        Check(TableRanges.DeleteColumns(g3, new CellRange(0, 3, 1, 2)) == 2 && CellGrid.Of(g3)!.Columns == 4 && CellText(CellGrid.Of(g3)!, 1, 1) is "" or "—" or { Length: >= 0 } &&
              ErrorCount(d3) == 0, "удаление двух столбцов, документ допустим");

        // Очистка и выравнивание.
        var d4 = GridTopic();
        var g4 = CellGrid.Of(d4.Root.DescendantsAndSelf().First(n => n.Name == "tgroup"))!;
        var entries = g4.CellsIn(new CellRange(1, 2, 0, 1)).Select(c => c.Entry).ToList();
        TableRanges.SetAlign(entries, "center");
        Check(entries.All(e => e.GetAttribute("align") == "center"), "выравнивание по центру у всех выделенных ячеек");
        TableRanges.SetAlign(entries, null);
        Check(entries.All(e => e.GetAttribute("align") is null), "снятие выравнивания");
        TableRanges.Clear(entries);
        Check(entries.All(e => e.Children.Count == 0) && g4.Cells.Count == 16 && ErrorCount(d4) == 0, "очистка: содержимое ушло, ячейки на месте");
    }
}
