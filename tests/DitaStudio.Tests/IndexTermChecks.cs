using System.Text.RegularExpressions;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Предметный указатель: страница в многостраничном сайте и поля XE/INDEX в DOCX.
internal static partial class CoreChecks
{
    internal static void IndexTermTests()
    {
        Section("Предметный указатель в сайте и DOCX");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.dita"), """
                <topic id="a"><title>Первый</title>
                <prolog><metadata><keywords><indexterm>Прологовый</indexterm></keywords></metadata></prolog>
                <body><p>Текст про <indexterm>Яблоко<indexterm>сорта</indexterm></indexterm>яблоки и
                <indexterm>Груша<index-see>Яблоко</index-see></indexterm>груши.</p></body></topic>
                """);
            File.WriteAllText(Path.Combine(root, "b.dita"), """
                <topic id="b"><title>Второй</title><body><p>Ещё <indexterm>Яблоко</indexterm>яблоки,
                <indexterm>Ёж<index-see-also>Груша</index-see-also></indexterm>ёж и
                <indexterm>Арбуз<sort-as>ярлык</sort-as></indexterm>арбуз.</p></body></topic>
                """);
            File.WriteAllText(Path.Combine(root, "plain.dita"), "<topic id=\"plain\"><title>Без терминов</title><body><p>Текст.</p></body></topic>");
            File.WriteAllText(Path.Combine(root, "m.ditamap"),
                "<map><title>Указатель</title><topicref href=\"a.dita\"/><topicref href=\"b.dita\"/></map>");
            File.WriteAllText(Path.Combine(root, "n.ditamap"),
                "<map><title>Без указателя</title><topicref href=\"plain.dita\"/></map>");
            var project = new DitaProject(root);
            project.Scan();
            var map = Path.Combine(root, "m.ditamap");

            // --- многостраничный сайт
            var site = Path.Combine(root, "site");
            var publish = new HtmlPublisher(project).Publish(map, new PublishOptions { OutputDirectory = site, Language = "ru" });
            var indexPath = Path.Combine(site, "index-terms.html");
            Check(File.Exists(indexPath) && publish.Files.Any(f => f.EndsWith("index-terms.html")), "сайт: страница index-terms.html написана");
            var indexHtml = File.Exists(indexPath) ? File.ReadAllText(indexPath) : string.Empty;
            Check(Regex.IsMatch(indexHtml, @"<li>Яблоко <a href=""a\.html#a"">1</a>, <a href=""b\.html#b"">2</a>"),
                "сайт: термин из двух топиков — ссылки на обе страницы");
            Check(indexHtml.Contains("<li>сорта <a href=\"a.html#a\">1</a>"), "сайт: подтермин вложен в термин");
            Check(indexHtml.Contains("Прологовый"), "сайт: термин из пролога попал в указатель");
            Check(indexHtml.Contains("<em>см.</em> Яблоко") && indexHtml.Contains("<em>см. также</em> Груша"), "сайт: index-see и index-see-also — отсылки");
            Check(indexHtml.IndexOf("Арбуз", StringComparison.Ordinal) > indexHtml.IndexOf("Яблоко", StringComparison.Ordinal),
                "сайт: sort-as ставит «Арбуз» после «Яблока»");
            Check(indexHtml.IndexOf("Груша", StringComparison.Ordinal) < indexHtml.IndexOf("Ёж", StringComparison.Ordinal), "сайт: термины по алфавиту");
            var firstPage = File.ReadAllText(Path.Combine(site, "a.html"));
            Check(firstPage.Contains("href=\"index-terms.html\">Указатель</a>"), "сайт: ссылка на указатель есть в навигации и на первой странице");
            Check(indexHtml.Contains("class=\"current\">Указатель</a>"), "сайт: на странице указателя пункт навигации текущий");
            Check(!firstPage.Contains("Яблоко<") && !firstPage.Contains("Прологовый"), "сайт: сами термины в тексте топика не выводятся");

            var plainSite = Path.Combine(root, "plain-site");
            new HtmlPublisher(project).Publish(Path.Combine(root, "n.ditamap"), new PublishOptions { OutputDirectory = plainSite });
            Check(!File.Exists(Path.Combine(plainSite, "index-terms.html")) && !File.ReadAllText(Path.Combine(plainSite, "plain.html")).Contains("index-terms.html"),
                "сайт: без терминов страницы и ссылки нет");

            // --- единый файл: указатель по-прежнему в конце, отсылки те же
            var single = File.ReadAllText(new HtmlPublisher(project).Publish(map,
                new PublishOptions { OutputDirectory = Path.Combine(root, "single"), SingleFile = true, Language = "ru" }).EntryFile);
            Check(single.Contains("<em>см.</em> Яблоко") && single.Contains("Прологовый"), "единый файл: отсылки и термины пролога в указателе");

            // --- DOCX
            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "предметный указатель");
            string[] codes;
            string[] paragraphs;
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                var body = doc.MainDocumentPart!.Document.Body!;
                codes = body.Descendants<FieldCode>().Select(c => c.Text.Trim()).ToArray();
                paragraphs = body.Descendants<Paragraph>().Select(p => p.InnerText).ToArray();
            }

            Check(codes.Contains("XE \"Яблоко:сорта\"") && codes.Contains("XE \"Яблоко\"") && codes.Contains("XE \"Прологовый\""),
                "DOCX: поля XE на термины, подтермины и пролог: " + string.Join(" | ", codes));
            Check(codes.Contains("XE \"Груша\" \\t \"см. Яблоко\"") && codes.Contains("XE \"Ёж\" \\t \"см. также Груша\""),
                "DOCX: отсылки — XE с переключателем \\t");
            Check(codes.Count(c => c.StartsWith("INDEX")) == 1, "DOCX: ровно одно поле INDEX");
            var indexAt = Array.LastIndexOf(paragraphs, "Указатель");
            Check(indexAt > 0 && paragraphs.Skip(indexAt).Any(p => p.Contains("Груша, см. Яблоко")), "DOCX: заголовок «Указатель» и заготовка поля с отсылкой");
            Check(paragraphs.Skip(indexAt).ToList().FindIndex(p => p.StartsWith("Арбуз")) > paragraphs.Skip(indexAt).ToList().FindIndex(p => p.StartsWith("Яблоко")),
                "DOCX: sort-as учтён в заготовке указателя");

            var plainDocx = Path.Combine(root, "plain.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "n.ditamap"), new PublishOptions { Language = "ru" }, plainDocx);
            CheckValidDocx(plainDocx, "без терминов");
            using (var doc = WordprocessingDocument.Open(plainDocx, false))
            {
                Check(!doc.MainDocumentPart!.Document.Body!.Descendants<FieldCode>().Any(c => c.Text.Contains("INDEX") || c.Text.Contains("XE")),
                    "DOCX: без терминов нет ни XE, ни INDEX");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // временная папка удалится системой
            }
        }
    }
}
