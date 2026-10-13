using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Таблицы из CSS: внешняя рамка и заливка таблицы с собственным классом, правила для ячеек этой таблицы (td, th), общий вид остальных не меняется.
internal static partial class CoreChecks
{
    private const string TableCssTopic = """
        <topic id="t"><title>Таблицы</title><body>
        <table outputclass="blue-frame"><tgroup cols="2"><thead><row><entry>Имя</entry><entry>Значение</entry></row></thead>
        <tbody><row><entry>Один</entry><entry>1</entry></row><row><entry>Два</entry><entry>2</entry></row></tbody></tgroup></table>
        <table><tgroup cols="2"><tbody><row><entry>Обычная</entry><entry>таблица</entry></row></tbody></tgroup></table>
        <simpletable outputclass="blue-frame"><sthead><stentry>А</stentry><stentry>Б</stentry></sthead><strow><stentry>в</stentry><stentry>г</stentry></strow></simpletable>
        </body></topic>
        """;

    // Как в замечании: у таблицы внешние голубые рамки толщиной 2 px; шапка — своя заливка и нижняя линия; левая колонка тела — заливка.
    private const string BlueFrameCss = """
        table.blue-frame { border: 2px solid #1E90FF; }
        .blue-frame th { background-color: #DCEBFA; border-bottom: 2px solid #1E90FF; }
        .blue-frame td:first-child { background-color: #F2F7FD; }
        """;

    internal static void TableCssTests()
    {
        Section("Таблицы из CSS: рамки и заливка у таблицы с классом");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = TableCssTopic,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>",
            ["custom.css"] = BlueFrameCss
        }, (root, project) =>
        {
            project.SetCustomCssPath("custom.css");
            var docx = Path.Combine(root, "d.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "рамки таблицы из CSS");
            Check(!result.Warnings.Any(w => w.Contains("blue-frame") || w.Contains("td") || w.Contains("th")), "селекторы таблиц не в списке «не подошли»: " + string.Join(" | ", result.Warnings));
            using var doc = WordprocessingDocument.Open(docx, false);
            var tables = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();
            Check(tables.Count == 3, "три таблицы в документе");

            var blue = tables[0].GetFirstChild<TableProperties>()!.TableBorders!;
            bool IsBlue(BorderType? b) => b?.Color?.Value == "1E90FF" && b.Size?.Value == 12 && b.Val?.Value == BorderValues.Single;
            Check(IsBlue(blue.TopBorder) && IsBlue(blue.LeftBorder) && IsBlue(blue.BottomBorder) && IsBlue(blue.RightBorder),
                "table.blue-frame: внешние рамки голубые, 2 px (1,5 пт)");
            Check(blue.InsideHorizontalBorder?.Color?.Value == "CCCCCC", "внутренние линии остались прежними");

            var plain = tables[1].GetFirstChild<TableProperties>()!.TableBorders!;
            Check(plain.TopBorder?.Color?.Value == "999999" && plain.LeftBorder?.Color?.Value == "999999", "таблица без класса — общий вид, рамка не изменилась");

            var cells = tables[0].Descendants<TableCell>().ToList();
            Check(cells[0].TableCellProperties?.Shading?.Fill?.Value == "DCEBFA", ".blue-frame th: заливка ячеек шапки");
            Check(IsBlue(cells[0].TableCellProperties?.TableCellBorders?.BottomBorder) && cells[0].TableCellProperties?.TableCellBorders?.TopBorder is null,
                ".blue-frame th: нижняя линия своя, остальные стороны не тронуты");
            Check(cells[2].TableCellProperties?.Shading?.Fill?.Value == "F2F7FD" && cells[3].TableCellProperties?.Shading?.Fill is null,
                ".blue-frame td:first-child: заливка только первой ячейки строки");

            var simple = tables[2].GetFirstChild<TableProperties>()!.TableBorders!;
            Check(IsBlue(simple.TopBorder) && IsBlue(simple.RightBorder), "simpletable с тем же классом — те же рамки");
        });

        // --- у каждой стороны своя рамка
        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = TableCssTopic,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>",
            ["custom.css"] = ".blue-frame { border-top: 3px double #FF0000; border-left: none; }"
        }, (root, project) =>
        {
            project.SetCustomCssPath("custom.css");
            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "рамки сторон таблицы");
            using var doc = WordprocessingDocument.Open(docx, false);
            var blue = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().First().GetFirstChild<TableProperties>()!.TableBorders!;
            Check(blue.TopBorder?.Val?.Value == BorderValues.Double && blue.TopBorder.Color?.Value == "FF0000", "border-top — только верхняя рамка");
            Check(blue.LeftBorder?.Val?.Value == BorderValues.Nil, "border-left: none — левой рамки нет");
            Check(blue.BottomBorder?.Color?.Value == "999999", "остальные стороны — общая рамка");
        });

    }

    internal static void ImageCssTests()
    {
        Section("Картинки из CSS: размер и рамка");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = """
                <topic id="t"><title>Картинки</title><body>
                <p><image href="wide.png" outputclass="small"/></p>
                <p><image href="wide.png" outputclass="half"/></p>
                <p><image href="wide.png" width="300px"/></p>
                <p><image href="wide.png" outputclass="capped"/></p>
                </body></topic>
                """,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>",
            ["custom.css"] = """
                img.small { width: 100px; border: 2px solid #FF0000; }
                .half { width: 50%; }
                image.capped { width: 1000px; max-width: 200px; }
                """
        }, (root, project) =>
        {
            File.WriteAllBytes(Path.Combine(root, "wide.png"), MakePng(400, 200));
            project.SetCustomCssPath("custom.css");
            var docx = Path.Combine(root, "d.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "картинки из CSS");
            Check(!result.Warnings.Any(w => w.Contains("img") || w.Contains("small") || w.Contains("half")), "правила для картинок не в списке «не подошли»: " + string.Join(" | ", result.Warnings));
            using var doc = WordprocessingDocument.Open(docx, false);
            var inlines = doc.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>().ToList();
            var sizes = inlines.Select(i => (Cx: i.Extent!.Cx!.Value, Cy: i.Extent.Cy!.Value)).ToList();
            Check(sizes.Count == 4, "четыре картинки");
            Check(sizes[0] == (952500L, 476250L), "img.small { width: 100px }: 100×50 px по пропорциям 2:1: " + sizes[0]);
            var textWidthEmu = (long)((210 - 40) / 25.4 * 914400);
            Check(Math.Abs(sizes[1].Cx - textWidthEmu / 2) < 20000 && Math.Abs(sizes[1].Cy * 2 - sizes[1].Cx) < 4, ".half { width: 50% } — половина ширины текста: " + sizes[1]);
            Check(sizes[2] == (2857500L, 1428750L), "без правил CSS — размер из атрибута width DITA: " + sizes[2]);
            Check(sizes[3] == (200L * 9525, 100L * 9525), "max-width: 200px ограничивает width: 1000px: " + sizes[3]);
            var outline = inlines[0].Descendants<DocumentFormat.OpenXml.Drawing.Outline>().FirstOrDefault();
            Check(outline?.Width?.Value == 19050 && outline.Descendants<DocumentFormat.OpenXml.Drawing.RgbColorModelHex>().First().Val?.Value == "FF0000", "border: 2px solid #FF0000 — линия вокруг картинки (1,5 пт, красная)");
            Check(!inlines[2].Descendants<DocumentFormat.OpenXml.Drawing.Outline>().Any(), "у картинки без правила рамки нет");
        });
    }
}
