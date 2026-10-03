using DocumentFormat.OpenXml.Packaging;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

/// <summary>Картинка в строке текста (wp:inline) — для тела документа и колонтитулов.</summary>
internal static class DocxPictures
{
    /// <summary>Добавляет файл картинки в часть документа (тело, колонтитул); id связи или null — формат не тот.</summary>
    public static string? AddImage(OpenXmlPart part, string path)
    {
        ImagePart? image = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => Add(part, ImagePartType.Png),
            ".jpg" or ".jpeg" => Add(part, ImagePartType.Jpeg),
            ".gif" => Add(part, ImagePartType.Gif),
            ".bmp" => Add(part, ImagePartType.Bmp),
            ".tif" or ".tiff" => Add(part, ImagePartType.Tiff),
            _ => null
        };
        if (image is null)
        {
            return null;
        }

        using (var stream = File.OpenRead(path))
        {
            image.FeedData(stream);
        }

        return part.GetIdOfPart(image);
    }

    /// <summary>Добавляет готовый PNG (например, растеризованный SVG) в часть документа.</summary>
    public static string? AddPng(OpenXmlPart part, byte[] png)
    {
        var image = Add(part, ImagePartType.Png);
        if (image is null)
        {
            return null;
        }

        using var stream = new MemoryStream(png);
        image.FeedData(stream);
        return part.GetIdOfPart(image);
    }

    private static ImagePart? Add(OpenXmlPart part, PartTypeInfo type) => part switch
    {
        MainDocumentPart main => main.AddImagePart(type),
        HeaderPart header => header.AddImagePart(type),
        FooterPart footer => footer.AddImagePart(type),
        _ => null
    };

    /// <summary>
    /// Прогон с картинкой. wp:inline обязан лежать внутри w:drawing — без этой обёртки OpenXml SDK
    /// молча не записывает элемент при сохранении (часть с картинкой остаётся «осиротевшей»).
    /// </summary>
    public static W.Run Inline(string relId, long widthEmu, long heightEmu, uint id, string fileName, string alt) =>
        new(new W.Drawing(new Drawing.Wordprocessing.Inline(
            new Drawing.Wordprocessing.Extent { Cx = widthEmu, Cy = heightEmu },
            new Drawing.Wordprocessing.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new Drawing.Wordprocessing.DocProperties { Id = id, Name = "image" + id, Description = alt },
            new Drawing.Graphic(
                new Drawing.GraphicData(
                    new Pic.Picture(
                        new Pic.NonVisualPictureProperties(
                            new Pic.NonVisualDrawingProperties { Id = id, Name = fileName },
                            new Pic.NonVisualPictureDrawingProperties()),
                        new Pic.BlipFill(
                            new Drawing.Blip { Embed = relId },
                            new Drawing.Stretch(new Drawing.FillRectangle())),
                        new Pic.ShapeProperties(
                            new Drawing.Transform2D(
                                new Drawing.Offset { X = 0, Y = 0 },
                                new Drawing.Extents { Cx = widthEmu, Cy = heightEmu }),
                            new Drawing.PresetGeometry(new Drawing.AdjustValueList()) { Preset = Drawing.ShapeTypeValues.Rectangle }))
                ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0
        }));
}
