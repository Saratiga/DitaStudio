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

// DOCX: блочные элементы — разделы, примечания, код, списки определений и параметров, рисунки.
public sealed partial class DocxRenderer
{
    private IEnumerable<OpenXmlCompositeElement> RenderChildrenBlocks(DitaNode node, int level)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind != NodeKind.Element || !Include(child))
            {
                continue;
            }

            foreach (var block in RenderBlock(child, level))
            {
                yield return block;
            }
        }
    }

    private IEnumerable<OpenXmlCompositeElement> RenderBlock(DitaNode node, int level) =>
        Include(node) && PagePlacement.Of(node) is { } place
            ? PlacedOnPage(RenderBlockCore(node, level), place)
            : RenderBlockCore(node, level);

    /// <summary>
    /// Блок на отдельном листе: свой раздел Word с вертикальным выравниванием страницы (vAlign)
    /// между разрывами разделов, по горизонтали — выравнивание абзацев и таблиц. Метки разделов
    /// пока пустые — параметры страницы в них копирует <see cref="DocxPublisher"/> в конце сборки
    /// (<c>FinishPlacedSections</c>).
    /// </summary>
    private static IEnumerable<OpenXmlCompositeElement> PlacedOnPage(IEnumerable<OpenXmlCompositeElement> blocks, string place)
    {
        yield return SectionMark(null);

        W.Paragraph? last = null;
        OpenXmlCompositeElement? lastBlock = null;
        var justification = PagePlacement.Horizontal(place) switch
        {
            "center" => W.JustificationValues.Center,
            "right" => W.JustificationValues.Right,
            _ => W.JustificationValues.Left
        };
        foreach (var block in blocks)
        {
            switch (block)
            {
                case W.Paragraph paragraph:
                    (paragraph.ParagraphProperties ??= new W.ParagraphProperties()).Justification =
                        new W.Justification { Val = justification };
                    paragraph.ParagraphProperties.PageBreakBefore = null;
                    last = paragraph;
                    break;
                case W.Table table:
                    (table.GetFirstChild<W.TableProperties>() ?? table.PrependChild(new W.TableProperties())).TableJustification =
                        new W.TableJustification { Val = justification == W.JustificationValues.Left ? W.TableRowAlignmentValues.Left
                            : justification == W.JustificationValues.Center ? W.TableRowAlignmentValues.Center : W.TableRowAlignmentValues.Right };
                    last = null;
                    break;
                default:
                    last = null;
                    break;
            }

            lastBlock = block;
            yield return block;
        }

        var vertical = PagePlacement.Vertical(place) switch
        {
            "middle" => W.VerticalJustificationValues.Center,
            "bottom" => W.VerticalJustificationValues.Bottom,
            _ => W.VerticalJustificationValues.Top
        };
        if (last is not null && ReferenceEquals(last, lastBlock))
        {
            // Раздел кончается последним абзацем блока — лишней пустой строки внизу листа нет.
            last.ParagraphProperties!.SectionProperties = new W.SectionProperties(new W.VerticalTextAlignmentOnPage { Val = vertical });
        }
        else
        {
            yield return SectionMark(vertical);
        }
    }

    /// <summary>Пустой абзац-метка разрыва раздела: без строки высотой в абзац.</summary>
    private static W.Paragraph SectionMark(W.VerticalJustificationValues? vertical)
    {
        var section = new W.SectionProperties();
        if (vertical is { } value)
        {
            section.Append(new W.VerticalTextAlignmentOnPage { Val = value });
        }

        return new W.Paragraph(new W.ParagraphProperties
        {
            SpacingBetweenLines = new W.SpacingBetweenLines { Before = "0", After = "0", Line = "20", LineRule = W.LineSpacingRuleValues.Exact },
            ParagraphMarkRunProperties = new W.ParagraphMarkRunProperties(new W.FontSize { Val = "2" }),
            SectionProperties = section
        });
    }

    private IEnumerable<OpenXmlCompositeElement> RenderBlockCore(DitaNode node, int level)
    {
        if (!Include(node))
        {
            yield break;
        }

        using var scope = BlockScope(node);

        switch (BlockElementCategoryMap.Of(node.Name))
        {
            case BlockElementCategory.ContainerDiv:
                foreach (var block in RenderChildrenBlocks(node, level))
                {
                    yield return block;
                }

                yield break;

            case BlockElementCategory.ContainerSection:
                foreach (var block in RenderSection(node, level))
                {
                    yield return block;
                }

                yield break;

            case BlockElementCategory.Preformatted:
                yield return RenderPre(node);
                yield break;

            case BlockElementCategory.Figure:
                foreach (var block in RenderFigure(node, level))
                {
                    yield return block;
                }

                yield break;

            case BlockElementCategory.SimpleTable:
                yield return RenderSimpleTable(node);
                yield break;

            case BlockElementCategory.Skip:
                yield break;

            case BlockElementCategory.ListUnordered:
                foreach (var block in RenderList(node, level, numId: null, ilvl: 0, ordered: false))
                {
                    yield return block;
                }

                yield break;

            case BlockElementCategory.StepsGroup:
                yield return GeneratedTitle(L.Steps);
                foreach (var block in RenderList(node, level, numId: null, ilvl: 0, ordered: node.Name == "steps"))
                {
                    yield return block;
                }

                yield break;
        }

        switch (node.Name)
        {
            case "p" when HeadingNumbering.IsNumbered(node):
            {
                var paragraph = Para(DocxStyleCatalog.BodyText, RenderInlineRuns(node));
                NumberParagraph(paragraph);
                yield return WithOutputClass(paragraph, node);
                yield break;
            }

            case "p":
                yield return WithOutputClass(Para(DocxStyleCatalog.BodyText, RenderInlineRuns(node)), node);
                yield break;

            case "ol":
                foreach (var block in RenderList(node, level, numId: null, ilvl: 0, ordered: true))
                {
                    yield return block;
                }

                yield break;

            case "dl":
                foreach (var block in RenderDl(node))
                {
                    yield return block;
                }

                yield break;

            case "parml":
                foreach (var block in RenderParml(node))
                {
                    yield return block;
                }

                yield break;

            case "note":
                yield return RenderNote(node);
                yield break;

            case "lq":
                yield return Para(DocxStyleCatalog.Quote, RenderInlineRuns(node));
                yield break;

            case "table":
                foreach (var block in RenderTable(node))
                {
                    yield return block;
                }

                yield break;

            case "draft-comment":
                if (_options.ShowDraftComments)
                {
                    yield return Para(DocxStyleCatalog.DraftComment, RenderInlineRuns(node));
                }

                yield break;

            case "indexterm":
                yield break;

            case "title":
                yield return Para(DocxStyleCatalog.BlockTitle, RenderInlineRuns(node));
                yield break;

            case "info":
            case "stepxmp":
            case "stepresult":
            case "steptroubleshooting":
            case "tutorialinfo":
                yield return Para(DocxStyleCatalog.StepInfo, RenderInlineRuns(node));
                yield break;

            case "related-links":
                foreach (var block in RenderRelatedLinks(node))
                {
                    yield return block;
                }

                yield break;

            default:
                foreach (var block in RenderByDisplayKind(node, level))
                {
                    yield return block;
                }

                yield break;
        }
    }

    /// <summary>Резервный путь для имён, не перечисленных явно ни в одном switch выше — по
    /// их display-kind из каталога (для элементов, которых каталог вообще не знает, — как
    /// обычный абзац).</summary>
    private IEnumerable<OpenXmlCompositeElement> RenderByDisplayKind(DitaNode node, int level)
    {
        var def = _catalog.Get(node.Name);
        if (def is null)
        {
            yield return Para(DocxStyleCatalog.Normal, RenderInlineRuns(node));
            yield break;
        }

        switch (def.Display)
        {
            case DisplayKind.Inline:
            case DisplayKind.Empty:
                yield return Para(DocxStyleCatalog.Normal, RenderInlineRunsForNode(node).ToList());
                yield break;
            case DisplayKind.Meta:
                yield break;
            case DisplayKind.Preformatted:
                yield return RenderPre(node);
                yield break;
            default:
                foreach (var block in RenderChildrenBlocks(node, level))
                {
                    yield return block;
                }

                yield break;
        }
    }

    private IEnumerable<OpenXmlCompositeElement> RenderSection(DitaNode node, int level)
    {
        var label = node.Name switch
        {
            "prereq" => L.Prerequisites,
            "context" => L.Context,
            "result" => L.Result,
            "postreq" => L.PostRequisites,
            "tasktroubleshooting" => L.TaskTroubleshooting,
            "condition" => L.Condition,
            "cause" => L.Cause,
            "remedy" => L.Remedy,
            "example" => L.Example,
            _ => null
        };

        var explicitTitle = node.FirstElement("title");
        if (explicitTitle is not null)
        {
            yield return HeadingParagraph(RenderInlineRuns(explicitTitle), Math.Min(level + 1, 6), TocRules.IsUnnumbered(explicitTitle));
        }
        else if (label is not null)
        {
            yield return HeadingParagraph(new List<OpenXmlElement> { new W.Run(new W.Text(label)) }, Math.Min(level + 1, 6));
        }

        foreach (var child in node.Children)
        {
            if (child.Kind == NodeKind.Element && ReferenceEquals(child, explicitTitle))
            {
                continue;
            }

            if (child.Kind != NodeKind.Element || !Include(child))
            {
                continue;
            }

            foreach (var block in RenderBlock(child, level + 1))
            {
                yield return block;
            }
        }
    }

    private W.Paragraph GeneratedTitle(string label) => Para(DocxStyleCatalog.GeneratedTitle, label);

    private W.Paragraph RenderNote(DitaNode node)
    {
        var type = node.GetAttribute("type") ?? "note";
        var label = L.NoteLabel(type);
        var runs = new List<OpenXmlElement>
        {
            new W.Run(new W.RunProperties(new W.RunStyle { Val = DocxStyleCatalog.NoteLabel }),
                new W.Text(label + ": ") { Space = SpaceProcessingModeValues.Preserve })
        };
        runs.AddRange(RenderInlineRuns(node));
        var style = type switch
        {
            "tip" or "fastpath" => DocxStyleCatalog.NoteTip,
            "important" or "remember" or "restriction" => DocxStyleCatalog.NoteImportant,
            "caution" or "attention" or "warning" or "notice" => DocxStyleCatalog.NoteWarning,
            "danger" => DocxStyleCatalog.NoteDanger,
            _ => DocxStyleCatalog.Note
        };
        return Para(style, runs);
    }

    private W.Paragraph RenderPre(DitaNode node)
    {
        var text = node.InnerText;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var runs = new List<OpenXmlElement>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                runs.Add(new W.Run(new W.Break()));
            }

            runs.Add(new W.Run(new W.Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve }));
        }

        return Para(DocxStyleCatalog.CodeBlock, runs);
    }

    private IEnumerable<OpenXmlCompositeElement> RenderDl(DitaNode node)
    {
        var head = node.FirstElement("dlhead");
        if (head is not null)
        {
            var runs = new List<OpenXmlElement>();
            foreach (var h in head.ElementChildren())
            {
                runs.AddRange(RenderInlineRuns(h));
                runs.Add(new W.Run(new W.Text("  ")));
            }

            yield return Para(DocxStyleCatalog.DefinitionTerm, runs);
        }

        foreach (var entry in node.ElementChildren().Where(e => e.Name == "dlentry"))
        {
            foreach (var dt in entry.ElementChildren().Where(e => e.Name == "dt"))
            {
                yield return Para(DocxStyleCatalog.DefinitionTerm, RenderInlineRuns(dt));
            }

            foreach (var dd in entry.ElementChildren().Where(e => e.Name == "dd"))
            {
                yield return Para(DocxStyleCatalog.Definition, RenderInlineRuns(dd));
            }
        }
    }

    private IEnumerable<OpenXmlCompositeElement> RenderParml(DitaNode node)
    {
        foreach (var entry in node.ElementChildren().Where(e => e.Name == "plentry"))
        {
            foreach (var pt in entry.ElementChildren().Where(e => e.Name == "pt"))
            {
                yield return Para(DocxStyleCatalog.DefinitionTerm, RenderInlineRuns(pt));
            }

            foreach (var pd in entry.ElementChildren().Where(e => e.Name == "pd"))
            {
                yield return Para(DocxStyleCatalog.Definition, RenderInlineRuns(pd));
            }
        }
    }

    private IEnumerable<OpenXmlCompositeElement> RenderFigure(DitaNode node, int level)
    {
        var title = node.FirstElement("title");
        foreach (var child in node.Children)
        {
            if (child.Kind == NodeKind.Element && (child.Name == "title" || child.Name == "desc"))
            {
                continue;
            }

            if (child.Kind != NodeKind.Element || !Include(child))
            {
                continue;
            }

            foreach (var block in RenderBlock(child, level))
            {
                yield return block;
            }
        }

        if (title is not null)
        {
            _figureNumber++;
            var captionRuns = new List<OpenXmlElement>();
            if (_options.NumberFiguresAndTables)
            {
                captionRuns.Add(new W.Run(new W.Text($"{L.Figure} {_figureNumber}. ") { Space = SpaceProcessingModeValues.Preserve }));
            }

            captionRuns.AddRange(RenderInlineRuns(title));
            yield return Para(DocxStyleCatalog.FigureCaption, captionRuns);
        }

        var desc = node.FirstElement("desc");
        if (desc is not null)
        {
            yield return Para(DocxStyleCatalog.FigureCaption, RenderInlineRuns(desc));
        }
    }
}
