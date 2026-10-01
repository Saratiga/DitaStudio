using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Д9: блоки внутри ячейки таблицы (абзацы, списки, примечания) в DOCX — отдельными абзацами, а не сплошным текстом.
internal static partial class CoreChecks
{
    internal static void CellParagraphsDocxTests()
    {
        Section("Абзацы внутри ячейки таблицы в DOCX");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = """
                <topic id="t"><title>Т</title><body>
                <table><tgroup cols="2"><tbody>
                <row><entry><p>АБЗАЦ_А1</p><p>АБЗАЦ_А2</p></entry><entry>ПРОСТО_ТЕКСТ</entry></row>
                <row><entry><p>ВВОД</p><ul><li>ПУНКТ_1</li><li>ПУНКТ_2</li></ul></entry><entry>Б2</entry></row>
                <row><entry>ТЕКСТ_ПЕРЕД<p>АБЗАЦ_ПОСЛЕ</p></entry><entry><note>ЗАМЕТКА</note></entry></row>
                </tbody></tgroup></table>
                <simpletable><strow><stentry><p>СТ_1</p><p>СТ_2</p></stentry><stentry>СТ_ТЕКСТ</stentry></strow></simpletable>
                </body></topic>
                """,
            ["m.ditamap"] = "<map><title>М</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "абзацы в ячейках");
            using var doc = WordprocessingDocument.Open(docx, false);
            var tables = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();
            List<string> Paragraphs(TableCell cell) => cell.Elements<Paragraph>().Select(p => p.InnerText).Where(t => t.Length > 0).ToList();
            var cells = tables[0].Descendants<TableCell>().ToList();

            Check(Paragraphs(cells[0]).SequenceEqual(new[] { "АБЗАЦ_А1", "АБЗАЦ_А2" }), $"два <p> в ячейке — два абзаца ({string.Join(" | ", Paragraphs(cells[0]))})");
            Check(Paragraphs(cells[1]).SequenceEqual(new[] { "ПРОСТО_ТЕКСТ" }), "ячейка с одним текстом — один абзац, как раньше");
            Check(Paragraphs(cells[2]).Count == 3 && Paragraphs(cells[2])[0] == "ВВОД" && Paragraphs(cells[2])[1].Contains("ПУНКТ_1") && Paragraphs(cells[2])[2].Contains("ПУНКТ_2"),
                $"абзац и список в ячейке — абзац и два пункта отдельными абзацами ({string.Join(" | ", Paragraphs(cells[2]))})");
            Check(Paragraphs(cells[4]).SequenceEqual(new[] { "ТЕКСТ_ПЕРЕД", "АБЗАЦ_ПОСЛЕ" }), $"текст и <p> в одной ячейке — два абзаца, текст первым ({string.Join(" | ", Paragraphs(cells[4]))})");
            Check(Paragraphs(cells[5]).Any(t => t.Contains("ЗАМЕТКА")), "примечание в ячейке сохраняется");
            var simple = tables[1].Descendants<TableCell>().ToList();
            Check(Paragraphs(simple[0]).SequenceEqual(new[] { "СТ_1", "СТ_2" }), $"в ячейке простой таблицы — два абзаца ({string.Join(" | ", Paragraphs(simple[0]))})");
        });
    }
}
