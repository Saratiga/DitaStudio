using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// SVG в DOCX: с растеризатором — PNG нужного размера, без него или при сбое — прежняя плашка с предупреждением.
internal static partial class CoreChecks
{
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAUCAIAAABwJOjsAAAAJ0lEQVR4nGM8oaHBMBCAaUBsHbV41OJRi0ctHrV41OJRi0ctHhAAABx5AUDfR9jWAAAAAElFTkSuQmCC";

    private sealed class FakeRasterizer : IImageRasterizer
    {
        public List<(string Path, int Width, int Height)> Calls { get; } = new();

        public bool Fail { get; set; }

        public byte[]? RasterizeSvg(string svgPath, int pixelWidth, int pixelHeight)
        {
            Calls.Add((Path.GetFileName(svgPath), pixelWidth, pixelHeight));
            return Fail ? null : Convert.FromBase64String(TinyPng);
        }
    }

    internal static void SvgDocxTests()
    {
        Section("SVG в DOCX через растеризатор");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "viewbox.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 100\"><rect width=\"200\" height=\"100\" fill=\"red\"/></svg>");
            File.WriteAllText(Path.Combine(root, "sized.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"2in\" height=\"1in\"><rect width=\"10\" height=\"10\"/></svg>");
            File.WriteAllText(Path.Combine(root, "t.dita"), """
                <topic id="t"><title>Схемы</title><body>
                <p><image href="viewbox.svg"><alt>Первая</alt></image></p>
                <p><image href="sized.svg" width="400px"/></p>
                </body></topic>
                """);
            File.WriteAllText(Path.Combine(root, "m.ditamap"), "<map><title>Схемы</title><topicref href=\"t.dita\"/></map>");
            var project = new DitaProject(root);
            project.Scan();
            var map = Path.Combine(root, "m.ditamap");

            // --- с растеризатором
            var rasterizer = new FakeRasterizer();
            var docx = Path.Combine(root, "with.docx");
            var result = new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru", ImageRasterizer = rasterizer }, docx);
            CheckValidDocx(docx, "SVG растеризован");
            Check(rasterizer.Calls.Count == 2, "растеризатор вызван на каждый SVG");
            Check(rasterizer.Calls[0] == ("viewbox.svg", 400, 200), $"плотность вдвое больше экранной: {rasterizer.Calls[0]}");
            Check(rasterizer.Calls[1] == ("sized.svg", 800, 400), $"dita-width 400px и пропорции SVG 2:1 — 800×400: {rasterizer.Calls[1]}");
            Check(!result.Warnings.Any(w => w.Contains("SVG") || w.Contains(".svg")), "предупреждений про SVG нет");
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                var main = doc.MainDocumentPart!;
                Check(main.ImageParts.Count() == 2 && main.ImageParts.All(i => i.ContentType == "image/png"), "в пакете две PNG-картинки");
                var extents = main.Document.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Select(e => (e.Cx!.Value, e.Cy!.Value)).ToList();
                Check(extents.SequenceEqual(new[] { (200L * 9525, 100L * 9525), (400L * 9525, 200L * 9525) }), "размеры на странице: " + string.Join(", ", extents));
                Check(!main.Document.Body.InnerText.Contains("изображение:"), "плашки с именем файла нет");
                Check(main.Document.Body.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().First().Description?.Value == "Первая", "alt попал в описание картинки");
            }

            // --- без растеризатора: прежнее поведение
            var plain = Path.Combine(root, "plain.docx");
            var plainResult = new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, plain);
            CheckValidDocx(plain, "SVG без растеризатора");
            Check(plainResult.Warnings.Count(w => w.Contains(".svg")) == 2, "без растеризатора — два предупреждения про SVG");
            using (var doc = WordprocessingDocument.Open(plain, false))
            {
                Check(!doc.MainDocumentPart!.ImageParts.Any() && doc.MainDocumentPart.Document.Body!.InnerText.Contains("[изображение: viewbox.svg — Первая]"),
                    "без растеризатора — плашка с именем файла и подписью");
            }

            // --- растеризатор не справился: плашка и предупреждение
            var failing = Path.Combine(root, "fail.docx");
            var failed = new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru", ImageRasterizer = new FakeRasterizer { Fail = true } }, failing);
            CheckValidDocx(failing, "растеризатор вернул null");
            Check(failed.Warnings.Count(w => w.Contains(".svg")) == 2, "сбой растеризатора — предупреждение на каждый SVG");
            using (var doc = WordprocessingDocument.Open(failing, false))
            {
                Check(doc.MainDocumentPart!.Document.Body!.InnerText.Contains("[изображение: sized.svg]"), "сбой — плашка с именем файла");
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
