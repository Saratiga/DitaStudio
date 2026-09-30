using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Г10: разрыв страницы перед топиком — свойство строки карты (outputclass page-break-before / page-break-none).
internal static partial class CoreChecks
{
    internal static void TopicPageBreakTests()
    {
        Section("Разрыв страницы перед топиком из карты");

        var row = DitaNode.Element("topicref");
        Check(TopicPageBreak.Of(row) is null, "по умолчанию — как в настройке (null)");
        row.SetAttribute("outputclass", "keep other");
        TopicPageBreak.Set(row, true);
        Check(TopicPageBreak.Of(row) == true && row.GetAttribute("outputclass") == "keep other page-break-before", "«с новой страницы» — класс добавлен, чужие сохранены");
        TopicPageBreak.Set(row, false);
        Check(TopicPageBreak.Of(row) == false && row.GetAttribute("outputclass") == "keep other page-break-none", "«не с новой» заменяет прежний выбор");
        TopicPageBreak.Set(row, null);
        Check(TopicPageBreak.Of(row) is null && row.GetAttribute("outputclass") == "keep other", "снятие возвращает как было");
        row.SetAttribute("outputclass", null);
        TopicPageBreak.Set(row, true);
        TopicPageBreak.Set(row, null);
        Check(!row.HasAttribute("outputclass"), "пустой outputclass не остаётся");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = "<topic id=\"a\"><title>Первый</title><body><p>А</p></body></topic>",
            ["b.dita"] = "<topic id=\"b\"><title>Второй</title><body><p>Б</p></body></topic>",
            ["c.dita"] = "<topic id=\"c\"><title>Третий</title><body><p>В</p></body></topic>",
            ["d.dita"] = "<topic id=\"d\"><title/><body><p>Г</p></body></topic>",
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"a.dita\"/><topicref href=\"b.dita\" outputclass=\"page-break-before\"/>" +
                            "<topicref href=\"c.dita\" outputclass=\"page-break-none\"/><topicref href=\"d.dita\" outputclass=\"page-break-before\"/></map>"
        }, (root, project) =>
        {
            var map = Path.Combine(root, "m.ditamap");
            bool? BreakOf(WordprocessingDocument doc, string text)
            {
                var paragraph = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().First(p => p.InnerText == text);
                return paragraph.ParagraphProperties?.PageBreakBefore is { } pb ? pb.Val is null || pb.Val.Value : null;
            }

            // Без общей настройки: разрыв только у помеченного.
            project.SetDocxLayout(new DocxLayout { TitlePage = false, TableOfContents = false });
            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "разрыв страницы у топика");
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                Check(BreakOf(doc, "Первый") is null && BreakOf(doc, "Второй") == true && BreakOf(doc, "Третий") != true,
                    "DOCX: разрыв перед «Второй» из карты; у остальных нет");
                var breaks = doc.MainDocumentPart!.Document.Body!.Descendants<Break>().Count(b => b.Type?.Value == BreakValues.Page);
                Check(breaks == 1, $"DOCX: у топика без заголовка разрыв — отдельным абзацем ({breaks})");
            }

            // «Каждый топик верхнего уровня — с новой страницы»: «не с новой» перекрывает настройку у одного топика.
            project.SetDocxLayout(new DocxLayout { TitlePage = false, TableOfContents = false, PageBreakBeforeTopLevel = true });
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                Check(BreakOf(doc, "Третий") == false, "DOCX: page-break-none перекрывает настройку (pageBreakBefore = 0 у абзаца)");
                Check(BreakOf(doc, "Второй") == true, "DOCX: page-break-before остаётся");
            }

            var html = File.ReadAllText(new HtmlPublisher(project).Publish(map,
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile);
            Check(html.Contains("class=\"topic-chunk chapter-heading page-break-before\"") && html.Contains("topic-chunk chapter-heading page-break-none"),
                "HTML: классы на блоках топиков");
            Check(html.Contains(".topic-chunk.page-break-before { break-before: page;"), "HTML: встроенное правило разрыва в CSS");
        });
    }
}
