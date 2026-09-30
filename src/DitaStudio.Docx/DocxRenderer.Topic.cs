using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Validation;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// DOCX: топик целиком (заголовок, тело, вложенные топики) и ссылки из таблиц соответствий.
public sealed partial class DocxRenderer
{
    /// <summary>Отрисовывает один топик и добавляет его содержимое в конец body.</summary>
    /// <param name="unnumbered">Заголовок без номера и вне оглавления (строка карты с toc="no").</param>
    /// <param name="pageBreakBefore">Разрыв страницы перед топиком из карты (<see cref="TopicPageBreak"/>): true — с новой страницы,
    /// false — не с новой (перекрывает настройку «каждый топик верхнего уровня — с новой страницы»), null — по настройке.</param>
    public void RenderTopic(DitaDocument document, DitaNode topic, W.Body body, int headingLevel, string? bookmarkName,
        bool unnumbered = false, bool? pageBreakBefore = null)
    {
        _document = document;
        using var topicScope = BlockScope(topic);
        var isTopLevel = bookmarkName is not null; // вложенные топики файла вызываются с bookmarkName == null
        var def = _catalog.Get(topic.Name);
        var isGlossary = def?.ClassAttr.Contains("glossentry/") == true;

        foreach (var child in topic.ElementChildren())
        {
            if (!Include(child))
            {
                continue;
            }

            switch (child.Name)
            {
                case "title" when DitaValidator.IsEmptyTitle(child):
                    // Топик без заголовка: абзаца-заголовка нет (и в оглавление он не попадает),
                    // закладка для ссылок на топик ставится прямо в тело перед содержимым.
                    if (bookmarkName is not null)
                    {
                        var id = (_nextBookmarkId++).ToString();
                        body.Append(new W.BookmarkStart { Id = id, Name = bookmarkName }, new W.BookmarkEnd { Id = id });
                        bookmarkName = null;
                    }

                    if (pageBreakBefore == true)
                    {
                        body.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page }))); // заголовка нет — разрыв отдельным абзацем
                        pageBreakBefore = null;
                    }

                    break;

                case "title":
                case "glossterm":
                    W.Paragraph heading;
                    using (BlockScope(child))
                    {
                        heading = HeadingParagraph(RenderInlineRuns(child), headingLevel, unnumbered || TocRules.IsUnnumbered(child));
                    }

                    if (bookmarkName is not null)
                    {
                        PrependBookmark(heading, bookmarkName);
                    }

                    if (HasOutputClass(child, "page-break-before") || pageBreakBefore == true)
                    {
                        EnsureParagraphProperties(heading).PageBreakBefore = new W.PageBreakBefore();
                    }
                    else if (pageBreakBefore == false)
                    {
                        // Топик из карты помечен «не с новой страницы» — перекрывает разрыв, заданный стилем заголовка.
                        EnsureParagraphProperties(heading).PageBreakBefore = new W.PageBreakBefore { Val = false };
                    }

                    pageBreakBefore = null;

                    body.Append(heading);
                    bookmarkName = null;
                    break;

                case "shortdesc":
                    using (BlockScope(child))
                    {
                        body.Append(Para(DocxStyleCatalog.Shortdesc, RenderInlineRuns(child)));
                    }

                    break;

                case "abstract":
                case "glossdef":
                    foreach (var block in RenderChildrenBlocks(child, headingLevel))
                    {
                        body.Append(block);
                    }

                    break;

                case "prolog":
                case "titlealts":
                    break;

                case "body":
                case "conbody":
                case "refbody":
                case "taskbody":
                case "troublebody":
                case "glossBody":
                case "learningBasebody":
                case "learningOverviewbody":
                case "learningContentbody":
                case "learningSummarybody":
                case "learningAssessmentbody":
                case "learningPlanbody":
                    foreach (var block in RenderChildrenBlocks(child, headingLevel))
                    {
                        body.Append(block);
                    }

                    break;

                case "related-links":
                    foreach (var block in RenderRelatedLinks(child))
                    {
                        body.Append(block);
                    }

                    break;

                default:
                    if (_catalog.Get(child.Name)?.IsTopicType == true)
                    {
                        RenderTopic(document, child, body, headingLevel + 1, bookmarkName);
                        bookmarkName = null;
                    }
                    else
                    {
                        foreach (var block in RenderBlock(child, headingLevel))
                        {
                            body.Append(block);
                        }
                    }

                    break;
            }
        }

        if (isTopLevel)
        {
            foreach (var block in RenderReltableLinks())
            {
                body.Append(block);
            }
        }

        _ = isGlossary;
    }

    /// <summary>Автоматический блок «Смотрите также» из таблицы соответствий (reltable) —
    /// отдельно от авторского related-links, который топик мог указать в разметке сам.</summary>
    private IEnumerable<OpenXmlCompositeElement> RenderReltableLinks()
    {
        if (_options.RelatedTopics is null || _options.RelatedTopics.Count == 0)
        {
            yield break;
        }

        yield return Para(DocxStyleCatalog.RelatedLinksTitle, L.RelatedLinks);

        foreach (var link in _options.RelatedTopics)
        {
            var reference = new DitaReference(link.Path, link.TopicId, null, link.Path);
            var title = TitleOf(reference) ?? Path.GetFileNameWithoutExtension(link.Path);
            var bookmark = _options.TopicBookmark?.Invoke(link.Path, link.TopicId);

            OpenXmlElement run = bookmark is not null
                ? new W.Hyperlink(HyperlinkRun(title)) { Anchor = bookmark, History = true }
                : new W.Run(new W.Text(title));

            yield return Para(DocxStyleCatalog.RelatedLink, new[] { run });
        }
    }
}
