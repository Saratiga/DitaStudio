using System.Globalization;
using System.Text.RegularExpressions;
using DitaStudio.Core.IO;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// Колонтитулы.
public sealed partial class DocxPublisher
{
    // Абзац строится, когда часть колонтитула уже есть: картинка кладётся в неё самую.
    private static string AddHeader(MainDocumentPart mainPart, Func<OpenXmlPart, W.Paragraph> build)
    {
        var part = mainPart.AddNewPart<HeaderPart>();
        part.Header = new W.Header(build(part));
        return mainPart.GetIdOfPart(part);
    }

    private static string AddFooter(MainDocumentPart mainPart, Func<OpenXmlPart, W.Paragraph> build)
    {
        var part = mainPart.AddNewPart<FooterPart>();
        part.Footer = new W.Footer(build(part));
        return mainPart.GetIdOfPart(part);
    }

    /// <summary>
    /// Колонтитул с картинкой: три места — слева, по центру (табуляция по центру строки) и справа
    /// (табуляция по правому краю). Картинка и текст — каждый на своём месте, а при одинаковом
    /// выравнивании — рядом. Без картинки — прежний абзац с выравниванием текста.
    /// </summary>
    private static W.Paragraph HeaderFooterBlock(OpenXmlPart part, string styleId, string text, DocxHeaderAlignment textAlignment,
        string? image, DocxHeaderAlignment imageAlignment, double imageHeightMm, double textWidthPt, string title, string date, uint imageId)
    {
        var textParagraph = HeaderFooterParagraph(styleId, text, textAlignment, title, date);
        if (image is null || DocxPictures.AddImage(part, image) is not { } relId)
        {
            return textParagraph;
        }

        var (naturalWidth, naturalHeight) = ImageSize.ReadEmuSize(image, null, null);
        var (widthEmu, heightEmu) = DocxHeaderFooter.Fit(naturalWidth, naturalHeight, imageHeightMm, textWidthPt);
        var picture = DocxPictures.Inline(relId, widthEmu, heightEmu, imageId, Path.GetFileName(image), "Логотип");
        var textRuns = textParagraph.ChildElements.Where(e => e is not W.ParagraphProperties).Select(e => e.CloneNode(true)).ToList();

        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = styleId },
            new W.Tabs(
                new W.TabStop { Val = W.TabStopValues.Center, Position = DocxPropsWriter.Twips(textWidthPt / 2) },
                new W.TabStop { Val = W.TabStopValues.Right, Position = DocxPropsWriter.Twips(textWidthPt) }),
            new W.Justification { Val = W.JustificationValues.Left }));

        foreach (var slot in new[] { DocxHeaderAlignment.Left, DocxHeaderAlignment.Center, DocxHeaderAlignment.Right })
        {
            if (slot != DocxHeaderAlignment.Left)
            {
                paragraph.Append(new W.Run(new W.TabChar()));
            }

            if (slot == imageAlignment)
            {
                paragraph.Append(picture);
                if (slot == textAlignment && textRuns.Count > 0)
                {
                    paragraph.Append(TextRun("  "));
                }
            }

            if (slot == textAlignment)
            {
                paragraph.Append(textRuns);
            }
        }

        return paragraph;
    }

    private static readonly Regex FieldPattern = new(@"\{(page|pages|title|date)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Абзац колонтитула: текст с полями {page}, {pages} (поля Word PAGE/NUMPAGES —
    /// Word сам подставит номера), {title} и {date} (подставляются при сборке).</summary>
    public static W.Paragraph HeaderFooterParagraph(string styleId, string text, DocxHeaderAlignment alignment, string title, string date)
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = styleId },
            new W.Justification
            {
                Val = alignment switch
                {
                    DocxHeaderAlignment.Left => W.JustificationValues.Left,
                    DocxHeaderAlignment.Center => W.JustificationValues.Center,
                    _ => W.JustificationValues.Right
                }
            }));

        var position = 0;
        foreach (Match match in FieldPattern.Matches(text))
        {
            if (match.Index > position)
            {
                paragraph.Append(TextRun(text[position..match.Index]));
            }

            switch (match.Groups[1].Value.ToLowerInvariant())
            {
                case "page":
                    paragraph.Append(new W.SimpleField(TextRun("1")) { Instruction = " PAGE " });
                    break;
                case "pages":
                    paragraph.Append(new W.SimpleField(TextRun("1")) { Instruction = " NUMPAGES " });
                    break;
                case "title":
                    paragraph.Append(TextRun(title));
                    break;
                default:
                    paragraph.Append(TextRun(date));
                    break;
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            paragraph.Append(TextRun(text[position..]));
        }

        return paragraph;
    }

    private static W.Run TextRun(string text) => new(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
}
