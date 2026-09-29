using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Подписи таблиц и рисунков: пустой <title/> — без «Таблица №» / «Рисунок №» и без расхода номера.
internal static partial class CoreChecks
{
    private const string CaptionTopic = """
        <topic id="t"><title>Подписи</title><body>
        <table><title/><tgroup cols="1"><tbody><row><entry>Без названия</entry></row></tbody></tgroup></table>
        <table><title>   </title><tgroup cols="1"><tbody><row><entry>Пробелы</entry></row></tbody></tgroup></table>
        <table><title>Первая с названием</title><tgroup cols="1"><tbody><row><entry>А</entry></row></tbody></tgroup></table>
        <fig><title/><image href="none.png"/></fig>
        <fig><title>Первый рисунок</title><image href="none.png"/></fig>
        <table><title>Вторая с названием</title><tgroup cols="1"><tbody><row><entry>Б</entry></row></tbody></tgroup></table>
        </body></topic>
        """;

    internal static void CaptionTests()
    {
        Section("Подписи таблиц и рисунков: пустой заголовок");

        Check(!CaptionRules.HasContent(null), "подпись: нет элемента title — подписи нет");
        Check(!CaptionRules.HasContent(DitaNode.Element("title")), "подпись: пустой <title/> — подписи нет");
        var blank = DitaNode.Element("title");
        blank.SetText("  \n ");
        Check(!CaptionRules.HasContent(blank), "подпись: заголовок из одних пробелов — подписи нет");
        var text = DitaNode.Element("title");
        text.SetText("Название");
        Check(CaptionRules.HasContent(text), "подпись: заголовок с текстом — подпись есть");
        var keyword = DitaNode.Element("title");
        keyword.Add(DitaNode.Element("keyword"));
        Check(CaptionRules.HasContent(keyword), "подпись: заголовок из одного элемента (ключевое слово) — подпись есть");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "t.dita"), CaptionTopic);
            File.WriteAllText(Path.Combine(root, "m.ditamap"), "<map><title>Подписи</title><topicref href=\"t.dita\"/></map>");
            var project = new DitaProject(root);
            project.Scan();
            var map = Path.Combine(root, "m.ditamap");

            var entry = new HtmlPublisher(project).Publish(map, new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile;
            var html = File.ReadAllText(entry);
            Check(html.Split("class=\"table-title\"").Length - 1 == 2, "HTML: подпись только у двух таблиц с названием");
            Check(html.Contains("Таблица 1. Первая с названием") && html.Contains("Таблица 2. Вторая с названием"),
                "HTML: пустые заголовки не занимают номера — «Таблица 1», затем «Таблица 2»");
            Check(!html.Contains("Таблица 3") && !html.Contains("Таблица 1. <"), "HTML: лишних «Таблица №» нет");
            Check(html.Split("class=\"fig-title\"").Length - 1 == 1 && html.Contains("Рисунок 1. Первый рисунок"),
                "HTML: у рисунка с пустым заголовком подписи нет, у второго — «Рисунок 1»");

            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "подписи с пустым заголовком");
            List<string> tables, figures;
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                var paragraphs = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
                tables = paragraphs.Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "TableCaption").Select(p => p.InnerText).ToList();
                figures = paragraphs.Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "FigureCaption").Select(p => p.InnerText).ToList();
            }

            Check(tables.SequenceEqual(new[] { "Таблица 1. Первая с названием", "Таблица 2. Вторая с названием" }),
                $"DOCX: подписи только у таблиц с названием, нумерация подряд ({string.Join(" | ", tables)})");
            Check(figures.SequenceEqual(new[] { "Рисунок 1. Первый рисунок" }),
                $"DOCX: у рисунка с пустым заголовком подписи нет ({string.Join(" | ", figures)})");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    internal static void FigureCaptionFormatTests()
    {
        Section("Подпись рисунка: формат, поле SEQ, по центру, HTML и DOCX");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = """
                <topic id="t"><title>Р</title><body>
                <fig><title>Первый</title><image href="pic.png"/></fig>
                <fig><title/><image href="none.png"/></fig>
                <fig><title>Второй</title><image href="none.png"/></fig>
                </body></topic>
                """,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            File.WriteAllBytes(Path.Combine(root, "pic.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));
            var map = Path.Combine(root, "m.ditamap");
            string Html(string dir) => File.ReadAllText(new HtmlPublisher(project).Publish(map,
                new PublishOptions { OutputDirectory = Path.Combine(root, dir), SingleFile = true }).EntryFile);

            var html = Html("h1");
            Check(html.Contains("Рисунок 1. Первый") && html.Contains("Рисунок 2. Второй"), "HTML: по умолчанию «Рисунок N. Название»");
            Check(html.Contains("figure { text-align: center; }") && html.Contains("figcaption.fig-title { text-align: center; }"),
                "HTML: рисунок и подпись по умолчанию по центру");

            project.SetDocxLayout(new DocxLayout { CaptionSeparator = CaptionSeparator.Dash });
            html = Html("h2");
            Check(html.Contains("Рисунок 1 — Первый") && html.Contains("Рисунок 2 — Второй"), "HTML: формат «Рисунок N — Название»");

            project.SetDocxLayout(new DocxLayout { NumberFiguresAndTables = false });
            html = Html("h3");
            Check(!html.Contains("Рисунок 1") && html.Contains(">Первый<"), "HTML: переключатель «Нумеровать» отключает номера и в HTML");

            project.SetDocxLayout(new DocxLayout { CaptionSeparator = CaptionSeparator.Dash });
            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "подписи рисунков: SEQ и тире");
            using var doc = WordprocessingDocument.Open(docx, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var captions = body.Descendants<Paragraph>().Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "FigureCaption").ToList();
            Check(captions.Select(c => c.InnerText).SequenceEqual(new[] { "Рисунок 1 — Первый", "Рисунок 2 — Второй" }),
                "DOCX: подписи «Рисунок N — Название», пустой заголовок номера не занимает");
            Check(captions.All(c => c.Descendants<SimpleField>().Any(f => f.Instruction?.Value?.Contains("SEQ") == true)),
                "DOCX: номер — поле Word SEQ (обновляется, попадает в список иллюстраций)");
            var style = doc.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<Style>().First(s => s.StyleId == "FigureCaption");
            Check(style.StyleParagraphProperties?.Justification?.Val?.Value == JustificationValues.Center, "DOCX: стиль подписи рисунка — по центру");
            var pictures = body.Descendants<Paragraph>().Where(p => p.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Any()).ToList();
            Check(pictures.Count == 1 && pictures[0].ParagraphProperties?.Justification?.Val?.Value == JustificationValues.Center, "DOCX: абзац с рисунком по центру");
        });
    }
}
