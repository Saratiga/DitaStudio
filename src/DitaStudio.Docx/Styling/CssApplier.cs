namespace DitaStudio.Docx.Styling;

/// <summary>Свойства стиля плюс внутренние поля (padding) — их смысл в Word зависит от рамки,
/// поэтому они разрешаются после всего каскада.</summary>
internal sealed class CssBox
{
    public CssBox(DocxStyleProps props)
    {
        Props = props;
    }

    public DocxStyleProps Props { get; }
    public double? PaddingTop { get; set; }
    public double? PaddingRight { get; set; }
    public double? PaddingBottom { get; set; }
    public double? PaddingLeft { get; set; }

    /// <summary>Поле у стороны с рамкой — расстояние от текста до рамки; без рамки — прибавка к
    /// отступу (слева/справа) или интервалу (сверху/снизу), как выглядело бы в браузере.</summary>
    public void ResolvePadding()
    {
        var p = Props;
        p.BorderLeft = Side(p.BorderLeft, PaddingLeft, v => p.IndentLeftPt = (p.IndentLeftPt ?? 0) + v);
        p.BorderRight = Side(p.BorderRight, PaddingRight, v => p.IndentRightPt = (p.IndentRightPt ?? 0) + v);
        p.BorderTop = Side(p.BorderTop, PaddingTop, v => p.SpaceBeforePt = (p.SpaceBeforePt ?? 0) + v);
        p.BorderBottom = Side(p.BorderBottom, PaddingBottom, v => p.SpaceAfterPt = (p.SpaceAfterPt ?? 0) + v);
        PaddingTop = PaddingRight = PaddingBottom = PaddingLeft = null;
    }

    private static DocxBorder? Side(DocxBorder? border, double? padding, Action<double> addToSpacing)
    {
        if (padding is not { } value || value <= 0)
        {
            return border;
        }

        if (border is not null && border.Style != "none")
        {
            return border with { SpacePt = value };
        }

        addToSpacing(value);
        return border;
    }
}

/// <summary>Переводит объявления CSS в свойства стиля Word.</summary>
internal static class CssApplier
{
    // Свойства вёрстки веб-страницы: к документу Word неприменимы, предупреждать о них — шум.
    private static readonly HashSet<string> SilentlyIgnored = new(StringComparer.Ordinal)
    {
        "box-sizing", "cursor", "transition", "animation", "max-width", "min-width", "width", "height",
        "max-height", "min-height", "overflow", "overflow-x", "overflow-y", "position", "top", "left", "right",
        "bottom", "z-index", "float", "clear", "list-style", "list-style-type", "list-style-position",
        "list-style-image", "content", "counter-reset", "counter-increment", "flex", "flex-direction",
        "flex-wrap", "flex-grow", "flex-shrink", "flex-basis", "grid", "grid-template-columns",
        "grid-template-rows", "grid-gap", "grid-column", "grid-row", "align-items", "align-self",
        "align-content", "justify-content", "justify-items", "gap", "row-gap", "column-gap", "white-space",
        "word-break", "word-wrap", "overflow-wrap", "hyphens", "font-feature-settings", "text-rendering",
        "font-kerning", "font-smoothing", "outline", "outline-offset", "user-select", "pointer-events",
        "object-fit", "border-collapse", "border-spacing", "table-layout", "caption-side", "empty-cells",
        "quotes", "orphans", "widows", "scroll-behavior", "scroll-margin-top", "will-change", "tab-size",
        "print-color-adjust", "color-scheme", "accent-color", "text-underline-offset", "text-decoration-color",
        "text-decoration-thickness", "text-decoration-style", "font-display", "direction", "unicode-bidi"
    };

    public static void Apply(CssBox box, IReadOnlyList<CssDeclaration> declarations, double parentFontPt,
        DocxStyleSheet sheet, bool silent)
    {
        var p = box.Props;

        // Сначала — итоговый размер шрифта: от него считаются em в остальных свойствах.
        var ownSize = p.FontSizePt ?? parentFontPt;
        foreach (var d in declarations)
        {
            if (d.Property is "font-size" or "font")
            {
                var value = sheet.Substitute(d.Value);
                var size = d.Property == "font-size" ? value : FontShorthandSize(value);
                if (size is not null && CssValues.TryParseFontSize(size, parentFontPt, out var pt))
                {
                    ownSize = pt;
                }
            }
        }

        foreach (var d in declarations)
        {
            var value = sheet.Substitute(d.Value).Trim();
            if (value is "inherit" or "initial" or "unset" or "revert")
            {
                continue;
            }

            if (!ApplyOne(box, d.Property, value, parentFontPt, ownSize) && !silent)
            {
                if (IsKnown(d.Property))
                {
                    sheet.Diag.UnsupportedValue(d.Property, value);
                }
                else if (!SilentlyIgnored.Contains(d.Property) && !d.Property.StartsWith('-'))
                {
                    sheet.Diag.UnsupportedProperty(d.Property);
                }
            }
        }
    }

    private static bool IsKnown(string property) => property is
        "font-family" or "font-size" or "font-weight" or "font-style" or "font" or "color" or
        "background" or "background-color" or "text-decoration" or "text-decoration-line" or
        "text-transform" or "font-variant" or "font-variant-caps" or "letter-spacing" or
        "vertical-align" or "display" or "visibility" or "text-align" or "text-indent" or "line-height" or
        "page-break-before" or "break-before" or "page-break-after" or "break-after" or
        "page-break-inside" or "break-inside" ||
        property.StartsWith("margin", StringComparison.Ordinal) ||
        property.StartsWith("padding", StringComparison.Ordinal) ||
        property.StartsWith("border", StringComparison.Ordinal);

    /// <returns>false — значение или свойство не удалось перенести.</returns>
    private static bool ApplyOne(CssBox box, string property, string value, double parentPt, double ownPt)
    {
        var p = box.Props;
        var lower = value.ToLowerInvariant();

        switch (property)
        {
            case "font-family":
                var family = CssValues.ParseFontFamily(value);
                if (family is null)
                {
                    return false;
                }

                p.FontFamily = family;
                return true;

            case "font-size":
                if (!CssValues.TryParseFontSize(value, parentPt, out var size))
                {
                    return false;
                }

                p.FontSizePt = size;
                return true;

            case "font":
                return ApplyFontShorthand(box, value, parentPt, ownPt);

            case "font-weight":
                if (lower is "bold" or "bolder")
                {
                    p.Bold = true;
                    return true;
                }

                if (lower is "normal" or "lighter")
                {
                    p.Bold = false;
                    return true;
                }

                if (CssValues.TryParseNumber(lower, out var weight))
                {
                    p.Bold = weight >= 600;
                    return true;
                }

                return false;

            case "font-style":
                p.Italic = lower is "italic" or "oblique" ? true : lower == "normal" ? false : null;
                return p.Italic is not null;

            case "color":
                if (!CssValues.TryParseColor(value, out var color))
                {
                    return false;
                }

                p.Color = color;
                return true;

            case "background-color":
                if (!CssValues.TryParseColor(value, out var fill))
                {
                    return false;
                }

                p.Background = fill;
                return true;

            case "background":
                if (lower is "none" or "transparent")
                {
                    p.Background = string.Empty;
                    return true;
                }

                foreach (var token in CssParser.SplitTopLevel(value, ' '))
                {
                    if (CssValues.TryParseColor(token, out var bg))
                    {
                        p.Background = bg;
                        return true;
                    }
                }

                return false; // картинки и градиенты в Word не переносятся

            case "text-decoration":
            case "text-decoration-line":
                if (lower.Contains("none"))
                {
                    p.Underline = false;
                    p.Strike = false;
                    return true;
                }

                var any = false;
                if (lower.Contains("underline"))
                {
                    p.Underline = true;
                    any = true;
                }

                if (lower.Contains("line-through"))
                {
                    p.Strike = true;
                    any = true;
                }

                return any;

            case "text-transform":
                if (lower == "uppercase")
                {
                    p.Caps = true;
                    return true;
                }

                if (lower == "none")
                {
                    p.Caps = false;
                    return true;
                }

                return false; // capitalize / lowercase в Word как формат знака не выражаются

            case "font-variant":
            case "font-variant-caps":
                if (lower is "small-caps" or "all-small-caps")
                {
                    p.SmallCaps = true;
                    return true;
                }

                if (lower == "normal")
                {
                    p.SmallCaps = false;
                    return true;
                }

                return false;

            case "letter-spacing":
                if (lower == "normal")
                {
                    p.LetterSpacingPt = 0;
                    return true;
                }

                if (Length(value, ownPt) is not { } spacing)
                {
                    return false;
                }

                p.LetterSpacingPt = spacing;
                return true;

            case "vertical-align":
                p.VerticalAlign = lower switch
                {
                    "super" => "superscript",
                    "sub" => "subscript",
                    "baseline" => "baseline",
                    _ => null
                };
                return p.VerticalAlign is not null || lower is "top" or "middle" or "bottom" or "text-top" or "text-bottom";

            case "display":
                if (lower == "none")
                {
                    p.Hidden = true;
                }

                return true; // остальные значения display к Word неприменимы и ничего не ломают

            case "visibility":
                if (lower is "hidden" or "collapse")
                {
                    p.Hidden = true;
                    return true;
                }

                if (lower == "visible")
                {
                    p.Hidden = false;
                    return true;
                }

                return false;

            case "text-align":
                p.Align = lower switch
                {
                    "left" or "start" => "left",
                    "right" or "end" => "right",
                    "center" => "center",
                    "justify" => "both",
                    _ => null
                };
                return p.Align is not null;

            case "text-indent":
                if (Length(value, ownPt) is not { } indent)
                {
                    return false;
                }

                p.FirstLinePt = indent;
                return true;

            case "line-height":
                return ApplyLineHeight(p, lower, ownPt);

            case "margin":
                if (Sides(value, ownPt) is not { } margins)
                {
                    return false;
                }

                (p.SpaceBeforePt, p.IndentRightPt, p.SpaceAfterPt, p.IndentLeftPt) = margins;
                return true;

            case "margin-top":
            case "margin-block-start":
                return SetLength(value, ownPt, v => p.SpaceBeforePt = v);
            case "margin-bottom":
            case "margin-block-end":
                return SetLength(value, ownPt, v => p.SpaceAfterPt = v);
            case "margin-left":
            case "margin-inline-start":
                return SetLength(value, ownPt, v => p.IndentLeftPt = v);
            case "margin-right":
            case "margin-inline-end":
                return SetLength(value, ownPt, v => p.IndentRightPt = v);
            case "margin-block":
                if (Pair(value, ownPt) is not { } block)
                {
                    return false;
                }

                (p.SpaceBeforePt, p.SpaceAfterPt) = block;
                return true;
            case "margin-inline":
                if (Pair(value, ownPt) is not { } inline)
                {
                    return false;
                }

                (p.IndentLeftPt, p.IndentRightPt) = inline;
                return true;

            case "padding":
                if (Sides(value, ownPt) is not { } paddings)
                {
                    return false;
                }

                (box.PaddingTop, box.PaddingRight, box.PaddingBottom, box.PaddingLeft) = paddings;
                return true;
            case "padding-top":
                return SetLength(value, ownPt, v => box.PaddingTop = v);
            case "padding-right":
                return SetLength(value, ownPt, v => box.PaddingRight = v);
            case "padding-bottom":
                return SetLength(value, ownPt, v => box.PaddingBottom = v);
            case "padding-left":
                return SetLength(value, ownPt, v => box.PaddingLeft = v);

            case "page-break-before":
            case "break-before":
                if (lower is "always" or "page" or "left" or "right" or "recto" or "verso")
                {
                    p.PageBreakBefore = true;
                    return true;
                }

                if (lower is "auto" or "avoid" or "avoid-page")
                {
                    p.PageBreakBefore = false;
                    return true;
                }

                return false;

            case "page-break-after":
            case "break-after":
                if (lower is "avoid" or "avoid-page")
                {
                    p.KeepNext = true;
                    return true;
                }

                if (lower == "auto")
                {
                    p.KeepNext = false;
                    return true;
                }

                return false; // «разрыв после» Word выражает только разрывом перед следующим абзацем

            case "page-break-inside":
            case "break-inside":
                if (lower is "avoid" or "avoid-page")
                {
                    p.KeepLines = true;
                    return true;
                }

                if (lower == "auto")
                {
                    p.KeepLines = false;
                    return true;
                }

                return false;
        }

        if (property.StartsWith("border", StringComparison.Ordinal))
        {
            return ApplyBorder(p, property, value, ownPt);
        }

        return false;
    }

    private static bool ApplyLineHeight(DocxStyleProps p, string value, double ownPt)
    {
        if (value == "normal")
        {
            p.LineHeight = new DocxLineHeight(1, DocxLineRule.Multiple);
            return true;
        }

        if (CssValues.TryParseNumber(value, out var multiplier) && multiplier > 0)
        {
            p.LineHeight = new DocxLineHeight(multiplier, DocxLineRule.Multiple);
            return true;
        }

        if (value.EndsWith('%') && CssValues.TryParseNumber(value[..^1], out var percent) && percent > 0)
        {
            p.LineHeight = new DocxLineHeight(percent / 100, DocxLineRule.Multiple);
            return true;
        }

        if (value.EndsWith("em", StringComparison.Ordinal) && !value.EndsWith("rem", StringComparison.Ordinal) &&
            CssValues.TryParseNumber(value[..^2], out var em) && em > 0)
        {
            p.LineHeight = new DocxLineHeight(em, DocxLineRule.Multiple);
            return true;
        }

        if (Length(value, ownPt) is { } points && points > 0)
        {
            p.LineHeight = new DocxLineHeight(points, DocxLineRule.AtLeast);
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ рамки

    private static bool ApplyBorder(DocxStyleProps p, string property, string value, double ownPt)
    {
        var parts = property.Split('-');
        // border | border-top | border-width | border-top-width | border-color | …
        string[] sides = parts.Length >= 2 && parts[1] is "top" or "right" or "bottom" or "left"
            ? new[] { parts[1] }
            : new[] { "top", "right", "bottom", "left" };
        var aspect = parts.Length switch
        {
            1 => null,
            2 when parts[1] is "top" or "right" or "bottom" or "left" => null,
            2 => parts[1],
            3 => parts[2],
            _ => "unknown"
        };

        if (aspect is "radius" or "image" or "collapse" or "spacing" or "unknown" ||
            parts.Length >= 2 && parts[1] is "radius" or "image" or "collapse" or "spacing" or "block" or "inline")
        {
            return false;
        }

        if (aspect is null)
        {
            if (ParseBorderShorthand(value, ownPt, p.Color) is not { } border)
            {
                return false;
            }

            foreach (var side in sides)
            {
                SetSide(p, side, border);
            }

            return true;
        }

        // Отдельные width/style/color: для четырёх сторон значения могут быть разными
        var values = aspect == "color"
            ? CssParser.SplitTopLevel(value, ' ').Where(s => s.Length > 0).ToList()
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (values.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < sides.Length; i++)
        {
            var side = sides[i];
            var token = sides.Length == 1 ? values[0] : SideValue(values, side);
            var current = GetSide(p, side) ?? new DocxBorder("none", 2.25, p.Color ?? string.Empty);
            switch (aspect)
            {
                case "width":
                    if (BorderWidth(token, ownPt) is not { } width)
                    {
                        return false;
                    }

                    current = current with { WidthPt = width };
                    break;
                case "style":
                    if (BorderStyleName(token) is not { } style)
                    {
                        return false;
                    }

                    current = current with { Style = style };
                    break;
                case "color":
                    if (!CssValues.TryParseColor(token, out var color))
                    {
                        return false;
                    }

                    current = current with { Color = color };
                    break;
                default:
                    return false;
            }

            SetSide(p, side, current);
        }

        return true;
    }

    private static string SideValue(List<string> values, string side) => (values.Count, side) switch
    {
        (1, _) => values[0],
        (2, "top" or "bottom") => values[0],
        (2, _) => values[1],
        (3, "top") => values[0],
        (3, "bottom") => values[2],
        (3, _) => values[1],
        (_, "top") => values[0],
        (_, "right") => values[1],
        (_, "bottom") => values[2],
        _ => values[3]
    };

    private static DocxBorder? ParseBorderShorthand(string value, double ownPt, string? currentColor)
    {
        if (value.Trim().ToLowerInvariant() is "none" or "0" or "hidden")
        {
            return new DocxBorder("none", 0, string.Empty);
        }

        string? style = null;
        double width = 2.25; // medium
        var color = currentColor ?? string.Empty;
        foreach (var token in CssParser.SplitTopLevel(value, ' ').Where(t => t.Length > 0))
        {
            if (BorderStyleName(token) is { } s)
            {
                style = s;
            }
            else if (BorderWidth(token, ownPt) is { } w)
            {
                width = w;
            }
            else if (CssValues.TryParseColor(token, out var c))
            {
                color = c;
            }
            else
            {
                return null;
            }
        }

        // Без стиля рамка в CSS не рисуется
        return new DocxBorder(style ?? "none", width, color);
    }

    private static string? BorderStyleName(string token) => token.Trim().ToLowerInvariant() switch
    {
        "none" or "hidden" => "none",
        "solid" => "single",
        "dashed" => "dashed",
        "dotted" => "dotted",
        "double" => "double",
        "groove" => "groove",
        "ridge" => "ridge",
        "inset" => "inset",
        "outset" => "outset",
        _ => null
    };

    private static double? BorderWidth(string token, double ownPt) => token.Trim().ToLowerInvariant() switch
    {
        "thin" => 0.75,
        "medium" => 2.25,
        "thick" => 3.75,
        var t => Length(t, ownPt)
    };

    private static DocxBorder? GetSide(DocxStyleProps p, string side) => side switch
    {
        "top" => p.BorderTop,
        "right" => p.BorderRight,
        "bottom" => p.BorderBottom,
        _ => p.BorderLeft
    };

    private static void SetSide(DocxStyleProps p, string side, DocxBorder border)
    {
        switch (side)
        {
            case "top":
                p.BorderTop = border;
                break;
            case "right":
                p.BorderRight = border;
                break;
            case "bottom":
                p.BorderBottom = border;
                break;
            default:
                p.BorderLeft = border;
                break;
        }
    }

    // ---------------------------------------------------------------- шрифт

    private static string? FontShorthandSize(string value)
    {
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var sizePart = token.Split('/')[0];
            if (CssValues.TryParseFontSize(sizePart, CssValues.RootFontSizePt, out _) &&
                !CssValues.TryParseNumber(sizePart, out _))
            {
                return sizePart;
            }
        }

        return null;
    }

    // font: [style] [variant] [weight] size[/line-height] family
    private static bool ApplyFontShorthand(CssBox box, string value, double parentPt, double ownPt)
    {
        var p = box.Props;
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].ToLowerInvariant();
            if (token is "italic" or "oblique")
            {
                p.Italic = true;
            }
            else if (token is "bold" or "bolder" || CssValues.TryParseNumber(token, out var w) && w >= 600 && w <= 1000)
            {
                p.Bold = true;
            }
            else if (token == "small-caps")
            {
                p.SmallCaps = true;
            }
            else if (token is "normal" or "lighter" || CssValues.TryParseNumber(token, out _))
            {
            }
            else
            {
                var sizeAndLine = tokens[i].Split('/');
                if (!CssValues.TryParseFontSize(sizeAndLine[0], parentPt, out var size))
                {
                    return false;
                }

                p.FontSizePt = size;
                if (sizeAndLine.Length > 1)
                {
                    ApplyLineHeight(p, sizeAndLine[1].ToLowerInvariant(), ownPt);
                }

                var family = CssValues.ParseFontFamily(string.Join(' ', tokens.Skip(i + 1)));
                if (family is not null)
                {
                    p.FontFamily = family;
                }

                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------ длины

    public static double? Length(string value, double emBasePt)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v == "auto")
        {
            return null;
        }

        return CssValues.TryParseLength(v, out var length) ? length.ToPoints(emBasePt) : null;
    }

    private static bool SetLength(string value, double ownPt, Action<double> set)
    {
        if (value.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return true; // «auto» у полей (центрирование блока) в Word смысла не имеет
        }

        if (Length(value, ownPt) is not { } length)
        {
            return false;
        }

        set(length);
        return true;
    }

    /// <summary>Сокращённая запись с 1–4 значениями: top right bottom left.</summary>
    public static (double Top, double Right, double Bottom, double Left)? Sides(string value, double emBasePt)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<double>();
        foreach (var part in parts)
        {
            if (part.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                numbers.Add(0);
                continue;
            }

            if (Length(part, emBasePt) is not { } n)
            {
                return null;
            }

            numbers.Add(n);
        }

        return numbers.Count switch
        {
            1 => (numbers[0], numbers[0], numbers[0], numbers[0]),
            2 => (numbers[0], numbers[1], numbers[0], numbers[1]),
            3 => (numbers[0], numbers[1], numbers[2], numbers[1]),
            4 => (numbers[0], numbers[1], numbers[2], numbers[3]),
            _ => null
        };
    }

    private static (double, double)? Pair(string value, double emBasePt)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || Length(parts[0], emBasePt) is not { } first)
        {
            return null;
        }

        var second = parts.Length == 2 ? Length(parts[1], emBasePt) : first;
        return second is null ? null : (first, second.Value);
    }
}
