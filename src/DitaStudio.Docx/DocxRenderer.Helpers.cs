using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// DOCX: вспомогательные методы.
public sealed partial class DocxRenderer
{
    private static bool HasOutputClass(DitaNode node, string token) =>
        (node.GetAttribute("outputclass") ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Contains(token, StringComparer.Ordinal);

    /// <summary>Полоса на полях у абзаца с непустым атрибутом rev — штатная DITA-пометка
    /// изменений (не полноценный track changes с историей правок), см. HtmlRenderer.BuildClassAttr.</summary>
    private OpenXmlCompositeElement WithOutputClass(W.Paragraph paragraph, DitaNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.GetAttribute("rev")))
        {
            EnsureParagraphProperties(paragraph).Append(new W.ParagraphBorders(
                DocxPropsWriter.Border(new W.LeftBorder(), Sheet.RevBorder)));
        }

        return paragraph;
    }

    private W.Paragraph HeadingParagraph(List<OpenXmlElement> runs, int level, bool unnumbered = false)
    {
        // Нумерованные абзацы после заголовка — на уровень ниже его (2.3 → 2.3.1), простая
        // нумерация абзацев начинается заново.
        _paragraphNumId = null;
        if (!unnumbered)
        {
            _lastHeadingLevel = level;
        }

        return Para(unnumbered ? DocxStyleCatalog.HeadingPlain(level) : DocxStyleCatalog.Heading(level), runs);
    }

    private int _lastHeadingLevel;
    private int? _paragraphNumId;

    /// <summary>
    /// Нумерованный абзац: в списке заголовков — на уровень ниже последнего заголовка (номер
    /// «2.3.1» Word считает сам), без нумерации заголовков — простой список «1, 2, 3» в разделе.
    /// </summary>
    private void NumberParagraph(W.Paragraph paragraph)
    {
        var (numId, level) = _options.HeadingNumId is { } headings
            ? (headings, Math.Min(_lastHeadingLevel, 8))
            : (_paragraphNumId ??= AllocateNumbering(ordered: true), 0);
        var numbering = new W.NumberingProperties(new W.NumberingLevelReference { Val = level }, new W.NumberingId { Val = numId });

        // numPr — после pStyle, keepNext, keepLines, pageBreakBefore, framePr, widowControl.
        var properties = EnsureParagraphProperties(paragraph);
        var before = properties.ChildElements.LastOrDefault(e => e is W.ParagraphStyleId or W.KeepNext or W.KeepLines
            or W.PageBreakBefore or W.FrameProperties or W.WidowControl);
        if (before is null)
        {
            properties.PrependChild(numbering);
        }
        else
        {
            properties.InsertAfter(numbering, before);
        }
    }

    /// <summary>
    /// Начало подписи: «Рисунок », поле Word <c>SEQ Рисунок</c> с текущим номером (работают «Обновить поля»,
    /// список иллюстраций и перекрёстные ссылки) и разделитель перед названием.
    /// </summary>
    private IEnumerable<OpenXmlElement> CaptionLabel(string label, int number)
    {
        yield return new W.Run(new W.Text(label + " ") { Space = SpaceProcessingModeValues.Preserve });
        yield return new W.SimpleField(new W.Run(new W.Text(number.ToString(System.Globalization.CultureInfo.InvariantCulture))))
        {
            Instruction = $" SEQ {label.Replace(' ', '_')} \\* ARABIC "
        };
        yield return new W.Run(new W.Text(CaptionRules.Separator(_options.CaptionSeparator)) { Space = SpaceProcessingModeValues.Preserve });
    }

    /// <summary>Абзацы рисунка по умолчанию стоят по центру; своё выравнивание (align-…) остаётся.</summary>
    private static void CenterUnlessAligned(OpenXmlCompositeElement block)
    {
        if (block is W.Paragraph paragraph && EnsureParagraphProperties(paragraph).Justification is null)
        {
            paragraph.ParagraphProperties!.Justification = new W.Justification { Val = W.JustificationValues.Center };
        }
    }

    private static W.ParagraphProperties EnsureParagraphProperties(W.Paragraph paragraph) =>
        paragraph.ParagraphProperties ??= new W.ParagraphProperties();

    private void PrependBookmark(W.Paragraph paragraph, string bookmarkName)
    {
        // Закладка — после pPr: свойства абзаца обязаны быть его первым дочерним элементом.
        var id = _nextBookmarkId++;
        var index = paragraph.ParagraphProperties is null ? 0 : 1;
        paragraph.InsertAt(new W.BookmarkStart { Id = id.ToString(), Name = bookmarkName }, index);
        paragraph.InsertAt(new W.BookmarkEnd { Id = id.ToString() }, index + 1);
    }

    /// <summary>Приводит произвольную строку к допустимому имени закладки Word (буквы/цифры/
    /// подчёркивание, не начинается с цифры, не длиннее 40 символов).</summary>
    public static string SafeBookmarkName(string seed)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in seed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }

        var result = sb.ToString();
        if (result.Length == 0 || char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        return result.Length > 40 ? result[..40] : result;
    }
}
