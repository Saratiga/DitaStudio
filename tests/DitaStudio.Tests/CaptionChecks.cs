using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Подписи таблиц и рисунков: пустой <title/> — только «Таблица N» / «Рисунок N» (номер расходуется), элемента title нет — подписи нет (Г4).
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

        Check(!CaptionRules.HasCaption(null) && !CaptionRules.HasContent(null), "подпись: нет элемента title — подписи нет");
        Check(CaptionRules.HasCaption(DitaNode.Element("title")) && !CaptionRules.HasContent(DitaNode.Element("title")), "подпись: пустой <title/> — подпись есть, названия нет");
        var blank = DitaNode.Element("title");
        blank.SetText("  \n ");
        Check(!CaptionRules.HasContent(blank), "подпись: заголовок из одних пробелов — названия нет");
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
            Check(html.Split("class=\"table-title\"").Length - 1 == 4, "HTML: подпись у всех четырёх таблиц с элементом title");
            Check(html.Contains(">Таблица 1</div>") && html.Contains(">Таблица 2</div>") &&
                  html.Contains("Таблица 3. Первая с названием") && html.Contains("Таблица 4. Вторая с названием"),
                "HTML: пустые заголовки — «Таблица 1», «Таблица 2» без точки, номер расходуется");
            Check(html.Split("class=\"fig-title\"").Length - 1 == 2 && html.Contains(">Рисунок 1</figcaption>") && html.Contains("Рисунок 2. Первый рисунок"),
                "HTML: у рисунка с пустым заголовком — «Рисунок 1», у второго — «Рисунок 2. Первый рисунок»");

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

            Check(tables.SequenceEqual(new[] { "Таблица 1", "Таблица 2", "Таблица 3. Первая с названием", "Таблица 4. Вторая с названием" }),
                $"DOCX: подписи только у таблиц с названием, нумерация подряд ({string.Join(" | ", tables)})");
            Check(figures.SequenceEqual(new[] { "Рисунок 1", "Рисунок 2. Первый рисунок" }),
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
            Check(html.Contains("Рисунок 1. Первый") && html.Contains(">Рисунок 2</figcaption>") && html.Contains("Рисунок 3. Второй"), "HTML: по умолчанию «Рисунок N. Название», пустой заголовок — «Рисунок N»");
            Check(html.Contains("figure { text-align: center; }") && html.Contains("figcaption.fig-title { text-align: center; }"),
                "HTML: рисунок и подпись по умолчанию по центру");

            project.SetDocxLayout(new DocxLayout { CaptionSeparator = CaptionSeparator.Dash });
            html = Html("h2");
            Check(html.Contains("Рисунок 1 — Первый") && html.Contains("Рисунок 3 — Второй"), "HTML: формат «Рисунок N — Название»");

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
            Check(captions.Select(c => c.InnerText).SequenceEqual(new[] { "Рисунок 1 — Первый", "Рисунок 2", "Рисунок 3 — Второй" }),
                "DOCX: подписи «Рисунок N — Название», пустой заголовок — «Рисунок N» без тире");
            Check(captions.All(c => c.Descendants<SimpleField>().Any(f => f.Instruction?.Value?.Contains("SEQ") == true)),
                "DOCX: номер — поле Word SEQ (обновляется, попадает в список иллюстраций)");
            var style = doc.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<Style>().First(s => s.StyleId == "FigureCaption");
            Check(style.StyleParagraphProperties?.Justification?.Val?.Value == JustificationValues.Center, "DOCX: стиль подписи рисунка — по центру");
            var pictures = body.Descendants<Paragraph>().Where(p => p.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Any()).ToList();
            Check(pictures.Count == 1 && pictures[0].ParagraphProperties?.Justification?.Val?.Value == JustificationValues.Center, "DOCX: абзац с рисунком по центру");
        });
    }

    // Г4 (docs/REVIEW_PLAN_3.md): пустой <title/> у рисунка и таблицы — подпись только «Рисунок N» / «Таблица N»
    // (без точки и названия, номер расходуется); элемента title нет вообще — подписи нет. Ловушка: в CoreTests пока Skip.
    internal static void EmptyTitleCaptionTests()
    {
        Section("Подпись без названия: «Рисунок N» / «Таблица N»");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = """
                <topic id="t"><title>Р</title><body>
                <fig><title>Первый</title><image href="none.png"/></fig>
                <fig><title/><image href="none.png"/></fig>
                <fig><image href="none.png"/></fig>
                <fig><title>Третий</title><image href="none.png"/></fig>
                <table><title/><tgroup cols="1"><tbody><row><entry>А</entry></row></tbody></tgroup></table>
                <table><tgroup cols="1"><tbody><row><entry>Б</entry></row></tbody></tgroup></table>
                <table><title>Именная</title><tgroup cols="1"><tbody><row><entry>В</entry></row></tbody></tgroup></table>
                </body></topic>
                """,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            var map = Path.Combine(root, "m.ditamap");
            var html = File.ReadAllText(new HtmlPublisher(project).Publish(map,
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile);
            Check(html.Contains("Рисунок 1. Первый") && html.Contains(">Рисунок 2</figcaption>") && html.Contains("Рисунок 3. Третий"),
                "HTML: пустой <title/> — «Рисунок 2» без точки, номер расходуется; рисунок без title — без подписи");
            Check(html.Contains(">Таблица 1</") && html.Contains("Таблица 2. Именная"), "HTML: пустой <title/> таблицы — «Таблица 1»");

            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            using var doc = WordprocessingDocument.Open(docx, false);
            var captions = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>()
                .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val is { } id && id == "FigureCaption" || p.ParagraphProperties?.ParagraphStyleId?.Val == "TableCaption")
                .Select(p => p.InnerText).ToList();
            Check(captions.SequenceEqual(new[] { "Рисунок 1. Первый", "Рисунок 2", "Рисунок 3. Третий", "Таблица 1", "Таблица 2. Именная" }),
                $"DOCX: подписи ({string.Join(" | ", captions)})");
        });
    }
}
