using System.Text;

namespace DitaStudio.Docx.Styling;

public sealed record CssDeclaration(string Property, string Value);

/// <summary>Обычное правило: список селекторов и объявления; Order — порядок в файле (для каскада).</summary>
public sealed record CssRule(IReadOnlyList<string> Selectors, IReadOnlyList<CssDeclaration> Declarations, int Order);

/// <summary>Правило @page (Pseudo — :first/:left/:right или null).</summary>
public sealed record CssPageRule(string? Pseudo, IReadOnlyList<CssDeclaration> Declarations, int Order);

public sealed class CssStyleSheet
{
    public List<CssRule> Rules { get; } = new();

    public List<CssPageRule> PageRules { get; } = new();

    /// <summary>Пропущенные @-правила (@import, @font-face, @supports…) — для предупреждений.</summary>
    public List<string> SkippedAtRules { get; } = new();
}

/// <summary>
/// Небольшой разборщик CSS — ровно столько, сколько нужно для переноса оформления в DOCX:
/// правила, списки селекторов, @media (по фильтру типов носителя), @page. Комментарии, строки и
/// вложенные скобки учитываются; всё непонятное пропускается, а не роняет публикацию.
/// </summary>
public static class CssParser
{
    /// <param name="mediaMatches">Решает, применяется ли блок @media с данным списком запросов.</param>
    public static CssStyleSheet Parse(string css, Func<string, bool> mediaMatches)
    {
        var sheet = new CssStyleSheet();
        var order = 0;
        ParseBlock(StripComments(css ?? string.Empty), sheet, mediaMatches, ref order);
        return sheet;
    }

    /// <summary>Запрос @media применяется к DOCX: all, print или собственный тип docx. Запросы с
    /// условиями (ширина экрана и т. п.) к документу Word отношения не имеют.</summary>
    public static bool DocxMedia(string mediaList)
    {
        foreach (var raw in SplitTopLevel(mediaList, ','))
        {
            var query = raw.Trim().ToLowerInvariant();
            if (query.StartsWith("only ", StringComparison.Ordinal))
            {
                query = query[5..].Trim();
            }

            if (query.Contains('(') || query.StartsWith("not ", StringComparison.Ordinal))
            {
                continue;
            }

            if (query is "all" or "print" or "docx")
            {
                return true;
            }
        }

        return false;
    }

    private static void ParseBlock(string text, CssStyleSheet sheet, Func<string, bool> mediaMatches, ref int order)
    {
        var i = 0;
        while (i < text.Length)
        {
            var preludeStart = i;
            var end = FindPreludeEnd(text, i);
            if (end < 0)
            {
                break; // хвост без блока — игнорируем
            }

            var prelude = text[preludeStart..end].Trim();
            if (text[end] == ';')
            {
                // @import/@charset/@namespace; или мусор
                if (prelude.StartsWith("@import", StringComparison.OrdinalIgnoreCase))
                {
                    sheet.SkippedAtRules.Add("@import");
                }

                i = end + 1;
                continue;
            }

            var close = FindMatchingBrace(text, end);
            var body = close < 0 ? text[(end + 1)..] : text[(end + 1)..close];
            i = close < 0 ? text.Length : close + 1;

            if (prelude.Length == 0)
            {
                continue;
            }

            if (prelude[0] == '@')
            {
                var name = AtRuleName(prelude);
                switch (name)
                {
                    case "media":
                        if (mediaMatches(prelude[6..]))
                        {
                            ParseBlock(body, sheet, mediaMatches, ref order);
                        }

                        break;
                    case "page":
                        var pseudo = prelude[5..].Trim();
                        sheet.PageRules.Add(new CssPageRule(pseudo.Length == 0 ? null : pseudo, ParseDeclarations(WithoutNestedBlocks(body)), order++));
                        break;
                    default:
                        sheet.SkippedAtRules.Add("@" + name);
                        break;
                }

                continue;
            }

            var selectors = SplitTopLevel(prelude, ',')
                .Select(s => NormalizeWhitespace(s))
                .Where(s => s.Length > 0)
                .ToList();
            if (selectors.Count > 0)
            {
                sheet.Rules.Add(new CssRule(selectors, ParseDeclarations(body), order++));
            }
        }
    }

    /// <summary>Тело @page без вложенных блоков полей страницы (<c>@top-left { … }</c>): их разбирает
    /// <see cref="DitaStudio.Core.Publishing.PageMarginBoxes"/>, а среди свойств страницы они были бы мусором.</summary>
    private static string WithoutNestedBlocks(string body)
    {
        if (!body.Contains('{'))
        {
            return body;
        }

        var result = new StringBuilder();
        var depth = 0;
        foreach (var c in body)
        {
            if (c == '{')
            {
                if (depth == 0)
                {
                    // Заголовок блока — хвост после последней ';' — уже попал в result: отрезаем.
                    var cut = result.ToString().LastIndexOf(';') + 1;
                    result.Length = cut;
                }

                depth++;
            }
            else if (c == '}')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0)
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    public static List<CssDeclaration> ParseDeclarations(string body)
    {
        var result = new List<CssDeclaration>();
        foreach (var part in SplitTopLevel(body, ';'))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var property = part[..colon].Trim().ToLowerInvariant();
            var value = part[(colon + 1)..].Trim();
            var important = value.LastIndexOf("!important", StringComparison.OrdinalIgnoreCase);
            if (important >= 0)
            {
                value = value[..important].Trim();
            }

            if (property.Length > 0 && value.Length > 0)
            {
                result.Add(new CssDeclaration(property, value));
            }
        }

        return result;
    }

    private static string AtRuleName(string prelude)
    {
        var j = 1;
        while (j < prelude.Length && (char.IsLetterOrDigit(prelude[j]) || prelude[j] == '-'))
        {
            j++;
        }

        return prelude[1..j].ToLowerInvariant();
    }

    // Позиция '{' или ';', завершающей заголовок правила; -1 — до конца текста ничего нет.
    private static int FindPreludeEnd(string text, int from)
    {
        char? quote = null;
        var parens = 0;
        for (var i = from; i < text.Length; i++)
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
                    return i;
                case ';' when parens == 0:
                    return i;
            }
        }

        return -1;
    }

    private static int FindMatchingBrace(string text, int open)
    {
        var depth = 0;
        char? quote = null;
        for (var i = open; i < text.Length; i++)
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

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Делит строку по разделителю, не заходя внутрь скобок и кавычек.</summary>
    public static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        char? quote = null;
        var depth = 0;
        foreach (var c in text)
        {
            if (quote is not null)
            {
                sb.Append(c);
                if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == separator && depth == 0)
            {
                parts.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0)
        {
            parts.Add(sb.ToString());
        }

        return parts;
    }

    private static string StripComments(string css)
    {
        var sb = new StringBuilder(css.Length);
        char? quote = null;
        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];
            if (quote is not null)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < css.Length)
                {
                    sb.Append(css[++i]);
                }
                else if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                sb.Append(c);
                continue;
            }

            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? css.Length : end + 1;
                sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string NormalizeWhitespace(string selector)
    {
        var sb = new StringBuilder();
        var pendingSpace = false;
        foreach (var c in selector.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && sb.Length > 0)
            {
                sb.Append(' ');
            }

            pendingSpace = false;
            sb.Append(c);
        }

        // ".a > .b" и ".a>.b" — одно и то же: пробелы вокруг комбинаторов убираем в одном виде
        return sb.ToString()
            .Replace(" > ", ">").Replace("> ", ">").Replace(" >", ">")
            .Replace(" + ", "+").Replace(" ~ ", "~");
    }
}
