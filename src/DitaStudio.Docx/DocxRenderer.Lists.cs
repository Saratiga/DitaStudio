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

// DOCX: списки и шаги, нумерация.
public sealed partial class DocxRenderer
{
    private IEnumerable<OpenXmlCompositeElement> RenderList(DitaNode node, int level, int? numId, int ilvl, bool ordered)
    {
        var effectiveNumId = numId ?? AllocateNumbering(ordered, ordered ? null : MarkerFor(node));
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

    private IEnumerable<OpenXmlCompositeElement> RenderListItem(DitaNode item, int level, int numId, int ilvl) =>
        item.Name is "step" or "substep"
            ? RenderStepListItem(item, level, numId, ilvl)
            : RenderPlainListItem(item, numId, ilvl);

    private IEnumerable<OpenXmlCompositeElement> RenderStepListItem(DitaNode item, int level, int numId, int ilvl)
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
                yield return NumberedParagraph(RenderInlineRuns(child), numId, ilvl, DocxStyleCatalog.StepCommand);
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
    }

    // li / sli / choice / stepsection — обычный пункт списка, возможно с вложенными блоками
    private IEnumerable<OpenXmlCompositeElement> RenderPlainListItem(DitaNode item, int numId, int ilvl)
    {
        var nested = item.ElementChildren().Where(c => c.Name is "ul" or "ol" or "sl" or "choices").ToList();
        var directRuns = new List<OpenXmlElement>();
        foreach (var child in item.Children)
        {
            if (child.Kind == NodeKind.Text)
            {
                directRuns.Add(new W.Run(new W.Text(CollapseSpaces(child.Value)) { Space = SpaceProcessingModeValues.Preserve }));
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

    /// <summary>Свой маркер списка по классу (outputclass или имя элемента: sl, choices) из CSS.</summary>
    private DocxListMarker? MarkerFor(DitaNode list)
    {
        var classes = (list.GetAttribute("outputclass") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Append(list.Name)
            .ToList();
        return Sheet.MarkerForClasses(classes);
    }

    private int AllocateNumbering(bool ordered, DocxListMarker? customMarker = null)
    {
        var abstractId = ordered ? DecimalAbstractNumId
            : customMarker is null ? BulletAbstractNumId
            : CustomBulletAbstract(customMarker);

        var numId = _nextNumId++;
        var instance = new W.NumberingInstance(new W.AbstractNumId { Val = abstractId }) { NumberID = numId };

        // Все нумерованные списки ссылаются на одно abstractNum — без явного сброса Word
        // продолжает счёт с предыдущего списка (шаги второй задачи начинались бы с 8).
        if (ordered)
        {
            for (var level = 0; level < 9; level++)
            {
                instance.Append(new W.LevelOverride(new W.StartOverrideNumberingValue { Val = 1 }) { LevelIndex = level });
            }
        }

        _numberingPart.Numbering!.Append(instance);
        return numId;
    }

    /// <summary>abstractNum со своим маркером на всех уровнях — один на каждый маркер. По схеме
    /// все abstractNum обязаны идти раньше любого num, поэтому вставляется перед первым num.</summary>
    private int CustomBulletAbstract(DocxListMarker marker)
    {
        if (_customBulletAbstracts.TryGetValue(marker, out var existing))
        {
            return existing;
        }

        var id = _nextCustomAbstractId++;
        var abstractNum = DocxNumbering.Bullet(id, _ => marker, Sheet.BulletColor);
        var numbering = _numberingPart.Numbering!;
        var firstInstance = numbering.Elements<W.NumberingInstance>().FirstOrDefault();
        if (firstInstance is null)
        {
            numbering.Append(abstractNum);
        }
        else
        {
            numbering.InsertBefore(abstractNum, firstInstance);
        }

        _customBulletAbstracts[marker] = id;
        return id;
    }

    private W.Paragraph NumberedParagraph(List<OpenXmlElement> runs, int numId, int ilvl,
        string style = DocxStyleCatalog.ListItem)
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = ParagraphStyle(style) },
            new W.NumberingProperties(
                new W.NumberingLevelReference { Val = ilvl },
                new W.NumberingId { Val = numId })));
        paragraph.Append(runs);
        TrimEdges(paragraph);
        return paragraph;
    }
}
