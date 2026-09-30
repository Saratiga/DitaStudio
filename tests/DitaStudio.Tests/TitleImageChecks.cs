using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DrawingWp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace DitaStudio.Tests;

// Г14: картинка на титульной странице (над названием) и отключение титула.
internal static partial class CoreChecks
{
    internal static void TitleImageTests()
    {
        Section("Картинка на титульной странице");

        var restored = DocxLayout.FromJson(new DocxLayout { TitleImage = "images/logo.png", TitleImageHeightMm = 33, TitleImageAlignment = DocxHeaderAlignment.Right }.ToJson());
        Check(restored.TitleImage == "images/logo.png" && restored.TitleImageHeightMm == 33 && restored.TitleImageAlignment == DocxHeaderAlignment.Right, "настройки картинки титула сохраняются в JSON");
        var clamp = new DocxLayout { TitleImageHeightMm = 1000 };
        clamp.Normalize();
        Check(clamp.TitleImageHeightMm == 250, "высота картинки титула ограничена 250 мм");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = "<topic id=\"t\"><title>Т</title><body><p>Текст.</p></body></topic>",
            ["m.ditamap"] = "<map><title>Моя книга</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            File.WriteAllBytes(Path.Combine(root, "logo.png"), MakePng(400, 200));
            var map = Path.Combine(root, "m.ditamap");
            var docx = Path.Combine(root, "d.docx");

            project.SetDocxLayout(new DocxLayout { TitleImage = "logo.png", TitleImageHeightMm = 30, TableOfContents = false });
            var result = new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "картинка на титульной странице");
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                var body = doc.MainDocumentPart!.Document.Body!;
                var first = body.Elements<Paragraph>().First();
                var extent = first.Descendants<DrawingWp.Extent>().SingleOrDefault();
                Check(extent is not null && Math.Abs(extent.Cy!.Value - 30 * 36000) < 400 && Math.Abs(extent.Cx!.Value - 60 * 36000) < 800,
                    $"DOCX: первая на титуле — картинка высотой 30 мм, пропорции 2:1 ({extent?.Cx?.Value}×{extent?.Cy?.Value})");
                Check(first.ParagraphProperties?.Justification?.Val?.Value == JustificationValues.Center, "DOCX: по центру по умолчанию");
                Check(body.Elements<Paragraph>().ElementAt(1).InnerText == "Моя книга", "DOCX: название — сразу под картинкой");
                Check(doc.MainDocumentPart.ImageParts.Count() == 1, "DOCX: картинка встроена в документ");
            }

            var html = File.ReadAllText(new HtmlPublisher(project).Publish(map,
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile);
            Check(html.Contains("class=\"title-image\"") && html.Contains("height:30mm") && html.IndexOf("title-image", StringComparison.Ordinal) < html.IndexOf("book-title", StringComparison.Ordinal),
                "HTML: картинка титула стоит перед названием издания");

            // Титул отключён — картинки нет ни в DOCX, ни в HTML.
            project.SetDocxLayout(new DocxLayout { TitleImage = "logo.png", TitlePage = false, TableOfContents = false });
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                Check(!doc.MainDocumentPart!.Document.Body!.Descendants<DrawingWp.Extent>().Any(), "DOCX: без титульной страницы картинки нет");
            }

            html = File.ReadAllText(new HtmlPublisher(project).Publish(map, new PublishOptions { OutputDirectory = Path.Combine(root, "out2"), SingleFile = true }).EntryFile);
            Check(!html.Contains("class=\"title-image\""), "HTML: без титула картинки нет");

            // Картинки нет на диске — предупреждение, публикация идёт.
            project.SetDocxLayout(new DocxLayout { TitleImage = "absent.png", TableOfContents = false });
            result = new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            Check(result.Warnings.Any(w => w.Contains("absent.png")), "картинка титула не найдена — предупреждение");
        });
    }
}
