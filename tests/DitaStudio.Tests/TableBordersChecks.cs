using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Г12: границы таблицы — frame, rowsep, colsep (стандартные атрибуты CALS) для всей таблицы, строки и столбца.
internal static partial class CoreChecks
{
    private const string BordersTable = """
        <table><tgroup cols="3"><thead><row><entry>Ш1</entry><entry>Ш2</entry><entry>Ш3</entry></row></thead>
        <tbody><row><entry>А1</entry><entry>А2</entry><entry>А3</entry></row><row><entry>Б1</entry><entry>Б2</entry><entry>Б3</entry></row></tbody></tgroup></table>
        """;

    internal static void TableBordersTests()
    {
        Section("Границы таблицы: frame, rowsep, colsep");

        DitaNode Table() => DitaDocument.Parse("<topic id=\"t\"><title>T</title><body>" + BordersTable + "</body></topic>").Root.DescendantsAndSelf().First(n => n.Name == "table");
        DitaNode Entry(DitaNode table, string text) => table.DescendantsAndSelf().First(n => n.Name == "entry" && n.InnerText == text);

        var plain = Table();
        Check(!CalsBorders.IsCustom(plain) && CalsBorders.FrameOf(plain) == "all", "обычная таблица — не «своя»");
        Check(CalsBorders.Compute(plain).Values.All(b => b == CellBorders.All), "по умолчанию у всех ячеек все линии");

        var none = Table();
        CalsBorders.SetMode(none, TableBorderMode.None);
        Check(none.GetAttribute("frame") == "none" && none.GetAttribute("rowsep") == "0" && none.GetAttribute("colsep") == "0" && CalsBorders.IsCustom(none), "«без границ»: frame=none, rowsep=0, colsep=0");
        Check(CalsBorders.Compute(none).Values.All(b => b is { Top: false, Right: false, Bottom: false, Left: false }), "«без границ»: ни у одной ячейки нет линий");

        var outer = Table();
        CalsBorders.SetMode(outer, TableBorderMode.OuterOnly);
        var o = CalsBorders.Compute(outer);
        Check(o[Entry(outer, "Ш1")] == new CellBorders(true, false, false, true) && o[Entry(outer, "Б3")] == new CellBorders(false, true, true, false) &&
              o[Entry(outer, "А2")] == new CellBorders(false, false, false, false), "«только внешняя рамка»: линии у краёв, внутри нет");

        var horizontal = Table();
        CalsBorders.SetMode(horizontal, TableBorderMode.HorizontalOnly);
        var h = CalsBorders.Compute(horizontal);
        Check(h[Entry(horizontal, "А2")] == new CellBorders(true, false, true, false) && h[Entry(horizontal, "Ш1")].Left == false,
            "«только горизонтальные»: линии над и под строками, вертикальных нет, боковых краёв нет");

        // Возврат «все границы» снимает атрибуты.
        CalsBorders.SetMode(none, TableBorderMode.All);
        Check(!CalsBorders.IsCustom(none) && !none.HasAttribute("frame") && !none.HasAttribute("rowsep"), "«все границы» возвращает таблицу к обычной");

        // Строка и столбец.
        var t = Table();
        var row = Entry(t, "А1").Parent!;
        CalsBorders.SetRowSeparator(row, false);
        var r = CalsBorders.Compute(t);
        Check(r[Entry(t, "А1")].Bottom == false && r[Entry(t, "А3")].Bottom == false && r[Entry(t, "Б1")].Top == false && r[Entry(t, "Ш1")].Bottom == true,
            "линия под строкой убрана только у этой строки; верх соседней ячейки согласован");
        CalsBorders.SetColumnSeparator(t.FirstElement("tgroup")!, 0, false);
        r = CalsBorders.Compute(t);
        Check(r[Entry(t, "А1")].Right == false && r[Entry(t, "А2")].Left == false && r[Entry(t, "Ш1")].Right == false && r[Entry(t, "А2")].Right == true,
            "линия справа от первого столбца убрана у всех ячеек столбца");
        Check(CalsBorders.LastColumnOf(Entry(t, "А2")) == 1 && CalsBorders.LastColumnOf(DitaNode.Element("entry")) == -1, "номер столбца ячейки");

        // Объединённая ячейка: последний столбец — по nameend.
        var merged = DitaDocument.Parse("<topic id=\"t\"><title>T</title><body><table frame=\"sides\"><tgroup cols=\"3\"><colspec colname=\"a\"/><colspec colname=\"b\"/><colspec colname=\"c\"/>" +
            "<tbody><row><entry namest=\"a\" nameend=\"b\">АБ</entry><entry>В</entry></row><row><entry>1</entry><entry>2</entry><entry>3</entry></row></tbody></tgroup></table></body></topic>")
            .Root.DescendantsAndSelf().First(n => n.Name == "table");
        Check(CalsBorders.LastColumnOf(Entry(merged, "АБ")) == 1, "объединённая ячейка заканчивается во втором столбце");
        var m = CalsBorders.Compute(merged);
        Check(m[Entry(merged, "АБ")].Top == false && m[Entry(merged, "В")].Right == true && m[Entry(merged, "АБ")].Bottom == true && m[Entry(merged, "3")].Bottom == false,
            "frame=sides: боковые линии есть, верха и низа нет");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = "<topic id=\"t\"><title>T</title><body>" + BordersTable.Replace("<table>", "<table frame=\"none\" rowsep=\"0\" colsep=\"1\">") + "</body></topic>",
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            var html = File.ReadAllText(new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile);
            Check(html.Contains("border-top:none;border-right:1px solid var(--line);border-bottom:none;border-left:none;"),
                "HTML: у ячеек без горизонтальных линий border-top/bottom: none, вертикальные — линия");

            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "таблица без горизонтальных линий");
            using var doc = WordprocessingDocument.Open(docx, false);
            var cellBorders = doc.MainDocumentPart!.Document.Body!.Descendants<TableCellBorders>().ToList();
            Check(cellBorders.Count == 9 && cellBorders.All(b => b.TopBorder?.Val?.Value == BorderValues.Nil && b.BottomBorder?.Val?.Value == BorderValues.Nil),
                $"DOCX: у девяти ячеек сверху и снизу нет линий ({cellBorders.Count})");
            Check(cellBorders.Any(b => b.RightBorder?.Val?.Value == BorderValues.Single), "DOCX: вертикальные линии остались");
            var tableBorders = doc.MainDocumentPart.Document.Body.Descendants<TableProperties>().First().TableBorders!;
            Check(tableBorders.TopBorder?.Val?.Value == BorderValues.Nil && tableBorders.InsideVerticalBorder?.Val?.Value == BorderValues.Nil, "DOCX: на уровне таблицы границ нет — всё в ячейках");
        });

        // Таблица по умолчанию публикуется как раньше — без tcBorders.
        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = "<topic id=\"t\"><title>T</title><body>" + BordersTable + "</body></topic>",
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            using var doc = WordprocessingDocument.Open(docx, false);
            Check(!doc.MainDocumentPart!.Document.Body!.Descendants<TableCellBorders>().Any(), "DOCX: обычная таблица — без tcBorders");
        });
    }
}
