using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Оформление текста классами size-/color-/align-: должно действовать на любой текстовый элемент,
// а не только на абзацы и заголовки (замечание В2, docs/REVIEW_PLAN_2.md). Пока раздел пропущен в
// CoreTests — он фиксирует ожидаемое поведение и включается вместе с исправлением (этап 3 плана).
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
        });
    }
}
