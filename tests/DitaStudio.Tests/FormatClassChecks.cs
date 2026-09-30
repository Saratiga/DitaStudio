using DitaStudio.Core.Model;
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

    // Г17: свой размер шрифта — класс size-13_5 (дробные через «_»), в HTML — стилем, в DOCX — классом на лету.
    internal static void CustomFontSizeTests()
    {
        Section("Свой размер шрифта: size-13_5");

        Check(TextFormatting.SizeToken(12) == "size-12" && TextFormatting.SizeToken(13.5) == "size-13_5" && TextFormatting.SizeToken(13.54) == "size-13_5",
            "токен: целое — size-12, дробное — size-13_5, округление до десятых");
        Check(TextFormatting.SizeToken(3) == "size-4" && TextFormatting.SizeToken(500) == "size-200", "границы 4–200 пт");
        Check(TextFormatting.ParseSizeToken("size-13_5") == 13.5 && TextFormatting.ParseSizeToken("size-16") == 16 && TextFormatting.ParseSizeToken("size-x") is null &&
              TextFormatting.ParseSizeToken("size-300") is null && TextFormatting.ParseSizeToken("color-red") is null, "разбор токена");
        Check(!TextFormatting.IsCustomSize("size-12") && TextFormatting.IsCustomSize("size-13_5") && TextFormatting.IsCustomSize("size-15"), "«свой» — не из списка стандартных");
        var paragraph = DitaDocument.Parse("<p outputclass=\"size-13_5\">x</p>").Root;
        Check(TextFormatting.SizeOf(paragraph) == 13.5, "SizeOf читает дробный размер");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = """
                <topic id="t"><title>T</title><body>
                <p outputclass="size-13_5">СВОЙ</p><p outputclass="size-18">СТАНДАРТ</p>
                <p>Фраза <ph outputclass="size-9_5 color-red">МЕЛКО</ph></p>
                </body></topic>
                """,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            var html = File.ReadAllText(new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile);
            Check(html.Contains("class=\"size-13_5\" style=\"font-size: 13.5pt\">СВОЙ"), "HTML: свой размер — стиль на элементе");
            Check(html.Contains("class=\"size-18\">СТАНДАРТ") && !html.Contains("size-18\" style"), "HTML: стандартный размер — только класс со встроенным правилом");
            Check(html.Contains("font-size: 9.5pt\">МЕЛКО"), "HTML: свой размер у фразы");

            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "свой размер шрифта");
            using var doc = WordprocessingDocument.Open(docx, false);
            var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(st => st.StyleId!.Value!);
            var own = doc.MainDocumentPart.Document.Body!.Descendants<Paragraph>().First(p => p.InnerText == "СВОЙ").ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            Check(own is not null && styles[own].StyleRunProperties?.FontSize?.Val?.Value == "27", "DOCX: свой размер 13,5 пт = 27 half-points");
            var run = doc.MainDocumentPart.Document.Body.Descendants<Run>().First(r => r.InnerText == "МЕЛКО");
            var runStyle = run.RunProperties?.RunStyle?.Val?.Value;
            Check(runStyle is not null && styles[runStyle].StyleRunProperties?.FontSize?.Val?.Value == "19" && styles[runStyle].StyleRunProperties?.Color?.Val?.Value == "C00000",
                "DOCX: у фразы свой размер и цвет");
        });
    }
}
