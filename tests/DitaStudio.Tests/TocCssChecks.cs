using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Оглавление из CSS: .toc-title, .toc, .toc .lvlN, .toc .lvl1 > .num, content названия, скрытие уровней и всего оглавления.
internal static partial class CoreChecks
{
    private static Dictionary<string, string> TocFiles(string css) => new()
    {
        ["a.dita"] = "<topic id=\"a\"><title>Глава один</title><body><p>Текст</p><topic id=\"a1\"><title>Раздел</title><body><p>x</p></body></topic></body></topic>",
        ["b.dita"] = "<topic id=\"b\"><title>Глава два</title><body><p>Текст</p></body></topic>",
        ["m.ditamap"] = "<map><title>Книга</title><topicref href=\"a.dita\"><topicref href=\"b.dita\"/></topicref></map>",
        ["custom.css"] = css
    };

    // CSS из замечания: заголовок капсом, шрифт и отступы; уровни 1 и 2 одинаково; «таб» между цифрой и текстом.
    private const string TocUserCss = """
        .toc-title {
            text-transform: uppercase;
            font-family: "Arial Black", Arial, sans-serif;
            font-size: 14pt;
            font-weight: normal;
            margin: 0 0 0.3em 0;
        }
        .toc { font-size: 12pt; font-weight: normal; }
        .toc .lvl1,
        .toc .lvl2 { display: block; margin: 0; font-weight: normal; }
        .toc .lvl1 > .num { display: inline-block; min-width: 1.2em; margin-right: 0.5em; }
        """;

    private static string TocDocx(string root, DitaProject project)
    {
        project.SetCustomCssPath("custom.css");
        var docx = Path.Combine(root, "d.docx");
        new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
        return docx;
    }

    internal static void TocCssTests()
    {
        Section("Оглавление из CSS");

        WithProject(TocFiles(TocUserCss), (root, project) =>
        {
            project.SetCustomCssPath("custom.css");
            var docx = Path.Combine(root, "d.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "оглавление из CSS");
            Check(!result.Warnings.Any(w => w.Contains(".toc")), "селекторы оглавления не в списке «не подошли»: " + string.Join(" | ", result.Warnings));
            using var doc = WordprocessingDocument.Open(docx, false);
            var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(s => s.StyleId!.Value!);

            var heading = styles["TOCHeading"];
            Check(heading.StyleRunProperties?.Caps is not null, "название: text-transform: uppercase → капитель Word");
            Check(heading.StyleRunProperties?.RunFonts?.Ascii?.Value == "Arial Black", "название: шрифт Arial Black");
            Check(heading.StyleRunProperties?.FontSize?.Val?.Value == "28", "название: 14 пт");
            Check(heading.StyleRunProperties?.Bold?.Val?.Value == false, "название: font-weight: normal снимает жирность");
            Check(heading.StyleParagraphProperties?.SpacingBetweenLines?.After?.Value == "84", "название: нижний отступ 0,3em от 14 пт = 4,2 пт: " + heading.StyleParagraphProperties?.SpacingBetweenLines?.After?.Value);

            var level1 = styles["TOC1"];
            Check(level1.StyleRunProperties?.FontSize?.Val?.Value == "24", ".toc: строки оглавления 12 пт");
            Check(level1.StyleRunProperties?.Bold?.Val?.Value == false, ".toc .lvl1: не жирный");
            var indent = level1.StyleParagraphProperties?.Indentation;
            Check(indent?.Hanging?.Value == "408" && indent.Left?.Value == "408",
                "«таб» после номера: min-width 1.2em + margin-right 0.5em = 20,4 пт — висячий отступ 408 твипов: " + indent?.Left?.Value + "/" + indent?.Hanging?.Value);
            Check(styles["TOC2"].StyleRunProperties?.FontSize?.Val?.Value == "24" && styles["TOC2"].StyleParagraphProperties?.Indentation?.Hanging is null,
                ".toc .lvl2: тот же вид, без отступа под номер (правило .num только у lvl1)");
            Check(styles["TOC3"].StyleRunProperties?.FontSize?.Val?.Value == "24", ".toc действует на все уровни");
        });

        // --- название из CSS и скрытие уровня
        WithProject(TocFiles(".toc-title::before { content: \"Оглавление: \"; }\n.toc .lvl2 { display: none; }"), (root, project) =>
        {
            var docx = TocDocx(root, project);
            CheckValidDocx(docx, "название и уровни из CSS");
            using var doc = WordprocessingDocument.Open(docx, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var headings = body.Descendants<Paragraph>().Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "TOCHeading").Select(p => p.InnerText).ToList();
            Check(headings.SequenceEqual(new[] { "Оглавление: Содержание" }), "::before у .toc-title дописывает текст к названию по умолчанию: " + string.Join("|", headings));
            var field = body.Descendants<FieldCode>().First(f => f.Text.Contains("TOC")).Text;
            Check(field.Contains("\"1-1\""), ".toc .lvl2 { display: none } — в поле оглавления только 1-й уровень: " + field);
        });

        WithProject(TocFiles(".toc-title { content: \"Содержимое книги\"; }\n.toc-title::after { content: \" (кратко)\"; }"), (root, project) =>
        {
            var docx = TocDocx(root, project);
            using var doc = WordprocessingDocument.Open(docx, false);
            Check(doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().Any(p => p.InnerText == "Содержимое книги (кратко)"), "content у .toc-title заменяет название, ::after дописывает");
        });

        WithProject(TocFiles(".toc-title { display: none; }"), (root, project) =>
        {
            var docx = TocDocx(root, project);
            using var doc = WordprocessingDocument.Open(docx, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            Check(!body.Descendants<Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "TOCHeading") &&
                  body.Descendants<FieldCode>().Any(f => f.Text.Contains("TOC")), ".toc-title { display: none } — названия нет, оглавление есть");
        });

        WithProject(TocFiles(".toc { display: none; }"), (root, project) =>
        {
            var docx = TocDocx(root, project);
            using var doc = WordprocessingDocument.Open(docx, false);
            Check(!doc.MainDocumentPart!.Document.Body!.Descendants<FieldCode>().Any(f => f.Text.Contains("TOC")), ".toc { display: none } — оглавления в документе нет");
        });

        // --- HTML: разметка оглавления для CSS (toc, toc-title, lvlN, num, text) и номера разделов
        WithProject(TocFiles(string.Empty), (root, project) =>
        {
            project.SetDocxLayout(new DocxLayout { NumberHeadings = true, NumberingDepth = 3 });
            var site = Path.Combine(root, "site");
            new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { OutputDirectory = site, Language = "ru" });
            var page = File.ReadAllText(Path.Combine(site, "b.html"));
            Check(page.Contains("<aside class=\"toc\">") && page.Contains("<h2 class=\"toc-title\">Содержание</h2>"), "сайт: aside.toc с заголовком .toc-title");
            Check(page.Contains("<li class=\"lvl1 has-num\"><span class=\"num\">1</span><a class=\"text\" href=\"a.html\">Глава один</a>") &&
                  page.Contains("<li class=\"lvl2 has-num\"><span class=\"num\">1.1</span><a class=\"text current\" href=\"b.html\">Глава два</a>"),
                "сайт: li.lvlN > span.num + a.text, номер заголовка топика, у текущей страницы a.current: " + page[page.IndexOf("<aside", StringComparison.Ordinal)..page.IndexOf("</aside>", StringComparison.Ordinal)].ReplaceLineEndings(""));

            var single = File.ReadAllText(new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "one"), SingleFile = true, Language = "ru" }).EntryFile);
            Check(single.Contains("<nav class=\"toc toc-inline\">") && single.Contains("<span class=\"num\">1</span> <a class=\"text\" href=\"#"),
                "единый файл: nav.toc с теми же классами и номерами");
            Check(single.IndexOf("toc-inline", StringComparison.Ordinal) < single.IndexOf("class=\"topic-chunk", StringComparison.Ordinal), "единый файл: оглавление перед текстом топиков");
            Check(single.Contains("aside.toc, .pager { display: none; }") && !single.Contains("\n.toc {"),"стили боковой панели относятся только к aside.toc — правила пользователя для .toc их не ломают");
        });

        WithProject(TocFiles(string.Empty), (root, project) =>
        {
            var site = Path.Combine(root, "site");
            new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { OutputDirectory = site, Language = "ru" });
            var page = File.ReadAllText(Path.Combine(site, "a.html"));
            Check(page.Contains("<li class=\"lvl1\"><a class=\"text current\" href=\"a.html\">Глава один</a>") && !page.Contains("class=\"num\""), "сайт без нумерации заголовков: без span.num");
        });

        // --- без правил про оглавление всё как раньше
        WithProject(TocFiles("p { color: red; }"), (root, project) =>
        {
            var docx = TocDocx(root, project);
            using var doc = WordprocessingDocument.Open(docx, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            Check(body.Descendants<Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "TOCHeading" && p.InnerText == "Содержание") &&
                  body.Descendants<FieldCode>().Any(f => f.Text.Contains("\"1-3\"")), "без правил CSS для оглавления — прежнее название и глубина");
        });
    }
}
