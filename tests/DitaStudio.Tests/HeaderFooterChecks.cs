using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Drawing = DocumentFormat.OpenXml.Drawing;
using DrawingWp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace DitaStudio.Tests;

// Колонтитулы: картинка подгоняется под область (В9а) и колонтитулы из CSS — @page { @top-left { … } } (В9б).
internal static partial class CoreChecks
{
    internal static void PageMarginBoxTests()
    {
        Section("Колонтитулы из CSS: разбор полей страницы");

        var boxes = PageMarginBoxes.Parse("""
            /* комментарий */
            @page {
              size: A4;
              margin: 25mm 20mm;
              @top-left { content: "Руководство" url("logo.png"); font-size: 9pt; color: #555; border-bottom: 0.5pt solid #999; margin-top: 8mm }
              @top-right { content: string(title); font-family: "Segoe UI", sans-serif; font-weight: bold; font-style: italic }
              @bottom-center { content: "Стр. " counter(page) " из " counter(pages); font: 8pt Arial }
              @bottom-left { content: none }
              @top-center { content: "x"; text-shadow: 1px 1px }
            }
            @media print { @page { @bottom-right { content: "©" } } }
            """);
        Check(boxes.Boxes.Count == 5, $"поля: top-left, top-center, top-right, bottom-center, bottom-right (без content: none) — {boxes.Boxes.Count}");
        var left = boxes.Boxes.First(b => b.Position == MarginBoxPosition.TopLeft);
        Check(left.Content.SequenceEqual(new MarginContent[] { new MarginContent.Text("Руководство"), new MarginContent.Image("logo.png") }),
            "content: строка и url(картинка) в порядке записи");
        Check(left.FontSizePt == 9 && left.Color == "555555", "font-size и color поля");
        Check(left.Rule is { WidthPt: 0.5, Color: "999999" }, "border-bottom у верхнего поля — линия под колонтитулом");
        Check(left.EdgeDistanceMm is { } edge && Math.Abs(edge - 8) < 0.01, "margin-top верхнего поля — отступ от края листа");
        var right = boxes.Boxes.First(b => b.Position == MarginBoxPosition.TopRight);
        Check(right.Content.Single() is MarginContent.Title && right.FontFamily == "Segoe UI" && right.Bold && right.Italic,
            "string(title), font-family, bold, italic");
        var pages = boxes.Boxes.First(b => b.Position == MarginBoxPosition.BottomCenter);
        Check(pages.Content.Select(c => c.GetType().Name).SequenceEqual(new[] { "Text", "PageNumber", "Text", "PageCount" }) && pages.FontSizePt == 8 && pages.FontFamily == "Arial",
            "counter(page), counter(pages) и сокращение font");
        Check(boxes.Boxes.Any(b => b.Position == MarginBoxPosition.BottomRight), "поле внутри @media print разобрано");
        Check(boxes.Warnings.Any(w => w.Contains("text-shadow")), "неподдерживаемое свойство — в предупреждениях");
        Check(boxes.HasTop && boxes.HasBottom, "HasTop / HasBottom");
        Check(PageMarginBoxes.Parse("p { color: red }") == PageMarginBoxes.Empty && PageMarginBoxes.Parse(null) == PageMarginBoxes.Empty, "без полей — пусто");

        // Свойства страницы (size, margin) не портятся вложенными блоками полей.
        var sheet = DitaStudio.Docx.Styling.DocxStyleSheet.FromCss("@page { size: A5; margin: 30mm; @top-left { content: \"x\" } }");
        Check(Math.Abs(sheet.Page.TopPt - 30 * 72 / 25.4) < 0.1 && sheet.Warnings.Count == 0,
            $"@page с вложенными полями: margin разобран, лишних предупреждений нет ({string.Join("; ", sheet.Warnings)})");
        Check(sheet.MarginBoxes.HasTop, "DocxStyleSheet.MarginBoxes заполнен");
    }

    internal static void HeaderFooterCssDocxTests()
    {
        Section("Колонтитулы DOCX: из CSS и подгонка картинок под область");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = "<topic id=\"t\"><title>Т</title><body><p>Текст.</p></body></topic>",
            ["m.ditamap"] = "<map><title>Моя книга</title><topicref href=\"t.dita\"/></map>",
            ["custom.css"] = """
                @page {
                  margin: 20mm;
                  @top-left { content: "Слева"; font-size: 9pt; color: #C00000; border-bottom: 1pt solid #888 }
                  @top-right { content: string(title) }
                  @bottom-center { content: "Стр. " counter(page) " из " counter(pages); font-weight: bold }
                }
                """
        }, (root, project) =>
        {
            project.SetCustomCssPath("custom.css");
            project.SetDocxLayout(new DocxLayout { HeaderText = "Из диалога", FooterText = "Подвал из диалога", NoHeaderOnFirstPage = false });
            var docx = Path.Combine(root, "d.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "колонтитулы из CSS");
            using var doc = WordprocessingDocument.Open(docx, false);
            var header = doc.MainDocumentPart!.HeaderParts.Single().Header!;
            var footer = doc.MainDocumentPart.FooterParts.Single().Footer!;
            var headerText = header.InnerText;
            Check(headerText.Contains("Слева") && headerText.Contains("Моя книга") && !headerText.Contains("Из диалога"),
                $"верхний колонтитул из CSS перекрывает диалог, string(title) — название издания ({headerText})");
            var run = header.Descendants<Run>().First(r => r.InnerText == "Слева");
            Check(run.RunProperties?.FontSize?.Val?.Value == "18" && run.RunProperties.Color?.Val?.Value == "C00000", "размер и цвет поля — в свойствах прогона");
            Check(header.Descendants<BottomBorder>().Any(b => b.Color?.Value == "888888"), "border-bottom верхнего поля — линия под колонтитулом");
            var fields = footer.Descendants<SimpleField>().Select(f => f.Instruction?.Value?.Trim()).ToList();
            Check(fields.SequenceEqual(new[] { "PAGE", "NUMPAGES" }) && footer.InnerText.Contains("Стр. ") && !footer.InnerText.Contains("из диалога"),
                $"counter(page), counter(pages) — поля Word PAGE и NUMPAGES ({string.Join(",", fields)})");
            Check(footer.Descendants<Bold>().Any(), "font-weight: bold поля");
        });

        // Без CSS остаётся колонтитул из диалога.
        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = "<topic id=\"t\"><title>Т</title><body><p>Текст.</p></body></topic>",
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            project.SetDocxLayout(new DocxLayout { HeaderText = "Из диалога", NoHeaderOnFirstPage = false });
            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            using var doc = WordprocessingDocument.Open(docx, false);
            Check(doc.MainDocumentPart!.HeaderParts.Single().Header!.InnerText.Contains("Из диалога"), "без полей в CSS — колонтитул из диалога");
        });

        Section("Колонтитулы DOCX: картинка подгоняется под колонтитул");
        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = "<topic id=\"t\"><title>Т</title><body><p>Текст.</p></body></topic>",
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            File.WriteAllBytes(Path.Combine(root, "wide.png"), MakePng(2000, 500));
            File.WriteAllBytes(Path.Combine(root, "square.png"), MakePng(100, 100));
            File.WriteAllBytes(Path.Combine(root, "banner.png"), MakePng(20000, 500));

            // Поля 20 мм: отступ 567 twips (10 мм), область = 10 мм − 1 мм зазора = 9 мм.
            const double areaMm = 9;
            (long Cx, long Cy) HeaderPicture(DocxLayout layout)
            {
                project.SetDocxLayout(layout);
                var docx = Path.Combine(root, "img.docx");
                new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
                using var doc = WordprocessingDocument.Open(docx, false);
                var extent = doc.MainDocumentPart!.HeaderParts.Single().Header!.Descendants<DrawingWp.Extent>().Single();
                return (extent.Cx!.Value, extent.Cy!.Value);
            }

            DocxLayout Layout(string image, bool fit, double manualMm = 30) => new()
            {
                MarginTopMm = 20, MarginBottomMm = 20, MarginLeftMm = 20, MarginRightMm = 20, NoHeaderOnFirstPage = false,
                HeaderImage = image, HeaderImageHeightMm = manualMm, FitHeaderFooterImages = fit
            };

            var textWidthEmu = (210 - 40) * 36000L;
            var wide = HeaderPicture(Layout("wide.png", fit: true));
            Check(Math.Abs(wide.Cy - areaMm * 36000) < 400 && Math.Abs(wide.Cx - 4 * areaMm * 36000) < 800,
                $"подгонка: 2000×500 — высота области {areaMm} мм, пропорции 4:1 ({wide.Cx}×{wide.Cy} EMU)");
            var square = HeaderPicture(Layout("square.png", fit: true));
            Check(Math.Abs(square.Cy - areaMm * 36000) < 400 && Math.Abs(square.Cx - square.Cy) < 400, $"подгонка: 100×100 — квадрат высотой области ({square.Cx}×{square.Cy})");
            var banner = HeaderPicture(Layout("banner.png", fit: true));
            Check(banner.Cx <= textWidthEmu + 400 && banner.Cy < areaMm * 36000 && Math.Abs(banner.Cx / (double)banner.Cy - 40) < 0.2,
                $"подгонка: очень широкая картинка не шире текста, пропорции сохранены ({banner.Cx}×{banner.Cy})");
            var manual = HeaderPicture(Layout("square.png", fit: false, manualMm: 30));
            Check(Math.Abs(manual.Cy - 30 * 36000) < 400, $"ручная высота (подгонка выключена): 30 мм ({manual.Cy})");
            var wideManual = HeaderPicture(Layout("banner.png", fit: false, manualMm: 30));
            Check(wideManual.Cx <= textWidthEmu + 400, $"ручной режим: ширина всё равно не больше текста ({wideManual.Cx})");
        });
    }

    /// <summary>Настоящий PNG заданного размера (1 бит, белый) — для проверки размеров картинок.</summary>
    private static byte[] MakePng(int width, int height)
    {
        static uint Crc(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }

            return ~crc;
        }

        static void Chunk(Stream stream, string type, byte[] data)
        {
            var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            void Be(uint value) => stream.Write(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
            Be((uint)data.Length);
            stream.Write(body);
            Be(Crc(body));
        }

        var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        void Put(int offset, int value)
        {
            header[offset] = (byte)(value >> 24);
            header[offset + 1] = (byte)(value >> 16);
            header[offset + 2] = (byte)(value >> 8);
            header[offset + 3] = (byte)value;
        }

        Put(0, width);
        Put(4, height);
        header[8] = 1; // 1 бит на пиксель
        header[9] = 0; // градации серого
        Chunk(output, "IHDR", header);

        var rowBytes = (width + 7) / 8 + 1; // байт фильтра + строка
        var raw = new byte[rowBytes * height];
        for (var i = 0; i < raw.Length; i++)
        {
            raw[i] = i % rowBytes == 0 ? (byte)0 : (byte)0xFF;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true))
        {
            zlib.Write(raw);
        }

        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }
}
