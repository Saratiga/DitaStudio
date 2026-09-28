using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Группирующие элементы div, bodydiv, sectiondiv, conbodydiv, refbodydiv: модели содержимого по
// DTD DITA 1.3 (commonElements.mod, topic.mod, concept.mod, reference.mod) и смешанный текст в DOCX.
internal static partial class CoreChecks
{
    private const string DivTopic = """
        <topic id="t"><title>Div</title><body>
        <div>Текст прямо в div и <b>жирная фраза</b> и хвост.<p>Абзац внутри div.</p><div>Вложенный div.</div></div>
        <bodydiv>Текст в bodydiv.<section><title>Раздел в bodydiv</title><p>Абзац раздела.</p></section><bodydiv><p>Вложенный bodydiv.</p></bodydiv></bodydiv>
        <section><title>Раздел</title>Текст прямо в разделе.<sectiondiv>Текст в sectiondiv.<sectiondiv><p>Вложенный sectiondiv.</p></sectiondiv></sectiondiv></section>
        </body></topic>
        """;

    internal static void DivContentTests()
    {
        Section("div и родственные группы: модели DITA 1.3, смешанный текст в DOCX");

        var validator = new DitaValidator { CheckStyleRules = false };
        var errors = validator.Validate(DitaDocument.Parse(DivTopic))
            .Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message).ToList();
        Check(errors.Count == 0,
            "div/bodydiv/sectiondiv: текст, фразы, вложенные div/bodydiv/sectiondiv и section в bodydiv допустимы" +
            (errors.Count > 0 ? ": " + string.Join("; ", errors) : string.Empty));

        var concept = DitaDocument.Parse(
            "<concept id=\"c\"><title>C</title><conbody><p>Вступление.</p>" +
            "<conbodydiv><section><title>Р</title><p>Т.</p></section><example><p>П.</p></example></conbodydiv></conbody></concept>");
        Check(!validator.Validate(concept).Any(i => i.Severity == IssueSeverity.Error),
            "conbodydiv: допустим в conbody и содержит section и example");

        var badConbodydiv = DitaDocument.Parse(
            "<concept id=\"c\"><title>C</title><conbody><conbodydiv><p>Абзац.</p></conbodydiv></conbody></concept>");
        Check(validator.Validate(badConbodydiv).Any(i => i.Severity == IssueSeverity.Error),
            "conbodydiv: абзац напрямую недопустим — только section и example (DTD concept.mod)");

        var reference = DitaDocument.Parse(
            "<reference id=\"r\"><title>R</title><refbody><refbodydiv><refbodydiv><section><p>Т.</p></section></refbodydiv></refbodydiv></refbody></reference>");
        Check(!validator.Validate(reference).Any(i => i.Severity == IssueSeverity.Error),
            "refbodydiv: вложенный refbodydiv допустим");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "t.dita"), DivTopic);
            File.WriteAllText(Path.Combine(root, "m.ditamap"), "<map><title>Div</title><topicref href=\"t.dita\"/></map>");
            var project = new DitaProject(root);
            project.Scan();

            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "div и смешанный текст");

            List<string> paragraphs;
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                paragraphs = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>()
                    .Select(p => p.InnerText).Where(t => t.Length > 0).ToList();
            }

            Check(paragraphs.Contains("Текст прямо в div и жирная фраза и хвост."),
                "docx: текст прямо в div вместе с фразой — один абзац (раньше текст пропадал, фраза становилась отдельным абзацем)");
            Check(!paragraphs.Contains("жирная фраза"), "docx: фраза внутри div не выносится в отдельный абзац");
            Check(paragraphs.Contains("Вложенный div.") && paragraphs.Contains("Текст в bodydiv.") && paragraphs.Contains("Текст в sectiondiv."),
                "docx: текст вложенного div, bodydiv и sectiondiv не теряется");
            Check(paragraphs.Contains("Текст прямо в разделе."), "docx: текст прямо в section не теряется");
            Check(paragraphs.IndexOf("Текст прямо в div и жирная фраза и хвост.") < paragraphs.IndexOf("Абзац внутри div.") &&
                  paragraphs.IndexOf("Абзац внутри div.") < paragraphs.IndexOf("Вложенный div."),
                "docx: порядок текста и блоков внутри div сохраняется");
            Check(paragraphs.Count(p => p.Trim().Length == 0) == 0, "docx: пробелы между блоками не дают пустых абзацев");
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
}
