using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Schema.Dtd;

/// <summary>Разбирает сырой текст ATTLIST (после раскрытия %entity;) в список AttributeDef —
/// формат "имя ТИП УМОЛЧАНИЕ" повторяется: <c>id ID #IMPLIED conref CDATA #IMPLIED importance
/// (obsolete|deprecated) #IMPLIED class CDATA "- topic/xref "</c>.</summary>
public static class DtdAttributeListParser
{
    public static List<AttributeDef> Parse(string attlistText)
    {
        var tokens = Tokenize(attlistText);
        var result = new List<AttributeDef>();
        var i = 0;

        while (i < tokens.Count)
        {
            var name = tokens[i++];
            if (i >= tokens.Count)
            {
                break;
            }

            var (type, values, consumed) = ParseType(tokens, i);
            i += consumed;

            var (required, defaultValue, consumedDecl) = ParseDefaultDecl(tokens, i);
            i += consumedDecl;

            result.Add(new AttributeDef(name, type, values, required, defaultValue, string.Empty));
        }

        return result;
    }

    private static (AttrType Type, IReadOnlyList<string> Values, int Consumed) ParseType(List<string> tokens, int i)
    {
        if (i >= tokens.Count)
        {
            return (AttrType.CData, Array.Empty<string>(), 0);
        }

        var token = tokens[i];
        if (token.StartsWith("(", StringComparison.Ordinal))
        {
            var values = token[1..^1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return (AttrType.Enumeration, values, 1);
        }

        if (token == "NOTATION")
        {
            // NOTATION (a|b) — сама нотация нам не нужна, но нужно съесть paren-группу следом.
            var extra = i + 1 < tokens.Count && tokens[i + 1].StartsWith("(", StringComparison.Ordinal) ? 1 : 0;
            return (AttrType.CData, Array.Empty<string>(), 1 + extra);
        }

        var type = token switch
        {
            "ID" => AttrType.Id,
            "IDREF" or "IDREFS" => AttrType.IdRef,
            "NMTOKEN" or "NMTOKENS" => AttrType.NmToken,
            _ => AttrType.CData // CDATA, ENTITY, ENTITIES — свободный текст
        };
        return (type, Array.Empty<string>(), 1);
    }

    private static (bool Required, string? DefaultValue, int Consumed) ParseDefaultDecl(List<string> tokens, int i)
    {
        if (i >= tokens.Count)
        {
            return (false, null, 0);
        }

        var token = tokens[i];
        switch (token)
        {
            case "#REQUIRED":
                return (true, null, 1);
            case "#IMPLIED":
                return (false, null, 1);
            case "#FIXED":
                if (i + 1 < tokens.Count && IsQuoted(tokens[i + 1]))
                {
                    return (false, Unquote(tokens[i + 1]), 2);
                }

                return (false, null, 1);
            default:
                if (IsQuoted(token))
                {
                    return (false, Unquote(token), 1);
                }

                return (false, null, 0);
        }
    }

    private static bool IsQuoted(string token) => token.Length >= 2 && token[0] is '"' or '\'';

    private static string Unquote(string token) => token.Length >= 2 ? token[1..^1] : token;

    /// <summary>Разбивает текст на токены по пробелам, не разрывая "(a|b|c)" и "строки в кавычках".</summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;

        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= text.Length)
            {
                break;
            }

            if (text[i] == '(')
            {
                var start = i;
                var depth = 0;
                while (i < text.Length)
                {
                    if (text[i] == '(')
                    {
                        depth++;
                    }
                    else if (text[i] == ')')
                    {
                        depth--;
                        i++;
                        if (depth == 0)
                        {
                            break;
                        }

                        continue;
                    }

                    i++;
                }

                tokens.Add(text[start..i]);
                continue;
            }

            if (text[i] is '"' or '\'')
            {
                var quote = text[i];
                var start = i;
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    i++;
                }

                i = Math.Min(i + 1, text.Length);
                tokens.Add(text[start..i]);
                continue;
            }

            var tokenStart = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(')
            {
                i++;
            }

            tokens.Add(text[tokenStart..i]);
        }

        return tokens;
    }
}
