using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

public sealed class DocxRenderOptions
{
    public Labels Labels { get; set; } = Labels.Russian;

    public bool ShowDraftComments { get; set; }

    /// <summary>Условная фильтрация: возвращает false, если элемент нужно исключить.</summary>
    public Func<DitaNode, bool>? Filter { get; set; }

    public bool NumberFiguresAndTables { get; set; } = true;

    /// <summary>Возвращает имя закладки для топика (путь, id элемента) — на неё ссылаются
    /// перекрёстные ссылки. Null, если целевой топик не входит в публикацию.</summary>
    public Func<string, string?, string?>? TopicBookmark { get; set; }

    public List<string> Warnings { get; } = new();

    /// <summary>Цепочка имён областей ключей (keyscope) для топика, который сейчас рендерится —
    /// см. MapItem.KeyScopeChain. Публикатор обновляет её перед каждым RenderTopic.</summary>
    public IReadOnlyList<string>? CurrentKeyScope { get; set; }

    /// <summary>Связанные топики из таблицы соответствий (reltable) для топика, который сейчас
    /// рендерится — см. MapTree.RelatedLinks. Публикатор обновляет перед каждым RenderTopic.</summary>
    public IReadOnlyList<MapTree.RelatedLink>? RelatedTopics { get; set; }
}

/// <summary>
/// Преобразование DITA в родной OOXML (.docx) — параллельно <see cref="HtmlRenderer"/>, но вместо
/// строк собирает Paragraph/Table для одного документа Word. Сноски — настоящие сноски Word
/// (FootnotesPart), поэтому Word сам кладёт их на ту страницу, где стоит ссылка. Списки — через
/// общий NumberingDefinitionsPart с отдельным numId на каждый независимый список (чтобы нумерация
/// начиналась заново). Покрыт основной словарь DITA, используемый в проекте; редкие
/// специализации (hazardstatement, видео/аудио, coderef, MathML/SVG-контейнер, learning-контент)
/// показываются обобщённым абзацем с текстом — как и в HtmlRenderer для неизвестных элементов.
/// </summary>
public sealed class DocxRenderer
{
    private readonly DitaProject _project;
    private readonly MainDocumentPart _mainPart;
    private readonly DocxRenderOptions _options;
    private readonly DitaCatalog _catalog = DitaCatalog.Default;
    private readonly NumberingDefinitionsPart _numberingPart;

    private DitaDocument _document = null!;
    private int _figureNumber;
    private int _tableNumber;
    private int _nextNumId = 1;
    private int _nextFootnoteId = 1;
    private int _nextBookmarkId = 1;
    private int _nextImageId = 1;
    private const uint BulletAbstractNumId = 1000;
    private const uint DecimalAbstractNumId = 1001;

    public DocxRenderer(DitaProject project, MainDocumentPart mainPart, NumberingDefinitionsPart numberingPart,
        DocxRenderOptions? options = null)
    {
        _project = project;
        _mainPart = mainPart;
        _numberingPart = numberingPart;
        _options = options ?? new DocxRenderOptions();
    }

    private Labels L => _options.Labels;

    private bool Include(DitaNode node) => _options.Filter?.Invoke(node) ?? true;

    // ====================================================================== топик

    /// <summary>Отрисовывает один топик и добавляет его содержимое в конец body.</summary>
    public void RenderTopic(DitaDocument document, DitaNode topic, W.Body body, int headingLevel, string? bookmarkName)
    {
        _document = document;
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
                case "title":
                case "glossterm":
                    var heading = HeadingParagraph(RenderInlineRuns(child), headingLevel);
                    if (bookmarkName is not null)
                    {
                        PrependBookmark(heading, bookmarkName);
                    }

                    if (HasOutputClass(child, "page-break-before"))
                    {
                        EnsureParagraphProperties(heading).Append(new W.PageBreakBefore());
                    }

                    body.Append(heading);
                    bookmarkName = null;
                    break;

                case "shortdesc":
                    body.Append(StyledParagraph(RenderInlineRuns(child), italic: true));
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

        yield return StyledParagraph(new List<OpenXmlElement> { new W.Run(new W.Text(L.RelatedLinks)) }, bold: true);

        foreach (var link in _options.RelatedTopics)
        {
            var reference = new DitaReference(link.Path, link.TopicId, null, link.Path);
            var title = TitleOf(reference) ?? Path.GetFileNameWithoutExtension(link.Path);
            var bookmark = _options.TopicBookmark?.Invoke(link.Path, link.TopicId);

            OpenXmlElement run = bookmark is not null
                ? new W.Hyperlink(new W.Run(new W.Text(title))) { Anchor = bookmark, History = true }
                : new W.Run(new W.Text(title));

            var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.Indentation { Left = "227" }));
            paragraph.Append(run);
            yield return paragraph;
        }
    }

    // ====================================================================== блоки

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

    private IEnumerable<OpenXmlCompositeElement> RenderBlock(DitaNode node, int level)
    {
        if (!Include(node))
        {
            yield break;
        }

        switch (node.Name)
        {
            case "p":
                yield return WithOutputClass(Paragraph(RenderInlineRuns(node)), node);
                yield break;

            case "div":
            case "bodydiv":
            case "conbodydiv":
            case "refbodydiv":
            case "sectiondiv":
            case "itemgroup":
            case "equation-block":
                foreach (var block in RenderChildrenBlocks(node, level))
                {
                    yield return block;
                }

                yield break;

            case "section":
            case "example":
            case "refsyn":
            case "prereq":
            case "context":
            case "result":
            case "postreq":
            case "tasktroubleshooting":
            case "condition":
            case "cause":
            case "remedy":
            case "troubleSolution":
            case "steps-informal":
            case "lcIntro":
            case "lcObjectives":
            case "lcSummary":
            case "lcReview":
            case "lcNextSteps":
            case "lcPrereqs":
            case "lcResources":
            case "lcAudience":
            case "lcDuration":
                foreach (var block in RenderSection(node, level))
                {
                    yield return block;
                }

                yield break;

            case "ul":
            case "sl":
            case "choices":
                foreach (var block in RenderList(node, level, numId: null, ilvl: 0, ordered: false))
                {
                    yield return block;
                }

                yield break;

            case "ol":
                foreach (var block in RenderList(node, level, numId: null, ilvl: 0, ordered: true))
                {
                    yield return block;
                }

                yield break;

            case "steps":
            case "steps-unordered":
                yield return GeneratedTitle(L.Steps);
                foreach (var block in RenderList(node, level, numId: null, ilvl: 0, ordered: node.Name == "steps"))
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
                yield return StyledParagraph(RenderInlineRuns(node), italic: true, indent: true);
                yield break;

            case "pre":
            case "codeblock":
            case "screen":
            case "msgblock":
            case "lines":
                yield return RenderPre(node);
                yield break;

            case "fig":
            case "equation-figure":
            case "imagemap":
                foreach (var block in RenderFigure(node, level))
                {
                    yield return block;
                }

                yield break;

            case "table":
                foreach (var block in RenderTable(node))
                {
                    yield return block;
                }

                yield break;

            case "simpletable":
            case "properties":
            case "choicetable":
                yield return RenderSimpleTable(node);
                yield break;

            case "draft-comment":
                if (_options.ShowDraftComments)
                {
                    yield return StyledParagraph(RenderInlineRuns(node), italic: true);
                }

                yield break;

            case "required-cleanup":
            case "indexterm":
            case "data":
            case "data-about":
            case "resourceid":
            case "titlealts":
            case "prolog":
                yield break;

            case "title":
                yield return StyledParagraph(RenderInlineRuns(node), bold: true);
                yield break;

            case "related-links":
                foreach (var block in RenderRelatedLinks(node))
                {
                    yield return block;
                }

                yield break;

            default:
                var def = _catalog.Get(node.Name);
                if (def is null)
                {
                    yield return Paragraph(RenderInlineRuns(node));
                    yield break;
                }

                switch (def.Display)
                {
                    case DisplayKind.Inline:
                    case DisplayKind.Empty:
                        yield return Paragraph(RenderInlineRunsForNode(node).ToList());
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
            yield return HeadingParagraph(RenderInlineRuns(explicitTitle), Math.Min(level + 1, 6));
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

    private static W.Paragraph GeneratedTitle(string label) =>
        new(new W.ParagraphProperties(new W.SpacingBetweenLines { Before = "160" }),
            new W.Run(new W.RunProperties(new W.Bold()), new W.Text(label)));

    private W.Paragraph RenderNote(DitaNode node)
    {
        var type = node.GetAttribute("type") ?? "note";
        var label = L.NoteLabel(type);
        var runs = new List<OpenXmlElement>
        {
            new W.Run(new W.RunProperties(new W.Bold()), new W.Text(label + ": "))
        };
        runs.AddRange(RenderInlineRuns(node));
        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.ParagraphBorders(new W.LeftBorder { Val = W.BorderValues.Single, Size = 12, Color = "999999" }),
            new W.Indentation { Left = "227" }));
        paragraph.Append(runs);
        return paragraph;
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

            runs.Add(new W.Run(new W.RunProperties(new W.RunFonts { Ascii = "Consolas" }),
                new W.Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve }));
        }

        var pre = new W.Paragraph(new W.ParagraphProperties(new W.Shading { Fill = "F2F2F2" }));
        pre.Append(runs);
        return pre;
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

            yield return StyledParagraph(runs, bold: true);
        }

        foreach (var entry in node.ElementChildren().Where(e => e.Name == "dlentry"))
        {
            foreach (var dt in entry.ElementChildren().Where(e => e.Name == "dt"))
            {
                yield return StyledParagraph(RenderInlineRuns(dt), bold: true);
            }

            foreach (var dd in entry.ElementChildren().Where(e => e.Name == "dd"))
            {
                yield return StyledParagraph(RenderInlineRuns(dd), indent: true);
            }
        }
    }

    private IEnumerable<OpenXmlCompositeElement> RenderParml(DitaNode node)
    {
        foreach (var entry in node.ElementChildren().Where(e => e.Name == "plentry"))
        {
            foreach (var pt in entry.ElementChildren().Where(e => e.Name == "pt"))
            {
                yield return StyledParagraph(RenderInlineRuns(pt), bold: true);
            }

            foreach (var pd in entry.ElementChildren().Where(e => e.Name == "pd"))
            {
                yield return StyledParagraph(RenderInlineRuns(pd), indent: true);
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
                captionRuns.Add(new W.Run(new W.Text($"{L.Figure} {_figureNumber}. ")));
            }

            captionRuns.AddRange(RenderInlineRuns(title));
            yield return StyledParagraph(captionRuns, italic: true);
        }

        var desc = node.FirstElement("desc");
        if (desc is not null)
        {
            yield return StyledParagraph(RenderInlineRuns(desc), italic: true);
        }
    }

    // ====================================================================== таблицы

    private IEnumerable<OpenXmlCompositeElement> RenderTable(DitaNode node)
    {
        var title = node.FirstElement("title");
        if (title is not null)
        {
            _tableNumber++;
            var captionRuns = new List<OpenXmlElement>();
            if (_options.NumberFiguresAndTables)
            {
                captionRuns.Add(new W.Run(new W.Text($"{L.Table} {_tableNumber}. ")));
            }

            captionRuns.AddRange(RenderInlineRuns(title));
            yield return StyledParagraph(captionRuns, bold: true);
        }

        var allowSplit = HasOutputClass(node, "page-break-auto");
        foreach (var tgroup in node.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            yield return RenderTgroup(tgroup, allowSplit);
            yield return new W.Paragraph(); // Word требует абзац после таблицы, иначе следующий блок "прилипает"
        }
    }

    private W.Table RenderTgroup(DitaNode tgroup, bool allowRowSplit)
    {
        var colspecs = tgroup.ElementChildren().Where(e => e.Name == "colspec").ToList();
        var colNames = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < colspecs.Count; i++)
        {
            var name = colspecs[i].GetAttribute("colname");
            if (!string.IsNullOrEmpty(name))
            {
                colNames[name!] = i;
            }
        }

        var numCols = colspecs.Count;
        if (numCols == 0)
        {
            numCols = Math.Max(1, AllRows(tgroup).Select(r => r.ElementChildren().Count(e => e.Name == "entry")).DefaultIfEmpty(1).Max());
        }

        var table = new W.Table();
        table.Append(new W.TableProperties(
            new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = "999999" },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = "999999" },
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Color = "999999" },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Color = "999999" },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4, Color = "CCCCCC" }),
            new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct }));

        var grid = new W.TableGrid();
        for (var i = 0; i < numCols; i++)
        {
            grid.Append(new W.GridColumn());
        }

        table.Append(grid);

        var pending = new List<(int Start, int End, int Remaining)>();

        var thead = tgroup.FirstElement("thead");
        if (thead is not null)
        {
            foreach (var row in thead.ElementChildren().Where(e => e.Name == "row"))
            {
                table.Append(RenderTableRow(row, colNames, numCols, pending, isHeader: true, allowRowSplit));
            }
        }

        var tbody = tgroup.FirstElement("tbody");
        if (tbody is not null)
        {
            foreach (var row in tbody.ElementChildren().Where(e => e.Name == "row"))
            {
                table.Append(RenderTableRow(row, colNames, numCols, pending, isHeader: false, allowRowSplit));
            }
        }

        return table;
    }

    private static IEnumerable<DitaNode> AllRows(DitaNode tgroup) =>
        tgroup.ElementChildren().Where(e => e.Name is "thead" or "tbody")
            .SelectMany(section => section.ElementChildren().Where(r => r.Name == "row"));

    private W.TableRow RenderTableRow(DitaNode row, Dictionary<string, int> colNames, int numCols,
        List<(int Start, int End, int Remaining)> pending, bool isHeader, bool allowRowSplit)
    {
        var placements = new List<(int Col, int Span, DitaNode? Entry)>();
        foreach (var p in pending)
        {
            placements.Add((p.Start, p.End - p.Start + 1, null));
        }

        var newSpans = new List<(DitaNode Entry, int Start, int End)>();
        var cursor = 0;
        foreach (var entry in row.ElementChildren().Where(e => e.Name == "entry"))
        {
            int start;
            var namest = entry.GetAttribute("namest");
            if (namest is not null && colNames.TryGetValue(namest, out var s))
            {
                start = s;
            }
            else
            {
                start = NextFreeColumn(cursor, placements, numCols);
            }

            var end = start;
            var nameend = entry.GetAttribute("nameend");
            if (nameend is not null && colNames.TryGetValue(nameend, out var e2))
            {
                end = e2;
            }

            placements.Add((start, end - start + 1, entry));
            cursor = end + 1;

            if (int.TryParse(entry.GetAttribute("morerows"), out var more) && more > 0)
            {
                newSpans.Add((entry, start, end));
            }
        }

        placements.Sort((a, b) => a.Col.CompareTo(b.Col));

        var tr = new W.TableRow();
        if (isHeader)
        {
            tr.Append(new W.TableRowProperties(new W.TableHeader()));
        }
        else if (!allowRowSplit)
        {
            tr.Append(new W.TableRowProperties(new W.CantSplit()));
        }

        foreach (var placement in placements)
        {
            if (placement.Entry is null)
            {
                tr.Append(ContinuationCell(placement.Span));
            }
            else
            {
                var isSpanStart = newSpans.Any(sp => ReferenceEquals(sp.Entry, placement.Entry));
                tr.Append(RealCell(placement.Entry, placement.Span, isSpanStart, isHeader));
            }
        }

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            p.Remaining--;
            if (p.Remaining <= 0)
            {
                pending.RemoveAt(i);
            }
            else
            {
                pending[i] = p;
            }
        }

        foreach (var (entry, start, end) in newSpans)
        {
            int.TryParse(entry.GetAttribute("morerows"), out var more);
            pending.Add((start, end, more));
        }

        return tr;
    }

    private static int NextFreeColumn(int from, List<(int Col, int Span, DitaNode? Entry)> placements, int numCols)
    {
        var col = from;
        while (col < numCols && placements.Any(p => col >= p.Col && col < p.Col + p.Span))
        {
            col++;
        }

        return col;
    }

    private W.TableCell RealCell(DitaNode entry, int span, bool isSpanStart, bool isHeader)
    {
        var props = new W.TableCellProperties();
        if (span > 1)
        {
            props.Append(new W.GridSpan { Val = span });
        }

        if (isSpanStart)
        {
            props.Append(new W.VerticalMerge { Val = W.MergedCellValues.Restart });
        }

        if (isHeader)
        {
            props.Append(new W.Shading { Fill = "E8E8E8" });
        }

        var valign = entry.GetAttribute("valign");
        if (valign == "top")
        {
            props.Append(new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Top });
        }
        else if (valign == "bottom")
        {
            props.Append(new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Bottom });
        }
        else if (valign == "middle")
        {
            props.Append(new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center });
        }

        var runs = RenderInlineRuns(entry);
        var paragraphProps = new List<OpenXmlElement>();
        var align = entry.GetAttribute("align");
        if (align is not null)
        {
            paragraphProps.Add(new W.Justification { Val = align switch
            {
                "center" => W.JustificationValues.Center,
                "right" => W.JustificationValues.Right,
                "justify" => W.JustificationValues.Both,
                _ => W.JustificationValues.Left
            }});
        }

        W.Paragraph paragraph;
        if (paragraphProps.Count > 0)
        {
            paragraph = new W.Paragraph(new W.ParagraphProperties(paragraphProps.ToArray()));
            paragraph.Append(runs);
        }
        else
        {
            paragraph = new W.Paragraph(runs.ToArray());
        }

        return new W.TableCell(props, paragraph);
    }

    private static W.TableCell ContinuationCell(int span)
    {
        var props = new W.TableCellProperties(new W.VerticalMerge { Val = W.MergedCellValues.Continue });
        if (span > 1)
        {
            props.Append(new W.GridSpan { Val = span });
        }

        return new W.TableCell(props, new W.Paragraph());
    }

    private W.Table RenderSimpleTable(DitaNode node)
    {
        var headNames = node.Name switch
        {
            "properties" => new[] { "proptypehd", "propvaluehd", "propdeschd" },
            "choicetable" => new[] { "choptionhd", "chdeschd" },
            _ => new[] { "stentry" }
        };
        var rowNames = node.Name switch
        {
            "properties" => new[] { "proptype", "propvalue", "propdesc" },
            "choicetable" => new[] { "choption", "chdesc" },
            _ => new[] { "stentry" }
        };

        var rows = node.ElementChildren().Where(e => e.Name is "strow" or "property" or "chrow").ToList();
        var numCols = Math.Max(1, rows.Select(r => r.ElementChildren().Count(c => rowNames.Contains(c.Name))).DefaultIfEmpty(headNames.Length).Max());

        var table = new W.Table();
        table.Append(new W.TableProperties(
            new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = "999999" },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = "999999" },
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Color = "999999" },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Color = "999999" },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4, Color = "CCCCCC" }),
            new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct }));

        var grid = new W.TableGrid();
        for (var i = 0; i < numCols; i++)
        {
            grid.Append(new W.GridColumn());
        }

        table.Append(grid);

        var head = node.ElementChildren().FirstOrDefault(e => e.Name is "sthead" or "prophead" or "chhead");
        List<string>? headLabels = node.Name switch
        {
            "properties" => new List<string> { L.Type, L.Value, L.Description },
            "choicetable" => new List<string> { L.Options, L.Description },
            _ => null
        };

        if (head is not null)
        {
            var tr = new W.TableRow(new W.TableRowProperties(new W.TableHeader()));
            foreach (var cell in head.ElementChildren().Where(c => headNames.Contains(c.Name)))
            {
                tr.Append(new W.TableCell(new W.TableCellProperties(new W.Shading { Fill = "E8E8E8" }),
                    new W.Paragraph(RenderInlineRuns(cell).ToArray())));
            }

            table.Append(tr);
        }
        else if (headLabels is not null)
        {
            var tr = new W.TableRow(new W.TableRowProperties(new W.TableHeader()));
            foreach (var label in headLabels)
            {
                tr.Append(new W.TableCell(new W.TableCellProperties(new W.Shading { Fill = "E8E8E8" }),
                    new W.Paragraph(new W.Run(new W.RunProperties(new W.Bold()), new W.Text(label)))));
            }

            table.Append(tr);
        }

        foreach (var row in rows)
        {
            var tr = new W.TableRow();
            foreach (var cell in row.ElementChildren().Where(c => rowNames.Contains(c.Name)))
            {
                tr.Append(new W.TableCell(new W.Paragraph(RenderInlineRuns(cell).ToArray())));
            }

            table.Append(tr);
        }

        return table;
    }

    // ====================================================================== списки

    private IEnumerable<OpenXmlCompositeElement> RenderList(DitaNode node, int level, int? numId, int ilvl, bool ordered)
    {
        var effectiveNumId = numId ?? AllocateNumbering(ordered);
        foreach (var item in node.ElementChildren())
        {
            if (!Include(item))
            {
                continue;
            }

            if (item.Name is "li" or "sli" or "choice" or "stepsection" or "step" or "substep")
            {
                foreach (var block in RenderListItem(item, level, effectiveNumId, ilvl))
                {
                    yield return block;
                }
            }
        }
    }

    private IEnumerable<OpenXmlCompositeElement> RenderListItem(DitaNode item, int level, int numId, int ilvl)
    {
        if (item.Name is "step" or "substep")
        {
            var cmd = item.FirstElement("cmd");
            var firstParagraph = true;
            foreach (var child in item.Children)
            {
                if (child.Kind != NodeKind.Element || !Include(child))
                {
                    continue;
                }

                if (ReferenceEquals(child, cmd))
                {
                    yield return NumberedParagraph(RenderInlineRuns(child), numId, ilvl);
                    firstParagraph = false;
                    continue;
                }

                if (child.Name is "substeps")
                {
                    foreach (var block in RenderList(child, level, numId: null, ilvl + 1, ordered: true))
                    {
                        yield return block;
                    }

                    continue;
                }

                foreach (var block in RenderBlock(child, level))
                {
                    yield return Indent(block, ilvl + 1);
                }
            }

            if (firstParagraph)
            {
                // <step> без <cmd> — по схеме невозможно, но на случай повреждённого документа
                yield return NumberedParagraph(new List<OpenXmlElement> { new W.Run() }, numId, ilvl);
            }

            yield break;
        }

        // li / sli / choice / stepsection — обычный пункт списка, возможно с вложенными блоками
        var nested = item.ElementChildren().Where(c => c.Name is "ul" or "ol" or "sl" or "choices").ToList();
        var directRuns = new List<OpenXmlElement>();
        foreach (var child in item.Children)
        {
            if (child.Kind == NodeKind.Text)
            {
                directRuns.Add(new W.Run(new W.Text(child.Value) { Space = SpaceProcessingModeValues.Preserve }));
                continue;
            }

            if (child.Kind != NodeKind.Element || !Include(child) || nested.Contains(child))
            {
                continue;
            }

            var def = _catalog.Get(child.Name);
            if (def is not null && def.Display is DisplayKind.Inline or DisplayKind.Empty)
            {
                directRuns.AddRange(RenderInlineRunsForNode(child));
            }
            else
            {
                // блочный ребёнок внутри li (редко) — переносим в отдельный абзац после
                directRuns.Add(new W.Run());
            }
        }

        yield return NumberedParagraph(directRuns, numId, ilvl);

        foreach (var child in item.Children)
        {
            if (child.Kind != NodeKind.Element || !Include(child))
            {
                continue;
            }

            if (nested.Contains(child))
            {
                var ordered = child.Name == "ol";
                foreach (var block in RenderList(child, 0, numId: null, ilvl + 1, ordered))
                {
                    yield return block;
                }

                continue;
            }

            var def = _catalog.Get(child.Name);
            if (def is not null && def.Display is DisplayKind.Inline or DisplayKind.Empty)
            {
                continue;
            }

            foreach (var block in RenderBlock(child, 0))
            {
                yield return Indent(block, ilvl + 1);
            }
        }
    }

    private static OpenXmlCompositeElement Indent(OpenXmlCompositeElement block, int ilvl)
    {
        if (block is W.Paragraph p)
        {
            EnsureParagraphProperties(p).Append(new W.Indentation { Left = ((ilvl + 1) * 360).ToString() });
        }

        return block;
    }

    private int AllocateNumbering(bool ordered)
    {
        var numId = _nextNumId++;
        _numberingPart.Numbering!.Append(new W.NumberingInstance(
            new W.AbstractNumId { Val = (int)(ordered ? DecimalAbstractNumId : BulletAbstractNumId) })
        { NumberID = numId });
        return numId;
    }

    private static W.Paragraph NumberedParagraph(List<OpenXmlElement> runs, int numId, int ilvl)
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.NumberingProperties(
            new W.NumberingLevelReference { Val = ilvl },
            new W.NumberingId { Val = numId })));
        paragraph.Append(runs);
        return paragraph;
    }

    // ====================================================================== инлайн

    private List<OpenXmlElement> RenderInlineRuns(DitaNode node)
    {
        var runs = new List<OpenXmlElement>();
        foreach (var child in node.Children)
        {
            runs.AddRange(RenderInlineRunsForNode(child));
        }

        return runs;
    }

    private IEnumerable<OpenXmlElement> RenderInlineRunsForNode(DitaNode node)
    {
        switch (node.Kind)
        {
            case NodeKind.Text:
                yield return new W.Run(new W.Text(node.Value) { Space = SpaceProcessingModeValues.Preserve });
                yield break;
            case NodeKind.Comment:
            case NodeKind.ProcessingInstruction:
                yield break;
        }

        if (!Include(node))
        {
            yield break;
        }

        switch (node.Name)
        {
            case "b":
                foreach (var r in Styled(node, bold: true)) yield return r;
                yield break;
            case "i":
                foreach (var r in Styled(node, italic: true)) yield return r;
                yield break;
            case "u":
                foreach (var r in Styled(node, underline: true)) yield return r;
                yield break;
            case "sup":
                foreach (var r in Styled(node, vertical: W.VerticalPositionValues.Superscript)) yield return r;
                yield break;
            case "sub":
                foreach (var r in Styled(node, vertical: W.VerticalPositionValues.Subscript)) yield return r;
                yield break;
            case "line-through":
                foreach (var r in Styled(node, strike: true)) yield return r;
                yield break;
            case "codeph":
            case "apiname":
            case "option":
            case "parmname":
            case "synph":
            case "cmdname":
            case "filepath":
            case "msgnum":
            case "msgph":
            case "systemoutput":
            case "userinput":
            case "varname":
            case "coderef":
                foreach (var r in Styled(node, monospace: true)) yield return r;
                yield break;
            case "uicontrol":
            case "wintitle":
                foreach (var r in Styled(node, bold: true)) yield return r;
                yield break;
            case "menucascade":
            {
                var parts = node.ElementChildren().Where(c => c.Name == "uicontrol").ToList();
                for (var i = 0; i < parts.Count; i++)
                {
                    if (i > 0)
                    {
                        yield return new W.Run(new W.Text(" → ") { Space = SpaceProcessingModeValues.Preserve });
                    }

                    foreach (var r in Styled(parts[i], bold: true))
                    {
                        yield return r;
                    }
                }

                yield break;
            }
            case "shortcut":
                foreach (var r in Styled(node, monospace: true)) yield return r;
                yield break;
            case "q":
                yield return new W.Run(new W.Text("«"));
                foreach (var r in RenderInlineRunsForChildren(node)) yield return r;
                yield return new W.Run(new W.Text("»"));
                yield break;
            case "image":
            case "glossSymbol":
            case "hazardsymbol":
                foreach (var r in RenderImage(node)) yield return r;
                yield break;
            case "xref":
            case "link":
                foreach (var r in RenderXref(node)) yield return r;
                yield break;
            case "fn":
                yield return RenderFootnote(node);
                yield break;
            case "indexterm":
            case "index-see":
            case "index-see-also":
            case "sort-as":
            case "draft-comment" when !_options.ShowDraftComments:
                yield break;
            case "keyword":
            case "term":
            case "text":
            {
                var keyText = KeyTextFor(node);
                if (keyText is not null && node.Children.Count == 0)
                {
                    yield return new W.Run(new W.Text(keyText) { Space = SpaceProcessingModeValues.Preserve });
                    yield break;
                }

                foreach (var r in RenderInlineRunsForChildren(node)) yield return r;
                yield break;
            }

            default:
            {
                var keyText = KeyTextFor(node);
                if (keyText is not null && node.Children.Count == 0)
                {
                    yield return new W.Run(new W.Text(keyText) { Space = SpaceProcessingModeValues.Preserve });
                    yield break;
                }

                foreach (var r in RenderInlineRunsForChildren(node)) yield return r;
                yield break;
            }
        }
    }

    private IEnumerable<OpenXmlElement> RenderInlineRunsForChildren(DitaNode node)
    {
        foreach (var child in node.Children)
        {
            foreach (var r in RenderInlineRunsForNode(child))
            {
                yield return r;
            }
        }
    }

    private IEnumerable<OpenXmlElement> Styled(DitaNode node, bool bold = false, bool italic = false,
        bool underline = false, bool strike = false, bool monospace = false,
        W.VerticalPositionValues? vertical = null)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == NodeKind.Text)
            {
                var props = new List<OpenXmlElement>();
                if (bold) props.Add(new W.Bold());
                if (italic) props.Add(new W.Italic());
                if (underline) props.Add(new W.Underline { Val = W.UnderlineValues.Single });
                if (strike) props.Add(new W.Strike());
                if (monospace) props.Add(new W.RunFonts { Ascii = "Consolas" });
                if (vertical is not null) props.Add(new W.VerticalTextAlignment { Val = vertical.Value });

                yield return props.Count > 0
                    ? new W.Run(new W.RunProperties(props.ToArray()), new W.Text(child.Value) { Space = SpaceProcessingModeValues.Preserve })
                    : new W.Run(new W.Text(child.Value) { Space = SpaceProcessingModeValues.Preserve });
            }
            else if (child.Kind == NodeKind.Element)
            {
                foreach (var r in RenderInlineRunsForNode(child))
                {
                    yield return r;
                }
            }
        }
    }

    private string? KeyTextFor(DitaNode node)
    {
        var keyref = node.GetAttribute("keyref");
        if (string.IsNullOrWhiteSpace(keyref))
        {
            return null;
        }

        return _project.ResolveKey(keyref!.Split('/')[0], _options.CurrentKeyScope)?.KeyText;
    }

    private IEnumerable<OpenXmlElement> RenderXref(DitaNode node)
    {
        var href = node.GetAttribute("href");
        var keyref = node.GetAttribute("keyref");
        string? bookmark = null;
        string? external = null;
        string? label = null;

        if (!string.IsNullOrWhiteSpace(keyref))
        {
            var keyDef = _project.ResolveKey(keyref!.Split('/')[0], _options.CurrentKeyScope);
            if (keyDef is not null)
            {
                label = keyDef.KeyText;
                if (keyDef.ResolvedPath is not null)
                {
                    bookmark = _options.TopicBookmark?.Invoke(keyDef.ResolvedPath, null);
                }
                else if (keyDef.Href is not null)
                {
                    external = keyDef.Href;
                }
            }
        }

        if (bookmark is null && external is null && !string.IsNullOrWhiteSpace(href))
        {
            if (RefResolver.IsExternal(href!) || node.GetAttribute("scope") is "external" or "peer")
            {
                external = href;
            }
            else if (_document.FilePath is not null)
            {
                var reference = RefResolver.Parse(_document.FilePath, href!);
                if (reference.Path is not null)
                {
                    bookmark = _options.TopicBookmark?.Invoke(reference.Path, reference.ElementId ?? reference.TopicId);
                    label ??= TitleOf(reference);
                }
            }
        }

        var innerRuns = RenderInlineRuns(node).ToList();
        var hasText = innerRuns.OfType<W.Run>().Any(r => r.GetFirstChild<W.Text>()?.Text.Length > 0);
        if (!hasText)
        {
            innerRuns = new List<OpenXmlElement> { new W.Run(new W.Text(label ?? external ?? href ?? string.Empty)) };
        }

        if (bookmark is not null)
        {
            yield return new W.Hyperlink(innerRuns.Select(r => (OpenXmlElement)r.CloneNode(true)).ToArray())
                { Anchor = bookmark, History = true };
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(external))
        {
            var relId = "hlink" + _nextImageId++;
            _mainPart.AddHyperlinkRelationship(new Uri(external!, UriKind.RelativeOrAbsolute), true, relId);
            yield return new W.Hyperlink(innerRuns.Select(r => (OpenXmlElement)r.CloneNode(true)).ToArray())
                { Id = relId, History = true };
            yield break;
        }

        foreach (var r in innerRuns)
        {
            yield return r;
        }
    }

    private string? TitleOf(DitaReference reference)
    {
        if (reference.Path is null || !File.Exists(reference.Path))
        {
            return null;
        }

        var doc = _project.TryGetDocument(reference.Path);
        if (doc is null)
        {
            return null;
        }

        if (reference.TopicId is null)
        {
            return doc.Title;
        }

        var topic = RefResolver.FindById(doc.Root, reference.TopicId);
        return topic?.FirstElement("title")?.InnerText.Trim() ?? doc.Title;
    }

    private IEnumerable<OpenXmlCompositeElement> RenderRelatedLinks(DitaNode node)
    {
        var links = node.DescendantsAndSelf()
            .Where(n => n.Kind == NodeKind.Element && n.Name == "link" && Include(n))
            .ToList();
        if (links.Count == 0)
        {
            yield break;
        }

        yield return StyledParagraph(new List<OpenXmlElement> { new W.Run(new W.Text(L.RelatedLinks)) }, bold: true);
        foreach (var link in links)
        {
            var runs = RenderXref(link).ToList();
            var linkParagraph = new W.Paragraph(new W.ParagraphProperties(new W.Indentation { Left = "227" }));
            linkParagraph.Append(runs);
            yield return linkParagraph;
        }
    }

    private W.Run RenderFootnote(DitaNode node)
    {
        var footnotesPart = _mainPart.FootnotesPart ?? _mainPart.AddNewPart<FootnotesPart>();
        EnsureFootnotesRoot(footnotesPart);

        var id = _nextFootnoteId++;
        var runs = RenderInlineRuns(node);
        var paragraph = new W.Paragraph(
            new W.Run(new W.RunProperties(new W.RunStyle { Val = "FootnoteReference" }), new W.FootnoteReferenceMark()),
            new W.Run(new W.Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
        foreach (var run in runs)
        {
            paragraph.Append(run);
        }

        footnotesPart.Footnotes!.Append(new W.Footnote(paragraph) { Id = id });

        return new W.Run(new W.RunProperties(new W.RunStyle { Val = "FootnoteReference" }), new W.FootnoteReference { Id = id });
    }

    private static void EnsureFootnotesRoot(FootnotesPart part)
    {
        if (part.Footnotes is not null)
        {
            return;
        }

        part.Footnotes = new W.Footnotes(
            new W.Footnote(new W.Paragraph(new W.Run(new W.SeparatorMark()))) { Type = W.FootnoteEndnoteValues.Separator, Id = -1 },
            new W.Footnote(new W.Paragraph(new W.Run(new W.ContinuationSeparatorMark()))) { Type = W.FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
    }

    private IEnumerable<OpenXmlElement> RenderImage(DitaNode node)
    {
        var href = node.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            var keyDef = node.GetAttribute("keyref") is { } k ? _project.ResolveKey(k, _options.CurrentKeyScope) : null;
            href = keyDef?.Href;
        }

        if (string.IsNullOrWhiteSpace(href) || RefResolver.IsExternal(href!) || _document.FilePath is null)
        {
            yield break;
        }

        var absolute = RefResolver.ResolvePath(_document.FilePath, href!);
        if (absolute is null || !File.Exists(absolute))
        {
            _options.Warnings.Add($"Изображение не найдено: {href}");
            yield break;
        }

        var alt = node.GetAttribute("alt") ?? node.FirstElement("alt")?.InnerText ?? string.Empty;
        var extension = Path.GetExtension(absolute).ToLowerInvariant();
        var supported = extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff";

        if (!supported)
        {
            _options.Warnings.Add($"Формат изображения не поддерживается в DOCX (показан как ссылка): {href}");
            yield return new W.Run(new W.RunProperties(new W.Italic(), new W.Color { Val = "808080" }),
                new W.Text($"[изображение: {Path.GetFileName(absolute)}{(string.IsNullOrEmpty(alt) ? string.Empty : " — " + alt)}]"));
            yield break;
        }

        var imagePart = extension switch
        {
            ".png" => _mainPart.AddImagePart(ImagePartType.Png),
            ".jpg" or ".jpeg" => _mainPart.AddImagePart(ImagePartType.Jpeg),
            ".gif" => _mainPart.AddImagePart(ImagePartType.Gif),
            ".bmp" => _mainPart.AddImagePart(ImagePartType.Bmp),
            _ => _mainPart.AddImagePart(ImagePartType.Tiff)
        };
        using (var stream = File.OpenRead(absolute))
        {
            imagePart.FeedData(stream);
        }

        var relId = _mainPart.GetIdOfPart(imagePart);
        var (widthEmu, heightEmu) = ImageSize.ReadEmuSize(absolute, node.GetAttribute("width"), node.GetAttribute("height"));
        var imageId = _nextImageId++;

        yield return new W.Run(new Drawing.Wordprocessing.Inline(
            new Drawing.Wordprocessing.Extent { Cx = widthEmu, Cy = heightEmu },
            new Drawing.Wordprocessing.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new Drawing.Wordprocessing.DocProperties { Id = (uint)imageId, Name = "image" + imageId, Description = alt },
            new Drawing.Graphic(
                new Drawing.GraphicData(
                    new Pic.Picture(
                        new Pic.NonVisualPictureProperties(
                            new Pic.NonVisualDrawingProperties { Id = (uint)imageId, Name = Path.GetFileName(absolute) },
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
        });
    }

    // ====================================================================== вспомогательное

    private static bool HasOutputClass(DitaNode node, string token) =>
        (node.GetAttribute("outputclass") ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Contains(token, StringComparer.Ordinal);

    /// <summary>Полоса на полях у абзаца с непустым атрибутом rev — штатная DITA-пометка
    /// изменений (не полноценный track changes с историей правок), см. HtmlRenderer.BuildClassAttr.</summary>
    private static OpenXmlCompositeElement WithOutputClass(W.Paragraph paragraph, DitaNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.GetAttribute("rev")))
        {
            EnsureParagraphProperties(paragraph).Append(new W.ParagraphBorders(
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 18, Color = "D4380D", Space = 4 }));
        }

        return paragraph;
    }

    private static W.Paragraph Paragraph(List<OpenXmlElement> runs) => new(runs.ToArray());

    private static W.Paragraph StyledParagraph(List<OpenXmlElement> runs, bool bold = false, bool italic = false,
        bool indent = false)
    {
        if (!bold && !italic)
        {
            var p = new W.Paragraph(runs.ToArray());
            if (indent)
            {
                EnsureParagraphProperties(p).Append(new W.Indentation { Left = "227" });
            }

            return p;
        }

        foreach (var run in runs.OfType<W.Run>())
        {
            var rPr = run.RunProperties ??= new W.RunProperties();
            if (bold)
            {
                rPr.Append(new W.Bold());
            }

            if (italic)
            {
                rPr.Append(new W.Italic());
            }
        }

        var paragraph = new W.Paragraph(runs.ToArray());
        if (indent)
        {
            EnsureParagraphProperties(paragraph).Append(new W.Indentation { Left = "227" });
        }

        return paragraph;
    }

    private static W.Paragraph HeadingParagraph(List<OpenXmlElement> runs, int level)
    {
        var style = "Heading" + Math.Clamp(level, 1, 6);
        var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }));
        paragraph.Append(runs);
        return paragraph;
    }

    private static W.ParagraphProperties EnsureParagraphProperties(W.Paragraph paragraph) =>
        paragraph.ParagraphProperties ??= new W.ParagraphProperties();

    private void PrependBookmark(W.Paragraph paragraph, string bookmarkName)
    {
        var id = _nextBookmarkId++;
        paragraph.InsertAt(new W.BookmarkStart { Id = id.ToString(), Name = bookmarkName }, 0);
        paragraph.InsertAt(new W.BookmarkEnd { Id = id.ToString() }, 1);
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
