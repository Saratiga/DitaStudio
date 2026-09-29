using System.Globalization;
using System.Net;
using System.Text;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Колонтитулы CSS (<see cref="PageMarginBoxes"/>) для печати PDF: встроенный Chromium версии 120 полей страницы
/// <c>@page</c> не выводит, зато принимает HTML-шаблоны верхнего и нижнего колонтитулов — с полями
/// <c>pageNumber</c>, <c>totalPages</c> и <c>title</c>.
/// </summary>
public static class PdfMarginTemplates
{
    /// <summary>Высота области колонтитула, мм: поле минус отступ от края (12,5 мм, но не больше половины поля) и зазор 1 мм.</summary>
    public static double AreaHeightMm(double marginMm)
    {
        var distance = Math.Min(12.5, Math.Max(0, marginMm) / 2);
        return Math.Max(3, marginMm - distance - 1);
    }

    /// <summary>
    /// Шаблон колонтитула из полей CSS: три места — слева, по центру, справа. <paramref name="imageDataUri"/>
    /// превращает путь из <c>url(...)</c> в data-URI (null — картинки нет); <paramref name="sideMm"/> — отступ
    /// от левого и правого края листа, <paramref name="areaMm"/> — высота картинки по умолчанию.
    /// </summary>
    public static string Html(IEnumerable<MarginBox> boxes, Func<string, string?> imageDataUri, double sideMm, double areaMm)
    {
        var list = boxes.ToList();
        var style = new StringBuilder("font-family:'Segoe UI',Arial,sans-serif;font-size:9px;color:#555;width:100%;-webkit-print-color-adjust:exact;")
            .Append("margin:0 ").Append(Num(sideMm)).Append("mm;display:flex;align-items:center;");
        if (list.FirstOrDefault(b => b.Rule is not null)?.Rule is { } rule)
        {
            style.Append(list[0].IsTop ? "border-bottom:" : "border-top:")
                .Append(Num(rule.WidthPt)).Append("pt solid #").Append(rule.Color).Append(";padding-").Append(list[0].IsTop ? "bottom" : "top").Append(":1mm;");
        }

        var html = new StringBuilder("<div style=\"").Append(style).Append("\">");
        foreach (var (slot, align) in new[] { (0, "left"), (1, "center"), (2, "right") })
        {
            html.Append("<div style=\"flex:1;text-align:").Append(align).Append("\">");
            foreach (var box in list.Where(b => (int)b.Position % 3 == slot))
            {
                foreach (var item in box.Content)
                {
                    html.Append(Item(box, item, imageDataUri, areaMm));
                }
            }

            html.Append("</div>");
        }

        return html.Append("</div>").ToString();
    }

    private static string Item(MarginBox box, MarginContent item, Func<string, string?> imageDataUri, double areaMm)
    {
        if (item is MarginContent.Image image)
        {
            return imageDataUri(image.Path) is { } uri
                ? $"<img src=\"{uri}\" style=\"height:{Num(box.HeightMm ?? areaMm)}mm;vertical-align:middle\">"
                : string.Empty;
        }

        var inner = item switch
        {
            MarginContent.Text text => WebUtility.HtmlEncode(text.Value),
            MarginContent.PageNumber => "<span class=\"pageNumber\"></span>",
            MarginContent.PageCount => "<span class=\"totalPages\"></span>",
            MarginContent.Title => "<span class=\"title\"></span>",
            _ => string.Empty
        };

        var css = new StringBuilder();
        if (box.FontFamily is { Length: > 0 } family)
        {
            css.Append("font-family:'").Append(family.Replace("'", string.Empty)).Append("',sans-serif;");
        }

        if (box.FontSizePt is { } size)
        {
            css.Append("font-size:").Append(Num(size)).Append("pt;");
        }

        if (box.Bold)
        {
            css.Append("font-weight:bold;");
        }

        if (box.Italic)
        {
            css.Append("font-style:italic;");
        }

        if (box.Color is { } color)
        {
            css.Append("color:#").Append(color).Append(';');
        }

        return css.Length == 0 ? inner : $"<span style=\"{css}\">{inner}</span>";
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
