using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx.Styling;

/// <summary>Маркер маркированного списка: текст и шрифт (null — шрифт абзаца); None — без маркера.</summary>
public sealed record DocxListMarker(string Text, string? Font, bool None = false)
{
    // U+F0B7 и U+F0A7 — символы шрифтов Symbol и Wingdings из области частного использования
    // Unicode. Только escape-последовательностями: при чтении и перезаписи файла такие символы
    // незаметно теряются, и маркер становится пустым.
    public static readonly DocxListMarker Disc = new("\uF0B7", "Symbol");
    public static readonly DocxListMarker Circle = new("o", "Courier New");
    public static readonly DocxListMarker Square = new("\uF0A7", "Wingdings");
    public static readonly DocxListMarker Nothing = new(string.Empty, null, None: true);

    /// <summary>Маркеры уровней по умолчанию — как у браузера: disc, circle, square, дальше по кругу.</summary>
    public static DocxListMarker DefaultForLevel(int ilvl) => (ilvl % 3) switch
    {
        0 => Disc,
        1 => Circle,
        _ => Square
    };

    /// <summary>Значение list-style-type, list-style или content у ::marker. null — не маркер
    /// (или вид, который в маркированный список Word не переносится, например decimal).</summary>
    public static DocxListMarker? Parse(string value)
    {
        foreach (var raw in CssParser.SplitTopLevel(value, ' '))
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (token.Length >= 2 && token[0] is '"' or '\'' && token[^1] == token[0])
            {
                var text = token[1..^1].Trim();
                return text.Length == 0 ? Nothing : new DocxListMarker(text, null);
            }

            switch (token.ToLowerInvariant())
            {
                case "disc":
                    return Disc;
                case "circle":
                    return Circle;
                case "square":
                    return Square;
                case "none":
                    return Nothing;
                case "inside":
                case "outside":
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }
}

/// <summary>Определения нумерации Word (abstractNum) для списков.</summary>
public static class DocxNumbering
{
    /// <summary>Маркированный список: маркер и цвет маркера для каждого из 9 уровней.</summary>
    public static W.AbstractNum Bullet(int abstractId, Func<int, DocxListMarker> marker, Func<int, string?> color)
    {
        var abstractNum = new W.AbstractNum { AbstractNumberId = abstractId };
        for (var i = 0; i < 9; i++)
        {
            var m = marker(i);
            var level = new W.Level
            {
                LevelIndex = i,
                StartNumberingValue = new W.StartNumberingValue { Val = 1 },
                NumberingFormat = new W.NumberingFormat { Val = m.None ? W.NumberFormatValues.None : W.NumberFormatValues.Bullet },
                LevelText = new W.LevelText { Val = m.None ? string.Empty : m.Text },
                LevelJustification = new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                PreviousParagraphProperties = new W.PreviousParagraphProperties(
                    new W.Indentation { Left = ((i + 1) * 360).ToString(), Hanging = "360" })
            };

            if (SymbolRunProperties(m.Font, color(i)) is { } runProperties)
            {
                level.NumberingSymbolRunProperties = runProperties;
            }

            abstractNum.Append(level);
        }

        return abstractNum;
    }

    /// <summary>Нумерованный список «1.», «1.1.», «1.1.1.».</summary>
    public static W.AbstractNum Decimal(int abstractId, string? color)
    {
        var abstractNum = new W.AbstractNum { AbstractNumberId = abstractId };
        string[] formats = { "%1.", "%1.%2.", "%1.%2.%3." };
        for (var i = 0; i < 9; i++)
        {
            var level = new W.Level
            {
                LevelIndex = i,
                StartNumberingValue = new W.StartNumberingValue { Val = 1 },
                NumberingFormat = new W.NumberingFormat { Val = W.NumberFormatValues.Decimal },
                LevelText = new W.LevelText { Val = formats[Math.Min(i, formats.Length - 1)] },
                LevelJustification = new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                PreviousParagraphProperties = new W.PreviousParagraphProperties(
                    new W.Indentation { Left = ((i + 1) * 360).ToString(), Hanging = "360" })
            };

            if (SymbolRunProperties(null, color) is { } runProperties)
            {
                level.NumberingSymbolRunProperties = runProperties;
            }

            abstractNum.Append(level);
        }

        return abstractNum;
    }

    // Шрифт маркера — для всех групп символов: иначе шрифт стиля абзаца (например, заданный в
    // CSS для body) перехватывает символ маркера из Symbol/Wingdings, и маркер исчезает.
    private static W.NumberingSymbolRunProperties? SymbolRunProperties(string? font, string? color)
    {
        if (font is null && string.IsNullOrEmpty(color))
        {
            return null;
        }

        var properties = new W.NumberingSymbolRunProperties();
        if (font is not null)
        {
            properties.Append(new W.RunFonts
            {
                Ascii = font, HighAnsi = font, ComplexScript = font, EastAsia = font,
                Hint = W.FontTypeHintValues.Default
            });
        }

        if (!string.IsNullOrEmpty(color))
        {
            properties.Append(new W.Color { Val = color });
        }

        return properties;
    }
}
