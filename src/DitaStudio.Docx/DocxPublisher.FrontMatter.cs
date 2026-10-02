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
using DitaStudio.Core.Localization;

namespace DitaStudio.Docx;

// Титульный лист и оглавление.
public sealed partial class DocxPublisher
{
    /// <summary>Уровень структуры «основной текст» — абзац не попадает в оглавление Word.</summary>
    private const int BodyTextOutlineLevel = 9;

    /// <summary>Строка оглавления: уровень, текст, закладка топика.</summary>
    private sealed record TocEntry(int Level, string Text, string Bookmark);

    private void WriteFrontMatter(W.Body body, MainDocumentPart mainPart, string title, string date, Labels labels, DocxLayout layout,
        IReadOnlyList<TocEntry> tocEntries, double textWidthPt, List<string> warnings)
    {
        var any = false;
        if (layout.TitlePage)
        {
            if (layout.TitleImage.Length > 0)
            {
                var full = DocxLayout.ResolveImage(_project.RootPath, layout.TitleImage);
                if (full is null || DocxPictures.AddImage(mainPart, full) is not { } relId)
                {
                    warnings.Add(Loc.T("Core_TheTitlePageImageWasNot", layout.TitleImage));
                }
                else
                {
                    var (naturalWidth, naturalHeight) = ImageSize.ReadEmuSize(full, null, null);
                    var (width, height) = DocxHeaderFooter.Fit(naturalWidth, naturalHeight, layout.TitleImageHeightMm, textWidthPt);
                    body.Append(new W.Paragraph(
                        new W.ParagraphProperties(new W.SpacingBetweenLines { After = "240" },
                            new W.Justification
                            {
                                Val = layout.TitleImageAlignment switch
                                {
                                    DocxHeaderAlignment.Left => W.JustificationValues.Left,
                                    DocxHeaderAlignment.Right => W.JustificationValues.Right,
                                    _ => W.JustificationValues.Center
                                }
                            }),
                        DocxPictures.Inline(relId, width, height, 9100, Path.GetFileName(full), "Картинка титульной страницы")));
                }
            }

            body.Append(StyledParagraph(DocxStyleCatalog.Title, title));
            if (layout.Subtitle.Length > 0)
            {
                body.Append(StyledParagraph(DocxStyleCatalog.Subtitle, layout.Subtitle));
            }

            if (layout.Author.Length > 0)
            {
                body.Append(StyledParagraph(DocxStyleCatalog.TitleAuthor, layout.Author));
            }

            if (layout.TitlePageDate)
            {
                body.Append(StyledParagraph(DocxStyleCatalog.TitleAuthor, date));
            }

            any = true;
        }

        if (layout.TableOfContents)
        {
            if (any)
            {
                body.Append(PageBreakParagraph());
            }

            body.Append(StyledParagraph(DocxStyleCatalog.TocHeading, layout.TocTitle.Length > 0 ? layout.TocTitle : labels.Contents));
            foreach (var paragraph in BuildTocParagraphs(layout.TocDepth, tocEntries))
            {
                body.Append(paragraph);
            }
            any = true;
        }

        // Если каждый топик верхнего уровня и так начинается с новой страницы, отдельный разрыв
        // дал бы пустую страницу.
        if (any && !layout.PageBreakBeforeTopLevel)
        {
            body.Append(PageBreakParagraph());
        }
    }

    private static W.Paragraph StyledParagraph(string styleId, string text) =>
        new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = styleId }),
            new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static W.Paragraph PageBreakParagraph() =>
        new(new W.Run(new W.Break { Type = W.BreakValues.Page }));

    /// <summary>
    /// Поле TOC, заранее заполненное строками-ссылками на топики: оглавление видно сразу — в
    /// LibreOffice, просмотрщиках, в Word до обновления полей; Word при открытии пересобирает его
    /// с номерами страниц (поле помечено устаревшим, в настройках — обновление при открытии).
    /// </summary>
    private static IEnumerable<W.Paragraph> BuildTocParagraphs(int depth, IReadOnlyList<TocEntry> entries)
    {
        var instruction = $" TOC \\o \"1-{Math.Clamp(depth, 1, 6)}\" \\h \\z \\u ";
        if (entries.Count == 0)
        {
            yield return new W.Paragraph(new W.SimpleField(
                new W.Run(new W.Text("Обновите оглавление: F9 или правой кнопкой → «Обновить поле»")))
            {
                Instruction = instruction.Trim()
            });
            yield break;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = DocxStyleCatalog.Toc(entry.Level) }));
            if (i == 0)
            {
                paragraph.Append(
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin, Dirty = true }),
                    new W.Run(new W.FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve }),
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }));
            }

            paragraph.Append(new W.Hyperlink(new W.Run(new W.Text(entry.Text) { Space = SpaceProcessingModeValues.Preserve }))
            {
                Anchor = entry.Bookmark,
                History = true
            });

            if (i == entries.Count - 1)
            {
                paragraph.Append(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }));
            }

            yield return paragraph;
        }
    }
}
