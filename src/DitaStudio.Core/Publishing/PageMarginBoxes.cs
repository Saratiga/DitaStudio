using System.Globalization;
using System.Text;
using DitaStudio.Core.Localization;

namespace DitaStudio.Core.Publishing;

/// <summary>Место колонтитула на листе: поля страницы CSS Paged Media (<c>@top-left</c>, <c>@bottom-center</c>…).</summary>
public enum MarginBoxPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

/// <summary>Часть содержимого колонтитула из <c>content</c>.</summary>
public abstract record MarginContent
{
    /// <summary>Строка в кавычках.</summary>
    public sealed record Text(string Value) : MarginContent;

    /// <summary><c>counter(page)</c> — номер страницы.</summary>
    public sealed record PageNumber : MarginContent;

    /// <summary><c>counter(pages)</c> — число страниц.</summary>
    public sealed record PageCount : MarginContent;

    /// <summary><c>string(title)</c> — название издания.</summary>
    public sealed record Title : MarginContent;

    /// <summary><c>url(картинка)</c> — путь от папки проекта или абсолютный.</summary>
    public sealed record Image(string Path) : MarginContent;
}

/// <summary>
/// Одно поле страницы: содержимое и оформление. <see cref="HeightMm"/> — высота картинки (иначе картинка
/// подгоняется под колонтитул); <see cref="EdgeDistanceMm"/> — расстояние от края листа
/// (<c>margin-top</c> у верхних полей, <c>margin-bottom</c> у нижних).
/// </summary>
public sealed class MarginBox
{
    public MarginBoxPosition Position { get; init; }

    public IReadOnlyList<MarginContent> Content { get; init; } = Array.Empty<MarginContent>();

    public string? FontFamily { get; init; }

    public double? FontSizePt { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    /// <summary>Цвет #RRGGBB или null.</summary>
    public string? Color { get; init; }

    /// <summary>Линия у края текста: у верхних полей — снизу, у нижних — сверху (толщина в пт, цвет #RRGGBB).</summary>
    public (double WidthPt, string Color)? Rule { get; init; }

    public double? HeightMm { get; init; }

    public double? EdgeDistanceMm { get; init; }

    public bool IsTop => Position <= MarginBoxPosition.TopRight;
}

/// <summary>
/// Колонтитулы из пользовательского CSS: стандартные поля страницы <c>@page { @top-left { content: … } }</c>.
/// Заданные в CSS перекрывают колонтитулы из диалога «Оформление DOCX»: верх — если в CSS есть хотя бы одно
/// верхнее поле, низ — так же. Одинаково служат DOCX (колонтитулы Word с полями PAGE/NUMPAGES) и PDF
/// (шаблоны колонтитулов Chromium — его версия 120 полей страницы сама не выводит).
/// </summary>
public sealed class PageMarginBoxes
{
    private static readonly Dictionary<string, MarginBoxPosition> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["top-left"] = MarginBoxPosition.TopLeft,
        ["top-center"] = MarginBoxPosition.TopCenter,
        ["top-right"] = MarginBoxPosition.TopRight,
        ["bottom-left"] = MarginBoxPosition.BottomLeft,
        ["bottom-center"] = MarginBoxPosition.BottomCenter,
        ["bottom-right"] = MarginBoxPosition.BottomRight
    };

    public static readonly PageMarginBoxes Empty = new(Array.Empty<MarginBox>(), Array.Empty<string>());

    private PageMarginBoxes(IReadOnlyList<MarginBox> boxes, IReadOnlyList<string> warnings)
    {
        Boxes = boxes;
        Warnings = warnings;
    }

    public IReadOnlyList<MarginBox> Boxes { get; }

    /// <summary>Что в CSS не разобрано (свойство, значение <c>content</c>).</summary>
    public IReadOnlyList<string> Warnings { get; }

    public bool HasTop => Boxes.Any(b => b.IsTop);

    public bool HasBottom => Boxes.Any(b => !b.IsTop);

    public IEnumerable<MarginBox> Top => Boxes.Where(b => b.IsTop);

    public IEnumerable<MarginBox> Bottom => Boxes.Where(b => !b.IsTop);

    /// <summary>
    /// Разбирает <c>@page { @top-left {…} }</c> из текста CSS (в том числе внутри <c>@media print</c>);
    /// несколько правил <c>@page</c> складываются по порядку, позднее свойство перекрывает раннее.
    /// <c>content: none</c> убирает поле.
    /// </summary>
    public static PageMarginBoxes Parse(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return Empty;
        }

        var declarations = new Dictionary<MarginBoxPosition, Dictionary<string, string>>();
        Collect(StripComments(css), declarations);
        if (declarations.Count == 0)
        {
            return Empty;
        }

        var warnings = new List<string>();
        var boxes = new List<MarginBox>();
        foreach (var (position, props) in declarations.OrderBy(pair => pair.Key))
        {
            if (!props.TryGetValue("content", out var content) || content.Trim() is "none" or "normal" or "")
            {
                continue;
            }

            var box = Build(position, props, content, warnings);
            if (box.Content.Count > 0)
            {
                boxes.Add(box);
            }
        }

        return boxes.Count == 0 && warnings.Count == 0 ? Empty : new PageMarginBoxes(boxes, warnings);
    }

    // ------------------------------------------------------------------ разбор правил

    private static void Collect(string css, Dictionary<MarginBoxPosition, Dictionary<string, string>> declarations)
    {
        var i = 0;
        while (i < css.Length)
        {
            var open = IndexOfBlockStart(css, i);
            if (open < 0)
            {
                return;
            }

            var prelude = css[i..open].Trim();
            var close = MatchingBrace(css, open);
            var body = close < 0 ? css[(open + 1)..] : css[(open + 1)..close];
            i = close < 0 ? css.Length : close + 1;

            if (prelude.StartsWith("@media", StringComparison.OrdinalIgnoreCase))
            {
                var query = prelude[6..].ToLowerInvariant();
                if (query.Trim().Length == 0 || query.Contains("print") || query.Contains("all") || query.Contains("docx"))
                {
                    Collect(body, declarations);
                }
            }
            else if (prelude.StartsWith("@page", StringComparison.OrdinalIgnoreCase) && prelude[5..].Trim().Length == 0)
            {
                CollectPage(body, declarations);
            }
        }
    }

    private static void CollectPage(string body, Dictionary<MarginBoxPosition, Dictionary<string, string>> declarations)
    {
        // Внутри @page — свои свойства (size, margin) и вложенные блоки полей.
        var i = 0;
        while (i < body.Length)
        {
            var open = IndexOfBlockStart(body, i);
            if (open < 0)
            {
                return;
            }

            // Заголовок вложенного блока — после последнего ';' перед '{'.
            var segment = body[i..open];
            var name = segment[(segment.LastIndexOf(';') + 1)..].Trim();
            var close = MatchingBrace(body, open);
            var inner = close < 0 ? body[(open + 1)..] : body[(open + 1)..close];
            i = close < 0 ? body.Length : close + 1;

            if (name.Length > 1 && name[0] == '@' && Names.TryGetValue(name[1..], out var position))
            {
                if (!declarations.TryGetValue(position, out var props))
                {
                    declarations[position] = props = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                foreach (var (property, value) in Declarations(inner))
                {
                    props[property] = value;
                }
            }
        }
    }

    /// <summary>Свойства верхнего уровня блока (без вложенных): пары «имя: значение».</summary>
    internal static IEnumerable<(string Property, string Value)> Declarations(string body)
    {
        foreach (var part in SplitTopLevel(body, ';'))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var value = part[(colon + 1)..].Trim();
            var important = value.LastIndexOf("!important", StringComparison.OrdinalIgnoreCase);
            if (important >= 0)
            {
                value = value[..important].Trim();
            }

            if (value.Length > 0)
            {
                yield return (part[..colon].Trim().ToLowerInvariant(), value);
            }
        }
    }

    // ------------------------------------------------------------------ поле

    private static MarginBox Build(MarginBoxPosition position, Dictionary<string, string> props, string content, List<string> warnings)
    {
        var items = ParseContent(content, warnings, position);
        string? family = null;
        double? size = null, height = null, edge = null;
        var bold = false;
        var italic = false;
        string? color = null;
        (double, string)? rule = null;
        var top = position <= MarginBoxPosition.TopRight;

        foreach (var (property, value) in props)
        {
            switch (property)
            {
                case "content":
                    break;
                case "font-family":
                    family = value.Split(',')[0].Trim().Trim('"', '\'');
                    break;
                case "font-size":
                    size = LengthPt(value, 10);
                    break;
                case "font-weight":
                    bold = value is "bold" or "bolder" || int.TryParse(value, out var weight) && weight >= 600;
                    break;
                case "font-style":
                    italic = value is "italic" or "oblique";
                    break;
                case "font":
                    // сокращение: «italic bold 9pt Arial»
                    var afterSize = false;
                    var familyParts = new List<string>();
                    foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (afterSize)
                        {
                            familyParts.Add(token);
                        }
                        else if (token is "bold" or "bolder")
                        {
                            bold = true;
                        }
                        else if (token is "italic" or "oblique")
                        {
                            italic = true;
                        }
                        else if (LengthPt(token.Split('/')[0], 10) is { } fontSize)
                        {
                            size = fontSize;
                            afterSize = true;
                        }
                    }

                    if (familyParts.Count > 0)
                    {
                        family = string.Join(' ', familyParts).Split(',')[0].Trim().Trim('"', '\'');
                    }

                    break;
                case "color":
                    color = ColorHex(value);
                    break;
                case "height":
                    height = LengthPt(value, 10) is { } h ? h / 72 * 25.4 : null;
                    break;
                case "margin-top" when top:
                case "margin-bottom" when !top:
                    edge = LengthPt(value, 10) is { } e ? e / 72 * 25.4 : null;
                    break;
                case "border-bottom" when top:
                case "border-top" when !top:
                    rule = Border(value);
                    break;
                case "text-align":
                case "vertical-align":
                case "width":
                case "padding":
                case "margin":
                    break; // место задаёт поле (left/center/right), остальное в колонтитулах Word не выразить
                default:
                    warnings.Add(Loc.T("Core_01ThePropertyIsNot", Name(position), property));
                    break;
            }
        }

        return new MarginBox
        {
            Position = position,
            Content = items,
            FontFamily = family,
            FontSizePt = size,
            Bold = bold,
            Italic = italic,
            Color = color,
            Rule = rule,
            HeightMm = height,
            EdgeDistanceMm = edge
        };
    }

    private static string Name(MarginBoxPosition position) =>
        Names.First(pair => pair.Value == position).Key;

    /// <summary>Разбор <c>content</c>: строки, counter(page|pages), string(title), url(...); части склеиваются.</summary>
    private static List<MarginContent> ParseContent(string value, List<string> warnings, MarginBoxPosition position)
    {
        var items = new List<MarginContent>();
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '"' or '\'')
            {
                var text = new StringBuilder();
                i++;
                while (i < value.Length && value[i] != c)
                {
                    if (value[i] == '\\' && i + 1 < value.Length)
                    {
                        i++;
                        // \A — перевод строки в CSS; в колонтитулах он не нужен
                        text.Append(value[i] is 'A' or 'a' ? ' ' : value[i]);
                    }
                    else
                    {
                        text.Append(value[i]);
                    }

                    i++;
                }

                i++;
                items.Add(new MarginContent.Text(text.ToString()));
            }
            else
            {
                var open = value.IndexOf('(', i);
                var close = open < 0 ? -1 : value.IndexOf(')', open);
                if (open < 0 || close < 0)
                {
                    warnings.Add(Loc.T("Core_0Content1CouldNotBe", Name(position), value[i..].Trim()));
                    break;
                }

                var function = value[i..open].Trim().ToLowerInvariant();
                var argument = value[(open + 1)..close].Trim().Trim('"', '\'');
                i = close + 1;
                switch (function, argument.ToLowerInvariant())
                {
                    case ("counter", "page"):
                        items.Add(new MarginContent.PageNumber());
                        break;
                    case ("counter", "pages"):
                        items.Add(new MarginContent.PageCount());
                        break;
                    case ("string", "title"):
                        items.Add(new MarginContent.Title());
                        break;
                    case ("url", _) when argument.Length > 0:
                        items.Add(new MarginContent.Image(argument));
                        break;
                    default:
                        warnings.Add(Loc.T("Core_0Content12IsNot", Name(position), function, argument));
                        break;
                }
            }
        }

        return items;
    }

    // ------------------------------------------------------------------ значения

    /// <summary>Длина в пунктах (pt, px, mm, cm, in, em/rem от <paramref name="basePt"/>); null — не длина.</summary>
    internal static double? LengthPt(string value, double basePt)
    {
        value = value.Trim().ToLowerInvariant();
        string[] units = { "pt", "px", "mm", "cm", "in", "rem", "em" };
        foreach (var unit in units)
        {
            if (value.EndsWith(unit, StringComparison.Ordinal) &&
                double.TryParse(value[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return unit switch
                {
                    "pt" => number,
                    "px" => number * 0.75,
                    "mm" => number / 25.4 * 72,
                    "cm" => number / 2.54 * 72,
                    "in" => number * 72,
                    _ => number * basePt
                };
            }
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var zero) && zero == 0 ? 0 : null;
    }

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "000000", ["white"] = "FFFFFF", ["gray"] = "808080", ["grey"] = "808080", ["silver"] = "C0C0C0",
        ["red"] = "FF0000", ["green"] = "008000", ["blue"] = "0000FF", ["navy"] = "000080", ["orange"] = "FFA500",
        ["purple"] = "800080", ["maroon"] = "800000", ["teal"] = "008080"
    };

    /// <summary>#RGB, #RRGGBB, rgb(r,g,b) или имя цвета → «RRGGBB».</summary>
    internal static string? ColorHex(string value)
    {
        value = value.Trim();
        if (NamedColors.TryGetValue(value, out var named))
        {
            return named;
        }

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3 && hex.All(Uri.IsHexDigit))
            {
                return string.Concat(hex.Select(ch => new string(ch, 2))).ToUpperInvariant();
            }

            return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? hex.ToUpperInvariant() : null;
        }

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
        {
            var parts = value[4..^1].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 3 && parts.All(p => int.TryParse(p, out var n) && n is >= 0 and <= 255))
            {
                return string.Concat(parts.Select(p => int.Parse(p).ToString("X2", CultureInfo.InvariantCulture)));
            }
        }

        return null;
    }

    /// <summary><c>1px solid #888</c> → толщина и цвет; <c>none</c> → null.</summary>
    private static (double, string)? Border(string value)
    {
        double width = 0.75;
        var color = "808080";
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "none" or "hidden")
            {
                return null;
            }

            if (LengthPt(token, 10) is { } length)
            {
                width = length;
            }
            else if (ColorHex(token) is { } parsed)
            {
                color = parsed;
            }
        }

        return (Math.Clamp(width, 0.25, 12), color);
    }

    // ------------------------------------------------------------------ скобки, комментарии

    private static string StripComments(string css)
    {
        var text = new StringBuilder(css.Length);
        for (var i = 0; i < css.Length; i++)
        {
            if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? css.Length : end + 1;
                continue;
            }

            text.Append(css[i]);
        }

        return text.ToString();
    }

    private static int IndexOfBlockStart(string text, int from)
    {
        char? quote = null;
        for (var i = from; i < text.Length; i++)
        {
            if (quote is not null)
            {
                if (text[i] == '\\')
                {
                    i++;
                }
                else if (text[i] == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (text[i] is '"' or '\'')
            {
                quote = text[i];
            }
            else if (text[i] == '{')
            {
                return i;
            }
        }

        return -1;
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        char? quote = null;
        for (var i = open; i < text.Length; i++)
        {
            if (quote is not null)
            {
                if (text[i] == '\\')
                {
                    i++;
                }
                else if (text[i] == quote)
                {
                    quote = null;
                }

                continue;
            }

            switch (text[i])
            {
                case '"' or '\'':
                    quote = text[i];
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    /// <summary>Делит по <paramref name="separator"/> вне скобок, кавычек и вложенных блоков.</summary>
    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var start = 0;
        var parens = 0;
        var braces = 0;
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '(':
                    parens++;
                    break;
                case ')':
                    parens = Math.Max(0, parens - 1);
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces = Math.Max(0, braces - 1);
                    break;
                default:
                    if (c == separator && parens == 0 && braces == 0)
                    {
                        yield return text[start..i];
                        start = i + 1;
                    }

                    break;
            }
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }
}
