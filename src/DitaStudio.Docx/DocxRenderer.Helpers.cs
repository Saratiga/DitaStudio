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

    private W.Paragraph HeadingParagraph(List<OpenXmlElement> runs, int level) =>
        Para(DocxStyleCatalog.Heading(level), runs);

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
