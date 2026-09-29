using System.Globalization;
using System.Text;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Publishing;

// Рендер HTML: таблицы CALS и simpletable.
public sealed partial class HtmlRenderer
{
    private string RenderTable(DitaNode node, int level)
    {
        var sb = new StringBuilder();
        var title = node.FirstElement("title");
        if (CaptionRules.HasContent(title))
        {
            _tableNumber++;
            var caption = _options.NumberFiguresAndTables
                ? $"{L.Table} {_tableNumber}{CaptionRules.Separator(_options.CaptionSeparator)}{RenderInlineChildren(title!)}"
                : RenderInlineChildren(title!);
            sb.Append("<div class=\"table-title\">").Append(caption).Append("</div>\n");
        }

        foreach (var tgroup in node.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            sb.Append(RenderTgroup(tgroup, level, node));
        }

        return sb.ToString();
    }

    private string RenderTgroup(DitaNode tgroup, int level, DitaNode table)
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

        var sb = new StringBuilder("<table").Append(OptionalClassAttr(table)).Append(">\n");
        if (colspecs.Count > 0)
        {
            // Ширины столбцов — доли таблицы (colwidth «N*» и абсолютные — пропорционально).
            sb.Append(ColGroup(TableLayout.CalsFractions(tgroup), colspecs.Count));
        }

        var thead = tgroup.FirstElement("thead");
        if (thead is not null)
        {
            sb.Append("<thead>\n");
            foreach (var row in thead.ElementChildren().Where(e => e.Name == "row"))
            {
                sb.Append(RenderRow(row, colNames, "th", level));
            }

            sb.Append("</thead>\n");
        }

        var tbody = tgroup.FirstElement("tbody");
        if (tbody is not null)
        {
            sb.Append("<tbody>\n");
            foreach (var row in tbody.ElementChildren().Where(e => e.Name == "row"))
            {
                sb.Append(RenderRow(row, colNames, "td", level));
            }

            sb.Append("</tbody>\n");
        }

        sb.Append("</table>\n");
        return sb.ToString();
    }

    /// <summary>colgroup с шириной каждого столбца в процентах; без ширин — пустые col.</summary>
    private static string ColGroup(double[]? fractions, int columns)
    {
        var sb = new StringBuilder("<colgroup>\n");
        for (var i = 0; i < columns; i++)
        {
            sb.Append("<col");
            if (fractions is not null && i < fractions.Length)
            {
                sb.Append(" style=\"width:").Append((fractions[i] * 100).ToString("0.##", CultureInfo.InvariantCulture)).Append("%\"");
            }

            sb.Append(" />\n");
        }

        return sb.Append("</colgroup>\n").ToString();
    }

    /// <summary>Открывающий tr: минимальная высота строки из row-height-Nmm.</summary>
    private static string RowStart(DitaNode row) =>
        TableLayout.RowHeightMm(row) is { } mm
            ? $"<tr style=\"height:{mm.ToString("0.#", CultureInfo.InvariantCulture)}mm\">\n"
            : "<tr>\n";

    private string RenderRow(DitaNode row, Dictionary<string, int> colNames, string cellTag, int level)
    {
        var sb = new StringBuilder(RowStart(row));
        foreach (var entry in row.ElementChildren().Where(e => e.Name == "entry"))
        {
            sb.Append('<').Append(cellTag).Append(OutputClassAttr(entry));

            var namest = entry.GetAttribute("namest");
            var nameend = entry.GetAttribute("nameend");
            if (namest is not null && nameend is not null &&
                colNames.TryGetValue(namest, out var start) && colNames.TryGetValue(nameend, out var end))
            {
                var span = end - start + 1;
                if (span > 1)
                {
                    sb.Append(" colspan=\"").Append(span).Append('"');
                }
            }

            var morerows = entry.GetAttribute("morerows");
            if (int.TryParse(morerows, out var more) && more > 0)
            {
                sb.Append(" rowspan=\"").Append(more + 1).Append('"');
            }

            var align = entry.GetAttribute("align");
            var valign = entry.GetAttribute("valign");
            if (align is not null || valign is not null)
            {
                sb.Append(" style=\"");
                if (align is not null)
                {
                    sb.Append("text-align:").Append(Escape(align)).Append(';');
                }

                if (valign is not null)
                {
                    sb.Append("vertical-align:").Append(Escape(valign)).Append(';');
                }

                sb.Append('"');
            }

            sb.Append('>').Append(RenderInlineChildren(entry)).Append("</").Append(cellTag).Append(">\n");
        }

        sb.Append("</tr>\n");
        return sb.ToString();
    }

    private string RenderSimpleTable(DitaNode node, int level)
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

        var sb = new StringBuilder();
        var spectitle = node.GetAttribute("spectitle");
        if (!string.IsNullOrWhiteSpace(spectitle))
        {
            sb.Append("<div class=\"table-title\">").Append(Escape(spectitle!)).Append("</div>\n");
        }

        sb.Append("<table class=\"").Append(node.Name).Append("\">\n");
        var columns = node.ElementChildren().Where(e => e.Name is "sthead" or "strow" or "prophead" or "property" or "chhead" or "chrow")
            .Select(r => r.ElementChildren().Count()).DefaultIfEmpty(0).Max();
        if (TableLayout.SimpleFractions(node, columns) is { } fractions)
        {
            sb.Append(ColGroup(fractions, columns));
        }

        var head = node.ElementChildren().FirstOrDefault(e => e.Name is "sthead" or "prophead" or "chhead");
        if (head is not null)
        {
            sb.Append("<thead>\n<tr>\n");
            foreach (var cell in head.ElementChildren().Where(c => headNames.Contains(c.Name)))
            {
                sb.Append("<th").Append(OutputClassAttr(cell)).Append('>').Append(RenderInlineChildren(cell)).Append("</th>\n");
            }

            sb.Append("</tr>\n</thead>\n");
        }
        else if (node.Name is "properties" or "choicetable")
        {
            sb.Append("<thead>\n<tr>");
            if (node.Name == "properties")
            {
                sb.Append("<th>").Append(Escape(L.Type)).Append("</th><th>")
                  .Append(Escape(L.Value)).Append("</th><th>")
                  .Append(Escape(L.Description)).Append("</th>");
            }
            else
            {
                sb.Append("<th>").Append(Escape(L.Options)).Append("</th><th>")
                  .Append(Escape(L.Description)).Append("</th>");
            }

            sb.Append("</tr>\n</thead>\n");
        }

        sb.Append("<tbody>\n");
        foreach (var row in node.ElementChildren().Where(e => e.Name is "strow" or "property" or "chrow"))
        {
            sb.Append(RowStart(row));
            foreach (var cell in row.ElementChildren().Where(c => rowNames.Contains(c.Name)))
            {
                sb.Append("<td").Append(OutputClassAttr(cell)).Append('>').Append(RenderInlineChildren(cell)).Append("</td>\n");
            }

            sb.Append("</tr>\n");
        }

        sb.Append("</tbody>\n</table>\n");
        return sb.ToString();
    }
}
