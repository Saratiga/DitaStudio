using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Оформление текста классами size-/color-/align-: действует на любой текстовый элемент, а не только на
// абзацы и заголовки (замечание В2, docs/REVIEW_PLAN_2.md), в DOCX и HTML; свой CSS перекрывает встроенный.
internal static partial class CoreChecks
{
    internal static void FormatClassOnAnyElementTests()
    {
        Section("Классы оформления текста — у любого элемента, в DOCX");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = """
                <topic id="t"><title>T</title><body>
                <ul><li outputclass="size-18 color-green">ПУНКТ</li></ul>
                <dl><dlentry><dt outputclass="size-18">ТЕРМИН</dt><dd outputclass="color-red">ОПРЕДЕЛЕНИЕ</dd></dlentry></dl>
                <table><title>Т</title><tgroup cols="1"><tbody><row><entry outputclass="size-18 color-red align-center">ЯЧЕЙКА</entry></row></tbody></tgroup></table>
                <ol><li outputclass="align-right">СПРАВА</li></ol>
                </body></topic>
                """,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            using var doc = WordprocessingDocument.Open(docx, false);
            var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(s => s.StyleId!.Value!);
            Style? StyleOf(string text)
            {
                var paragraph = doc.MainDocumentPart.Document.Body!.Descendants<Paragraph>().First(p => p.InnerText.Contains(text));
                var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                return id is not null && styles.TryGetValue(id, out var style) ? style : null;
            }

            Check(StyleOf("ПУНКТ")?.StyleRunProperties?.FontSize?.Val?.Value == "36" && StyleOf("ПУНКТ")?.StyleRunProperties?.Color?.Val?.Value == "00873C",
                "DOCX: size-18 и color-green на li — 18 pt и зелёный");
            Check(StyleOf("ТЕРМИН")?.StyleRunProperties?.FontSize?.Val?.Value == "36", "DOCX: size-18 на dt");
            Check(StyleOf("ОПРЕДЕЛЕНИЕ")?.StyleRunProperties?.Color?.Val?.Value == "C00000", "DOCX: color-red на dd");
            Check(StyleOf("ЯЧЕЙКА")?.StyleRunProperties?.FontSize?.Val?.Value == "36", "DOCX: size-18 на ячейке таблицы");
            Check(StyleOf("ЯЧЕЙКА")?.StyleRunProperties?.Color?.Val?.Value == "C00000", "DOCX: color-red на ячейке таблицы");
            Check(StyleOf("СПРАВА")?.StyleParagraphProperties?.Justification?.Val?.Value == JustificationValues.Right, "DOCX: align-right на li");

            // HTML: класс остаётся на своём элементе, встроенный CSS не привязан к тегу.
            var htmlEntry = new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile;
            var html = File.ReadAllText(htmlEntry);
            Check(html.Contains("<li class=\"size-18 color-green\">ПУНКТ") || System.Text.RegularExpressions.Regex.IsMatch(html, "<li[^>]*size-18[^>]*>[^<]*ПУНКТ"),
                "HTML: класс size-18 остаётся на li");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<dt[^>]*size-18[^>]*>[^<]*ТЕРМИН") &&
                  System.Text.RegularExpressions.Regex.IsMatch(html, "<dd[^>]*color-red[^>]*>[^<]*ОПРЕДЕЛЕНИЕ"),
                "HTML: классы на dt и dd");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<td[^>]*size-18[^>]*>[^<]*ЯЧЕЙКА"), "HTML: класс на ячейке");
            Check(html.Contains(".size-18 { font-size: 18pt; }") && !html.Contains("p.size-18") && !html.Contains("p .size-18"),
                "HTML: встроенное правило .size-18 не привязано к тегу p");

            // Порядок слоёв: встроенный CSS классов → CSS проекта. Своё правило перекрывает встроенное.
            File.WriteAllText(Path.Combine(root, "custom.css"), ".size-18 { font-size: 20pt; }");
            project.SetCustomCssPath("custom.css");
            var custom = File.ReadAllText(new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out2"), SingleFile = true }).EntryFile);
            Check(custom.IndexOf(".size-18 { font-size: 18pt; }", StringComparison.Ordinal) is var builtin and >= 0 &&
                  custom.IndexOf(".size-18 { font-size: 20pt; }", StringComparison.Ordinal) > builtin,
                "HTML: правило проекта .size-18 идёт после встроенного и перекрывает его");
            var docx2 = Path.Combine(root, "d2.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx2);
            using var doc2 = WordprocessingDocument.Open(docx2, false);
            var styles2 = doc2.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(s => s.StyleId!.Value!);
            var pr = doc2.MainDocumentPart.Document.Body!.Descendants<Paragraph>().First(p => p.InnerText.Contains("ПУНКТ")).ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            Check(pr is not null && styles2[pr].StyleRunProperties?.FontSize?.Val?.Value == "40", "DOCX: правило проекта .size-18 (20 pt) перекрывает встроенное");
        });
    }
}
