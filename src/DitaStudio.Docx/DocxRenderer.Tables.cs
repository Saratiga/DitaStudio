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
        if (title is not null)
        {
            _tableNumber++;
            var captionRuns = new List<OpenXmlElement>();
            if (_options.NumberFiguresAndTables)
            {
                captionRuns.Add(new W.Run(new W.Text($"{L.Table} {_tableNumber}. ") { Space = SpaceProcessingModeValues.Preserve }));
            }

            captionRuns.AddRange(RenderInlineRuns(title));
            yield return Para(DocxStyleCatalog.TableCaption, captionRuns);
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
        table.Append(TableProperties());

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

        var paragraph = Para(isHeader ? DocxStyleCatalog.TableHeading : DocxStyleCatalog.TableText, runs);
        foreach (var property in paragraphProps)
        {
            EnsureParagraphProperties(paragraph).Append(property);
        }

        return new W.TableCell(props, paragraph);
    }

    private static W.TableCell ContinuationCell(int span)
    {
        // Порядок в tcPr задан схемой: gridSpan раньше vMerge.
        var props = new W.TableCellProperties();
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
                tr.Append(Cell(RenderInlineRuns(cell), isHeader: true));
            }

            table.Append(tr);
        }
        else if (headLabels is not null)
        {
            var tr = new W.TableRow(new W.TableRowProperties(new W.TableHeader()));
            foreach (var label in headLabels)
            {
                tr.Append(Cell(new OpenXmlElement[] { new W.Run(new W.RunProperties(new W.Bold()), new W.Text(label)) }, isHeader: true));
            }

            table.Append(tr);
        }

        foreach (var row in rows)
        {
            var tr = new W.TableRow();
            foreach (var cell in row.ElementChildren().Where(c => rowNames.Contains(c.Name)))
            {
                tr.Append(Cell(RenderInlineRuns(cell), isHeader: false));
            }

            table.Append(tr);
        }

        return table;
    }
}
