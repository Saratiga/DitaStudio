using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Шаблон DOCX: стили, шрифты и значения по умолчанию берутся из шаблона, правила CSS проекта накладываются поверх, остальное достраивает публикация.
internal static partial class CoreChecks
{
    private static void MakeTemplate(string path)
    {
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body(new Paragraph(new Run(new Text("Текст шаблона, которого в публикации быть не должно")))));
        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new DocDefaults(new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = "Arial", HighAnsi = "Arial" }, new FontSize { Val = "24" }))),
            new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
            new Style(
                new StyleName { Val = "heading 1" }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new PrimaryStyle(),
                new StyleParagraphProperties(
                    new KeepNext(),
                    new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 7 }),
                    new SpacingBetweenLines { Before = "480", After = "120" }),
                new StyleRunProperties(
                    new RunFonts { Ascii = "Georgia", HighAnsi = "Georgia" }, new Bold(), new Color { Val = "123456" }, new FontSize { Val = "40" }))
            { Type = StyleValues.Paragraph, StyleId = "Heading1" },
            new Style(new StyleName { Val = "Шаблонный" }, new BasedOn { Val = "Normal" },
                new StyleRunProperties(new Italic())) { Type = StyleValues.Paragraph, StyleId = "TemplateOnly" });
        main.Document.Save();
    }

    private static Dictionary<string, Style> StylesOf(string docx)
    {
        using var doc = WordprocessingDocument.Open(docx, false);
        return doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(s => s.StyleId!.Value!, s => (Style)s.CloneNode(true));
    }

    internal static void DocxTemplateTests()
    {
        Section("Шаблон DOCX: стили шаблона и CSS поверх");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = "<topic id=\"t\"><title>Заголовок</title><body><p>Текст.</p><note><p>Примечание</p></note></body></topic>",
            ["m.ditamap"] = "<map><title>Книга</title><topicref href=\"t.dita\"/></map>",
            ["custom.css"] = "h1 { color: #FF0000; }\n.note { font-style: italic; }"
        }, (root, project) =>
        {
            MakeTemplate(Path.Combine(root, "template.docx"));
            project.SetCustomCssPath("custom.css");
            var map = Path.Combine(root, "m.ditamap");

            // --- без шаблона
            project.SetDocxLayout(new DocxLayout { TitlePage = false, TableOfContents = false });
            var plain = Path.Combine(root, "plain.docx");
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, plain);
            var plainStyles = StylesOf(plain);
            Check(plainStyles["Heading1"].StyleRunProperties?.RunFonts?.Ascii?.Value != "Georgia", "без шаблона шрифт заголовка — из каталога");

            // --- с шаблоном
            project.SetDocxLayout(new DocxLayout { TitlePage = false, TableOfContents = false, TemplatePath = "template.docx" });
            var docx = Path.Combine(root, "with.docx");
            var result = new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "шаблон DOCX");
            Check(result.Warnings.Count == 0, "предупреждений нет: " + string.Join(" | ", result.Warnings));
            var styles = StylesOf(docx);
            var heading = styles["Heading1"];
            Check(heading.StyleRunProperties?.RunFonts?.Ascii?.Value == "Georgia" && heading.StyleRunProperties.Bold is not null && heading.StyleRunProperties.FontSize?.Val?.Value == "40",
                "Heading1: шрифт, жирность и размер остались из шаблона");
            Check(heading.StyleRunProperties?.Color?.Val?.Value == "FF0000", "h1 { color } из CSS главнее цвета шаблона: " + heading.StyleRunProperties?.Color?.Val?.Value);
            Check(heading.StyleParagraphProperties?.SpacingBetweenLines?.Before?.Value == "480" && heading.StyleParagraphProperties.KeepNext is not null,
                "интервалы и «не отрывать от следующего» остались из шаблона");
            Check(heading.StyleParagraphProperties?.NumberingProperties is null, "нумерация шаблона (numId 7) снята — списков шаблона в документе нет");
            Check(styles.ContainsKey("TemplateOnly"), "стиль, которого нет в каталоге, но есть в шаблоне, сохранён");
            Check(styles.ContainsKey("Note") && styles["Note"].StyleRunProperties?.Italic is not null, "стиль каталога, которого нет в шаблоне, добавлен и оформлен CSS");
            Check(styles.ContainsKey("TOC1") && styles.ContainsKey("TableText"), "стили оглавления и таблиц достроены");
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                var defaults = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.DocDefaults!;
                Check(defaults.RunPropertiesDefault?.RunPropertiesBaseStyle?.RunFonts?.Ascii?.Value == "Arial", "значения по умолчанию (шрифт Arial) — из шаблона");
                Check(!doc.MainDocumentPart.Document.Body!.InnerText.Contains("которого в публикации быть не должно"), "текст шаблона в публикацию не попадает");
                Check(doc.MainDocumentPart.Document.Body.Descendants<Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading1"), "заголовок идёт стилем Heading1");
            }

            // --- нумерация заголовков — своя, поверх шаблона
            project.SetDocxLayout(new DocxLayout { TitlePage = false, TableOfContents = false, TemplatePath = "template.docx", NumberHeadings = true, NumberingDepth = 2, PageBreakBeforeTopLevel = true });
            var numbered = Path.Combine(root, "numbered.docx");
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, numbered);
            CheckValidDocx(numbered, "шаблон DOCX с нумерацией заголовков");
            var numberedHeading = StylesOf(numbered)["Heading1"];
            Check(numberedHeading.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value is { } id && id != 7, "нумерация заголовков — своя, не из шаблона");
            Check(numberedHeading.StyleParagraphProperties?.PageBreakBefore is not null && numberedHeading.StyleRunProperties?.RunFonts?.Ascii?.Value == "Georgia",
                "«каждая глава с новой страницы» применена, шрифт шаблона цел");

            // --- шаблон-пустышка и отсутствующий файл: предупреждение и сборка без шаблона
            File.WriteAllText(Path.Combine(root, "broken.docx"), "это не документ Word");
            project.SetDocxLayout(new DocxLayout { TitlePage = false, TableOfContents = false, TemplatePath = "broken.docx" });
            var brokenOut = Path.Combine(root, "broken-out.docx");
            var broken = new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, brokenOut);
            CheckValidDocx(brokenOut, "повреждённый шаблон");
            Check(broken.Warnings.Any(w => w.Contains("broken.docx")), "повреждённый шаблон — предупреждение с именем файла");
            Check(StylesOf(brokenOut)["Heading1"].StyleRunProperties?.RunFonts?.Ascii?.Value != "Georgia", "после предупреждения — стили каталога");

            project.SetDocxLayout(new DocxLayout { TitlePage = false, TableOfContents = false, TemplatePath = "нет-такого.dotx" });
            var missingOut = Path.Combine(root, "missing-out.docx");
            var missing = new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, missingOut);
            CheckValidDocx(missingOut, "нет файла шаблона");
            Check(missing.Warnings.Any(w => w.Contains("нет-такого.dotx")), "нет файла шаблона — предупреждение");

            // --- путь шаблона сохраняется вместе с остальными настройками
            var restored = DocxLayout.FromJson(new DocxLayout { TemplatePath = "  шаблоны/a.dotx  " }.ToJson());
            Check(restored.TemplatePath == "шаблоны/a.dotx", "TemplatePath сохраняется и обрезается");
        });
    }
}
