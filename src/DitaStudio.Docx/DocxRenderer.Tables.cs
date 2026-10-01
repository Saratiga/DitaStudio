using System.Globalization;
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

// DOCX: таблицы CALS и simpletable, объединение ячеек.
public sealed partial class DocxRenderer
{
    private IEnumerable<OpenXmlCompositeElement> RenderTable(DitaNode node)
    {
        var title = node.FirstElement("title");
        if (CaptionRules.HasCaption(title))
        {
            _tableNumber++;
            var hasText = CaptionRules.HasContent(title);
            var captionRuns = new List<OpenXmlElement>();
            if (_options.NumberFiguresAndTables)
            {
                captionRuns.AddRange(CaptionLabel(L.Table, _tableNumber, hasText));
            }

            if (hasText)
            {
                captionRuns.AddRange(RenderInlineRuns(title!));
            }

            if (captionRuns.Count > 0)
            {
                yield return Para(DocxStyleCatalog.TableCaption, captionRuns);
            }
        }

        var allowSplit = HasOutputClass(node, "page-break-auto");
        var outerBorders = _cellBorders;
        _cellBorders = CalsBorders.IsCustom(node) ? CalsBorders.Compute(node) : null;
        foreach (var tgroup in node.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            yield return RenderTgroup(tgroup, allowSplit);
            yield return new W.Paragraph(); // Word требует абзац после таблицы, иначе следующий блок "прилипает"
        }

        _cellBorders = outerBorders;
    }

    // Границы ячеек таблицы, у которой линии отключены (frame, rowsep, colsep); null — обычная таблица.
    private Dictionary<DitaNode, CellBorders>? _cellBorders;

    /// <summary>Границы ячейки для <c>tcBorders</c>: где линия есть — как в оформлении (внешняя рамка или внутренняя), где нет — nil.</summary>
    private W.TableCellBorders? CellBordersFor(DitaNode entry, int col, int span, int numCols)
    {
        if (_cellBorders is null || !_cellBorders.TryGetValue(entry, out var b))
        {
            return null;
        }

        var look = Sheet.Table;
        var none = new DocxBorder("none", 0, "auto");
        return new W.TableCellBorders(
            DocxPropsWriter.Border(new W.TopBorder(), b.Top ? look.Inner : none),
            DocxPropsWriter.Border(new W.LeftBorder(), b.Left ? look.Inner : none),
            DocxPropsWriter.Border(new W.BottomBorder(), b.Bottom ? look.Inner : none),
            DocxPropsWriter.Border(new W.RightBorder(), b.Right ? look.Inner : none));
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
        table.Append(TableProperties());
        var outerFractions = _columnFractions; // вложенная таблица в ячейке не должна сбить внешнюю
        _columnFractions = TableLayout.CalsFractions(tgroup) is { } fractions && fractions.Length == numCols ? fractions : null;
        table.Append(Grid(numCols));

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

        _columnFractions = outerFractions;
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
        if (RowProperties(row, isHeader, cantSplit: !isHeader && !allowRowSplit) is { } rowProperties)
        {
            tr.Append(rowProperties);
        }

        foreach (var placement in placements)
        {
            if (placement.Entry is null)
            {
                tr.Append(ContinuationCell(placement.Col, placement.Span));
            }
            else
            {
                var isSpanStart = newSpans.Any(sp => ReferenceEquals(sp.Entry, placement.Entry));
                tr.Append(RealCell(placement.Entry, placement.Col, placement.Span, isSpanStart, isHeader));
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

    private W.TableCell RealCell(DitaNode entry, int col, int span, bool isSpanStart, bool isHeader)
    {
        var props = new W.TableCellProperties();
        if (CellWidth(col, span) is { } width)
        {
            props.Append(width);
        }

        if (span > 1)
        {
            props.Append(new W.GridSpan { Val = span });
        }

        if (isSpanStart)
        {
            props.Append(new W.VerticalMerge { Val = W.MergedCellValues.Restart });
        }

        if (CellBordersFor(entry, col, span, 0) is { } cellBorders)
        {
            props.TableCellBorders = cellBorders;
        }

        if (CellShading(isHeader) is { } shading)
        {
            props.Append(shading);
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

        var blocks = CellBlocks(entry, isHeader);
        if (entry.GetAttribute("align") is { } align)
        {
            var justification = align switch
            {
                "center" => W.JustificationValues.Center,
                "right" => W.JustificationValues.Right,
                "justify" => W.JustificationValues.Both,
                _ => W.JustificationValues.Left
            };
            foreach (var paragraph in blocks.OfType<W.Paragraph>())
            {
                EnsureParagraphProperties(paragraph).Justification = new W.Justification { Val = justification };
            }
        }

        var cell = new W.TableCell(props);
        cell.Append(blocks);
        return cell;
    }

    /// <summary>
    /// Содержимое ячейки таблицы. Только текст и фразовые элементы — один абзац стиля «текст таблицы» (или «заголовок таблицы» в
    /// шапке). Если внутри есть блоки (<c>p</c>, <c>ul</c>, <c>note</c>…), каждый становится своим абзацем или списком, как вне таблицы:
    /// текст, идущий до блока и между блоками, — абзацем стиля ячейки. Раньше всё вливалось в один абзац сплошным текстом. Последним
    /// в ячейке по схеме OOXML всегда стоит абзац.
    /// </summary>
    private List<OpenXmlCompositeElement> CellBlocks(DitaNode cell, bool isHeader)
    {
        var textStyle = isHeader ? DocxStyleCatalog.TableHeading : DocxStyleCatalog.TableText;
        var result = new List<OpenXmlCompositeElement>();
        using (BlockScope(cell))
        {
            if (!cell.ElementChildren().Any(c => Include(c) && !IsInlineElement(c)))
            {
                result.Add(Para(textStyle, RenderInlineRuns(cell)));
                return result;
            }

            result.AddRange(RenderMixedChildren(cell, 5));
        }

        foreach (var paragraph in result.OfType<W.Paragraph>())
        {
            if (paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value == DocxStyleCatalog.BodyText)
            {
                paragraph.ParagraphProperties.ParagraphStyleId.Val = textStyle;
            }
        }

        if (result.Count == 0 || result[^1] is not W.Paragraph)
        {
            result.Add(new W.Paragraph());
        }

        return result;
    }

    private W.TableCell ContinuationCell(int col, int span)
    {
        // Порядок в tcPr задан схемой: tcW, gridSpan, vMerge.
        var props = new W.TableCellProperties();
        if (CellWidth(col, span) is { } width)
        {
            props.Append(width);
        }

        if (span > 1)
        {
            props.Append(new W.GridSpan { Val = span });
        }

        props.Append(new W.VerticalMerge { Val = W.MergedCellValues.Continue });

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
        table.Append(TableProperties());
        var outerFractions = _columnFractions;
        _columnFractions = TableLayout.SimpleFractions(node, numCols) is { } fractions && fractions.Length == numCols ? fractions : null;
        table.Append(Grid(numCols));

        var head = node.ElementChildren().FirstOrDefault(e => e.Name is "sthead" or "prophead" or "chhead");
        List<string>? headLabels = node.Name switch
        {
            "properties" => new List<string> { L.Type, L.Value, L.Description },
            "choicetable" => new List<string> { L.Options, L.Description },
            _ => null
        };

        if (head is not null)
        {
            var tr = new W.TableRow(RowProperties(head, isHeader: true, cantSplit: false)!);
            var column = 0;
            foreach (var cell in head.ElementChildren().Where(c => headNames.Contains(c.Name)))
            {
                tr.Append(Cell(CellBlocks(cell, isHeader: true), isHeader: true, column++));
            }

            table.Append(tr);
        }
        else if (headLabels is not null)
        {
            var tr = new W.TableRow(new W.TableRowProperties(new W.TableHeader()));
            var column = 0;
            foreach (var label in headLabels)
            {
                tr.Append(Cell(new OpenXmlElement[] { new W.Run(new W.RunProperties(new W.Bold()), new W.Text(label)) }, isHeader: true, column++));
            }

            table.Append(tr);
        }

        foreach (var row in rows)
        {
            var tr = new W.TableRow();
            if (RowProperties(row, isHeader: false, cantSplit: false) is { } rowProperties)
            {
                tr.Append(rowProperties);
            }

            var column = 0;
            foreach (var cell in row.ElementChildren().Where(c => rowNames.Contains(c.Name)))
            {
                tr.Append(Cell(CellBlocks(cell, isHeader: false), isHeader: false, column++));
            }

            table.Append(tr);
        }

        _columnFractions = outerFractions;
        return table;
    }

    // Доли ширины столбцов текущей таблицы (из colwidth / relcolwidth) — null, если не заданы.
    private double[]? _columnFractions;

    /// <summary>Сетка таблицы: при заданных долях — ширины столбцов от типовой ширины текста A4.</summary>
    private W.TableGrid Grid(int columns)
    {
        const double textWidthTwips = 9638; // 170 мм — таблица всё равно растягивается на 100 %
        var grid = new W.TableGrid();
        for (var i = 0; i < columns; i++)
        {
            grid.Append(_columnFractions is { } fractions
                ? new W.GridColumn { Width = ((int)Math.Round(fractions[i] * textWidthTwips)).ToString(CultureInfo.InvariantCulture) }
                : new W.GridColumn());
        }

        return grid;
    }

    /// <summary>Ширина ячейки — доля ширины таблицы (в пятидесятых долях процента, как требует OOXML).</summary>
    private W.TableCellWidth? CellWidth(int col, int span)
    {
        if (_columnFractions is not { } fractions || col < 0 || col >= fractions.Length)
        {
            return null;
        }

        var share = fractions.Skip(col).Take(Math.Max(1, span)).Sum();
        return new W.TableCellWidth { Type = W.TableWidthUnitValues.Pct, Width = ((int)Math.Round(share * 5000)).ToString(CultureInfo.InvariantCulture) };
    }

    /// <summary>Свойства строки в порядке схемы: cantSplit, trHeight (row-height-Nmm), tblHeader.</summary>
    private static W.TableRowProperties? RowProperties(DitaNode? row, bool isHeader, bool cantSplit)
    {
        var children = new List<OpenXmlElement>();
        if (cantSplit)
        {
            children.Add(new W.CantSplit());
        }

        if (row is not null && TableLayout.RowHeightMm(row) is { } mm)
        {
            children.Add(new W.TableRowHeight { Val = (uint)Math.Round(mm * 1440 / 25.4), HeightType = W.HeightRuleValues.AtLeast });
        }

        if (isHeader)
        {
            children.Add(new W.TableHeader());
        }

        return children.Count == 0 ? null : new W.TableRowProperties(children);
    }
}
