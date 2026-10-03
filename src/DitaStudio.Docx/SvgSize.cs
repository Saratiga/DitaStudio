using System.Globalization;
using System.Xml.Linq;

namespace DitaStudio.Docx;

/// <summary>Размер SVG-файла в пикселях (96 dpi): <c>width</c>/<c>height</c> корня, а если их нет или они в процентах — <c>viewBox</c>.</summary>
internal static class SvgSize
{
    public static (int Width, int Height) Read(string path)
    {
        try
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = System.Xml.XmlReader.Create(path, settings);
            var root = XDocument.Load(reader).Root;
            if (root is null || root.Name.LocalName != "svg")
            {
                return (0, 0);
            }

            var width = ToPixels(root.Attribute("width")?.Value);
            var height = ToPixels(root.Attribute("height")?.Value);
            var box = ViewBox(root.Attribute("viewBox")?.Value);
            if (width is null && box is not null)
            {
                width = height is not null && box.Value.H > 0 ? height * box.Value.W / box.Value.H : box.Value.W;
            }

            if (height is null && box is not null)
            {
                height = width is not null && box.Value.W > 0 ? width * box.Value.H / box.Value.W : box.Value.H;
            }

            return width is > 0 && height is > 0 ? ((int)Math.Round(width.Value), (int)Math.Round(height.Value)) : (0, 0);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    private static (double W, double H)? ViewBox(string? value)
    {
        var parts = (value ?? string.Empty).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 &&
               double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
               double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && w > 0 && h > 0
            ? (w, h)
            : null;
    }

    // Проценты и неизвестные единицы размера не дают — их заменяет viewBox.
    private static double? ToPixels(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().ToLowerInvariant();
        var end = text.TakeWhile(c => char.IsDigit(c) || c == '.').Count();
        if (end == 0 || !double.TryParse(text[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        return text[end..] switch
        {
            "" or "px" => number,
            "pt" => number * 96 / 72,
            "pc" => number * 16,
            "in" => number * 96,
            "cm" => number * 96 / 2.54,
            "mm" => number * 96 / 25.4,
            _ => null
        };
    }
}
