using DocumentFormat.OpenXml;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx.Styling;

/// <summary>Рамка одной стороны абзаца. Style "none" — убрать рамку (в том числе унаследованную).</summary>
public sealed record DocxBorder(string Style, double WidthPt, string Color, double SpacePt = 0);

public enum DocxLineRule
{
    /// <summary>Множитель (1.5 — полуторный интервал).</summary>
    Multiple,

    /// <summary>Не меньше указанного числа пунктов.</summary>
    AtLeast
}

public sealed record DocxLineHeight(double Value, DocxLineRule Rule);

/// <summary>
/// Оформление одного стиля Word в терминах CSS-подобных свойств. null — «не задано,
/// наследуется от базового стиля»; явное false/"none" перекрывает наследование.
/// </summary>
public sealed class DocxStyleProps
{
    // ---- знак
    public string? FontFamily { get; set; }
    public double? FontSizePt { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public bool? Strike { get; set; }
    public bool? Caps { get; set; }
    public bool? SmallCaps { get; set; }
    public bool? Hidden { get; set; }

    /// <summary>RRGGBB; "" — цвет по умолчанию (auto).</summary>
    public string? Color { get; set; }

    /// <summary>Заливка RRGGBB; "" — без заливки.</summary>
    public string? Background { get; set; }

    public double? LetterSpacingPt { get; set; }

    /// <summary>superscript / subscript / baseline.</summary>
    public string? VerticalAlign { get; set; }

    // ---- абзац
    /// <summary>left / center / right / both.</summary>
    public string? Align { get; set; }
    public double? SpaceBeforePt { get; set; }
    public double? SpaceAfterPt { get; set; }
    public double? IndentLeftPt { get; set; }
    public double? IndentRightPt { get; set; }

    /// <summary>Отступ первой строки; отрицательный — выступ.</summary>
    public double? FirstLinePt { get; set; }
    public DocxLineHeight? LineHeight { get; set; }
    public DocxBorder? BorderTop { get; set; }
    public DocxBorder? BorderBottom { get; set; }
    public DocxBorder? BorderLeft { get; set; }
    public DocxBorder? BorderRight { get; set; }
    public bool? KeepNext { get; set; }
    public bool? KeepLines { get; set; }
    public bool? PageBreakBefore { get; set; }

    public bool HasRunProps =>
        FontFamily is not null || FontSizePt is not null || Bold is not null || Italic is not null ||
        Underline is not null || Strike is not null || Caps is not null || SmallCaps is not null ||
        Hidden is not null || Color is not null || Background is not null || LetterSpacingPt is not null ||
        VerticalAlign is not null;

    public bool HasParagraphProps =>
        Align is not null || SpaceBeforePt is not null || SpaceAfterPt is not null || IndentLeftPt is not null ||
        IndentRightPt is not null || FirstLinePt is not null || LineHeight is not null || BorderTop is not null ||
        BorderBottom is not null || BorderLeft is not null || BorderRight is not null || KeepNext is not null ||
        KeepLines is not null || PageBreakBefore is not null;

    public bool IsEmpty => !HasRunProps && !HasParagraphProps;

    public DocxStyleProps Clone() => (DocxStyleProps)MemberwiseClone();

    /// <summary>Заданные в other значения перекрывают свои.</summary>
    public void Overlay(DocxStyleProps other)
    {
        FontFamily = other.FontFamily ?? FontFamily;
        FontSizePt = other.FontSizePt ?? FontSizePt;
        Bold = other.Bold ?? Bold;
        Italic = other.Italic ?? Italic;
        Underline = other.Underline ?? Underline;
        Strike = other.Strike ?? Strike;
        Caps = other.Caps ?? Caps;
        SmallCaps = other.SmallCaps ?? SmallCaps;
        Hidden = other.Hidden ?? Hidden;
        Color = other.Color ?? Color;
        Background = other.Background ?? Background;
        LetterSpacingPt = other.LetterSpacingPt ?? LetterSpacingPt;
        VerticalAlign = other.VerticalAlign ?? VerticalAlign;
        Align = other.Align ?? Align;
        SpaceBeforePt = other.SpaceBeforePt ?? SpaceBeforePt;
        SpaceAfterPt = other.SpaceAfterPt ?? SpaceAfterPt;
        IndentLeftPt = other.IndentLeftPt ?? IndentLeftPt;
        IndentRightPt = other.IndentRightPt ?? IndentRightPt;
        FirstLinePt = other.FirstLinePt ?? FirstLinePt;
        LineHeight = other.LineHeight ?? LineHeight;
        BorderTop = other.BorderTop ?? BorderTop;
        BorderBottom = other.BorderBottom ?? BorderBottom;
        BorderLeft = other.BorderLeft ?? BorderLeft;
        BorderRight = other.BorderRight ?? BorderRight;
        KeepNext = other.KeepNext ?? KeepNext;
        KeepLines = other.KeepLines ?? KeepLines;
        PageBreakBefore = other.PageBreakBefore ?? PageBreakBefore;
    }

    /// <summary>Только свойства знака — для символьных стилей.</summary>
    public DocxStyleProps RunOnly()
    {
        var copy = new DocxStyleProps();
        copy.FontFamily = FontFamily;
        copy.FontSizePt = FontSizePt;
        copy.Bold = Bold;
        copy.Italic = Italic;
        copy.Underline = Underline;
        copy.Strike = Strike;
        copy.Caps = Caps;
        copy.SmallCaps = SmallCaps;
        copy.Hidden = Hidden;
        copy.Color = Color;
        copy.Background = Background;
        copy.LetterSpacingPt = LetterSpacingPt;
        copy.VerticalAlign = VerticalAlign;
        return copy;
    }
}

/// <summary>
/// Перевод DocxStyleProps в элементы WordprocessingML. Порядок дочерних элементов rPr/pPr в
/// OOXML строгий — нарушение Word считает повреждением файла, поэтому элементы выдаются уже
/// упорядоченными и добавляются в пустой контейнер.
/// </summary>
public static class DocxPropsWriter
{
    public static int Twips(double pt) => (int)Math.Round(pt * 20);

    public static IEnumerable<OpenXmlElement> RunElements(DocxStyleProps p)
    {
        if (p.FontFamily is not null)
        {
            yield return new W.RunFonts
            {
                Ascii = p.FontFamily, HighAnsi = p.FontFamily, ComplexScript = p.FontFamily, EastAsia = p.FontFamily
            };
        }

        if (p.Bold is { } bold)
        {
            yield return bold ? new W.Bold() : new W.Bold { Val = false };
        }

        if (p.Italic is { } italic)
        {
            yield return italic ? new W.Italic() : new W.Italic { Val = false };
        }

        if (p.Caps is { } caps)
        {
            yield return caps ? new W.Caps() : new W.Caps { Val = false };
        }

        if (p.SmallCaps is { } smallCaps)
        {
            yield return smallCaps ? new W.SmallCaps() : new W.SmallCaps { Val = false };
        }

        if (p.Strike is { } strike)
        {
            yield return strike ? new W.Strike() : new W.Strike { Val = false };
        }

        if (p.Hidden is { } hidden)
        {
            yield return hidden ? new W.Vanish() : new W.Vanish { Val = false };
        }

        if (p.Color is not null)
        {
            yield return new W.Color { Val = p.Color.Length == 0 ? "auto" : p.Color };
        }

        if (p.LetterSpacingPt is { } spacing)
        {
            yield return new W.Spacing { Val = Twips(spacing) };
        }

        if (p.FontSizePt is { } size)
        {
            var halfPoints = Math.Max(2, (int)Math.Round(size * 2)).ToString();
            yield return new W.FontSize { Val = halfPoints };
            yield return new W.FontSizeComplexScript { Val = halfPoints };
        }

        if (p.Underline is { } underline)
        {
            yield return new W.Underline { Val = underline ? W.UnderlineValues.Single : W.UnderlineValues.None };
        }

        if (p.Background is not null)
        {
            yield return Shading(p.Background);
        }

        if (p.VerticalAlign is not null)
        {
            yield return new W.VerticalTextAlignment
            {
                Val = p.VerticalAlign switch
                {
                    "superscript" => W.VerticalPositionValues.Superscript,
                    "subscript" => W.VerticalPositionValues.Subscript,
                    _ => W.VerticalPositionValues.Baseline
                }
            };
        }
    }

    /// <summary>Элементы pPr (без pStyle — его ставит вызывающий первым).</summary>
    public static IEnumerable<OpenXmlElement> ParagraphElements(DocxStyleProps p, W.NumberingProperties? numbering = null, int? outlineLevel = null)
    {
        if (p.KeepNext is { } keepNext)
        {
            yield return keepNext ? new W.KeepNext() : new W.KeepNext { Val = false };
        }

        if (p.KeepLines is { } keepLines)
        {
            yield return keepLines ? new W.KeepLines() : new W.KeepLines { Val = false };
        }

        if (p.PageBreakBefore is { } pageBreak)
        {
            yield return pageBreak ? new W.PageBreakBefore() : new W.PageBreakBefore { Val = false };
        }

        if (numbering is not null)
        {
            yield return numbering;
        }

        if (p.BorderTop is not null || p.BorderLeft is not null || p.BorderBottom is not null || p.BorderRight is not null)
        {
            var borders = new W.ParagraphBorders();
            if (p.BorderTop is not null)
            {
                borders.Append(Border(new W.TopBorder(), p.BorderTop));
            }

            if (p.BorderLeft is not null)
            {
                borders.Append(Border(new W.LeftBorder(), p.BorderLeft));
            }

            if (p.BorderBottom is not null)
            {
                borders.Append(Border(new W.BottomBorder(), p.BorderBottom));
            }

            if (p.BorderRight is not null)
            {
                borders.Append(Border(new W.RightBorder(), p.BorderRight));
            }

            yield return borders;
        }

        if (p.Background is not null)
        {
            yield return Shading(p.Background);
        }

        if (p.SpaceBeforePt is not null || p.SpaceAfterPt is not null || p.LineHeight is not null)
        {
            var spacing = new W.SpacingBetweenLines();
            if (p.SpaceBeforePt is { } before)
            {
                spacing.Before = Math.Max(0, Twips(before)).ToString();
            }

            if (p.SpaceAfterPt is { } after)
            {
                spacing.After = Math.Max(0, Twips(after)).ToString();
            }

            if (p.LineHeight is { } line)
            {
                if (line.Rule == DocxLineRule.Multiple)
                {
                    spacing.Line = ((int)Math.Round(Math.Clamp(line.Value, 0.5, 10) * 240)).ToString();
                    spacing.LineRule = W.LineSpacingRuleValues.Auto;
                }
                else
                {
                    spacing.Line = Math.Max(20, Twips(line.Value)).ToString();
                    spacing.LineRule = W.LineSpacingRuleValues.AtLeast;
                }
            }

            yield return spacing;
        }

        if (p.IndentLeftPt is not null || p.IndentRightPt is not null || p.FirstLinePt is not null)
        {
            var indentation = new W.Indentation();
            if (p.IndentLeftPt is { } left)
            {
                indentation.Left = Twips(left).ToString();
            }

            if (p.IndentRightPt is { } right)
            {
                indentation.Right = Twips(right).ToString();
            }

            if (p.FirstLinePt is { } first)
            {
                if (first < 0)
                {
                    indentation.Hanging = Twips(-first).ToString();
                }
                else
                {
                    indentation.FirstLine = Twips(first).ToString();
                }
            }

            yield return indentation;
        }

        if (p.Align is not null)
        {
            yield return new W.Justification
            {
                Val = p.Align switch
                {
                    "center" => W.JustificationValues.Center,
                    "right" => W.JustificationValues.Right,
                    "both" => W.JustificationValues.Both,
                    _ => W.JustificationValues.Left
                }
            };
        }

        if (outlineLevel is { } level)
        {
            yield return new W.OutlineLevel { Val = level };
        }
    }

    public static W.Shading Shading(string fill) => new()
    {
        Val = W.ShadingPatternValues.Clear,
        Color = "auto",
        Fill = fill.Length == 0 ? "auto" : fill
    };

    public static T Border<T>(T border, DocxBorder spec) where T : W.BorderType
    {
        border.Val = BorderStyle(spec.Style);
        if (spec.Style != "none")
        {
            border.Size = (uint)Math.Clamp((int)Math.Round(spec.WidthPt * 8), 2, 96);
            border.Space = (uint)Math.Clamp((int)Math.Round(spec.SpacePt), 0, 31);
            border.Color = string.IsNullOrEmpty(spec.Color) ? "auto" : spec.Color;
        }

        return border;
    }

    public static W.BorderValues BorderStyle(string style) => style switch
    {
        "none" => W.BorderValues.Nil,
        "dashed" => W.BorderValues.Dashed,
        "dotted" => W.BorderValues.Dotted,
        "double" => W.BorderValues.Double,
        "groove" => W.BorderValues.ThreeDEngrave,
        "ridge" => W.BorderValues.ThreeDEmboss,
        "inset" => W.BorderValues.Inset,
        "outset" => W.BorderValues.Outset,
        _ => W.BorderValues.Single
    };

    /// <summary>Добавляет элементы по порядку в пустой контейнер (rPr/pPr стиля или абзаца).</summary>
    public static TContainer Fill<TContainer>(TContainer container, IEnumerable<OpenXmlElement> elements)
        where TContainer : OpenXmlCompositeElement
    {
        foreach (var element in elements)
        {
            container.Append(element);
        }

        return container;
    }
}
