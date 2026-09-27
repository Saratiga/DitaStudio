using System.Globalization;

namespace DitaStudio.Docx.Styling;

/// <summary>Длина CSS: число и единица; перевод в пункты — с базой для em/%.</summary>
public readonly record struct CssLength(double Value, string Unit)
{
    /// <summary>Пункты (1/72 дюйма). emBasePt — размер шрифта, относительно которого считаются em и %.</summary>
    public double ToPoints(double emBasePt) => Unit switch
    {
        "px" => Value * 0.75,
        "pt" => Value,
        "pc" => Value * 12,
        "in" => Value * 72,
        "cm" => Value * 72 / 2.54,
        "mm" => Value * 72 / 25.4,
        "q" => Value * 72 / 101.6,
        "em" => Value * emBasePt,
        "rem" => Value * CssValues.RootFontSizePt,
        "%" => Value * emBasePt / 100,
        "ex" or "ch" => Value * emBasePt / 2,
        _ => Value * 0.75 // число без единицы (допустимо только для 0) — как px
    };
}

public static class CssValues
{
    /// <summary>Размер шрифта корневого элемента в браузере по умолчанию (16px) — база для rem.</summary>
    public const double RootFontSizePt = 12;

    private static readonly string[] Units = { "rem", "px", "pt", "pc", "in", "cm", "mm", "em", "ex", "ch", "q", "%" };

    public static bool TryParseLength(string text, out CssLength length)
    {
        length = default;
        var s = text.Trim().ToLowerInvariant();
        if (s.Length == 0)
        {
            return false;
        }

        foreach (var unit in Units)
        {
            if (s.EndsWith(unit, StringComparison.Ordinal) &&
                double.TryParse(s[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                length = new CssLength(v, unit);
                return true;
            }
        }

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var bare) && bare == 0)
        {
            length = new CssLength(0, "px");
            return true;
        }

        return false;
    }

    public static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Размер шрифта в пунктах: длина, проценты или ключевое слово.</summary>
    public static bool TryParseFontSize(string text, double parentPt, out double pt)
    {
        var s = text.Trim().ToLowerInvariant();
        pt = s switch
        {
            "xx-small" => 7,
            "x-small" => 7.5,
            "small" => 10,
            "medium" => 12,
            "large" => 13.5,
            "x-large" => 18,
            "xx-large" => 24,
            "xxx-large" => 36,
            "smaller" => parentPt / 1.2,
            "larger" => parentPt * 1.2,
            _ => double.NaN
        };

        if (!double.IsNaN(pt))
        {
            return true;
        }

        if (TryParseLength(s, out var length) && length.Value > 0)
        {
            pt = length.ToPoints(parentPt);
            return true;
        }

        return false;
    }

    /// <summary>Цвет в виде RRGGBB; "" — прозрачный (сбросить). false — не цвет.</summary>
    public static bool TryParseColor(string text, out string hex)
    {
        hex = string.Empty;
        var s = text.Trim().ToLowerInvariant();
        if (s is "transparent" or "none")
        {
            return true;
        }

        if (s.StartsWith('#'))
        {
            var h = s[1..];
            if (h.Length is 3 or 4)
            {
                h = string.Concat(h[..3].Select(c => new string(c, 2)));
            }
            else if (h.Length == 8)
            {
                h = h[..6];
            }

            if (h.Length == 6 && h.All(Uri.IsHexDigit))
            {
                hex = h.ToUpperInvariant();
                return true;
            }

            return false;
        }

        if (s.StartsWith("rgb", StringComparison.Ordinal))
        {
            var open = s.IndexOf('(');
            var close = s.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return false;
            }

            var parts = s[(open + 1)..close].Replace('/', ' ').Replace(',', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return false;
            }

            var rgb = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var p = parts[i];
                if (p.EndsWith('%') && double.TryParse(p[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                {
                    rgb[i] = (int)Math.Round(Math.Clamp(pct, 0, 100) * 2.55);
                }
                else if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    rgb[i] = (int)Math.Round(Math.Clamp(v, 0, 255));
                }
                else
                {
                    return false;
                }
            }

            // Альфа-канал Word не поддерживает — берём цвет как есть.
            hex = $"{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
            return true;
        }

        if (NamedColors.TryGetValue(s, out var named))
        {
            hex = named;
            return true;
        }

        return false;
    }

    /// <summary>Первое подходящее семейство из списка font-family; общие имена заменяются
    /// шрифтами, которые есть в Windows.</summary>
    public static string? ParseFontFamily(string text)
    {
        foreach (var raw in CssParser.SplitTopLevel(text, ','))
        {
            var name = raw.Trim().Trim('"', '\'').Trim();
            if (name.Length == 0)
            {
                continue;
            }

            switch (name.ToLowerInvariant())
            {
                case "-apple-system":
                case "blinkmacsystemfont":
                case "system-ui":
                case "ui-sans-serif":
                    continue; // системные псевдонимы, в Windows-документе смысла не имеют
                case "sans-serif":
                    return "Arial";
                case "serif":
                case "ui-serif":
                    return "Times New Roman";
                case "monospace":
                case "ui-monospace":
                    return "Consolas";
                case "cursive":
                    return "Comic Sans MS";
                case "inherit":
                case "initial":
                case "unset":
                    return null;
                default:
                    return name;
            }
        }

        return null;
    }

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.Ordinal)
    {
        ["black"] = "000000", ["white"] = "FFFFFF", ["red"] = "FF0000", ["green"] = "008000",
        ["blue"] = "0000FF", ["yellow"] = "FFFF00", ["gray"] = "808080", ["grey"] = "808080",
        ["silver"] = "C0C0C0", ["maroon"] = "800000", ["olive"] = "808000", ["lime"] = "00FF00",
        ["aqua"] = "00FFFF", ["cyan"] = "00FFFF", ["teal"] = "008080", ["navy"] = "000080",
        ["fuchsia"] = "FF00FF", ["magenta"] = "FF00FF", ["purple"] = "800080", ["orange"] = "FFA500",
        ["darkgray"] = "A9A9A9", ["darkgrey"] = "A9A9A9", ["lightgray"] = "D3D3D3", ["lightgrey"] = "D3D3D3",
        ["dimgray"] = "696969", ["dimgrey"] = "696969", ["gainsboro"] = "DCDCDC", ["whitesmoke"] = "F5F5F5",
        ["darkred"] = "8B0000", ["darkgreen"] = "006400", ["darkblue"] = "00008B", ["darkorange"] = "FF8C00",
        ["crimson"] = "DC143C", ["firebrick"] = "B22222", ["tomato"] = "FF6347", ["coral"] = "FF7F50",
        ["gold"] = "FFD700", ["khaki"] = "F0E68C", ["beige"] = "F5F5DC", ["ivory"] = "FFFFF0",
        ["steelblue"] = "4682B4", ["royalblue"] = "4169E1", ["dodgerblue"] = "1E90FF", ["skyblue"] = "87CEEB",
        ["lightblue"] = "ADD8E6", ["slategray"] = "708090", ["slategrey"] = "708090", ["seagreen"] = "2E8B57",
        ["forestgreen"] = "228B22", ["lightgreen"] = "90EE90", ["indigo"] = "4B0082", ["violet"] = "EE82EE",
        ["brown"] = "A52A2A", ["chocolate"] = "D2691E", ["tan"] = "D2B48C", ["pink"] = "FFC0CB",
        ["lightyellow"] = "FFFFE0", ["lightcyan"] = "E0FFFF", ["aliceblue"] = "F0F8FF", ["honeydew"] = "F0FFF0",
        ["mintcream"] = "F5FFFA", ["lavender"] = "E6E6FA", ["linen"] = "FAF0E6", ["snow"] = "FFFAFA"
    };
}
