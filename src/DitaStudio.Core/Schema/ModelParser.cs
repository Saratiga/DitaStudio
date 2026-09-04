using System.Text;

namespace DitaStudio.Core.Schema;

/// <summary>
/// Разбор контент-моделей в синтаксисе DTD: (title, (shortdesc|abstract)?, prolog?, body?),
/// (#PCDATA|%basic.ph;)*, EMPTY, ANY. Параметрические сущности %name; подставляются текстом,
/// как это делает настоящий DTD-процессор.
/// </summary>
public static class ModelParser
{
    public static string ExpandEntities(string text, IReadOnlyDictionary<string, string> entities, int depth = 0)
    {
        if (depth > 24 || text.IndexOf('%') < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 64);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '%')
            {
                sb.Append(text[i]);
                continue;
            }

            var end = text.IndexOf(';', i + 1);
            if (end < 0)
            {
                sb.Append(text[i]);
                continue;
            }

            var name = text.Substring(i + 1, end - i - 1).Trim();
            if (entities.TryGetValue(name, out var value))
            {
                sb.Append(ExpandEntities(value, entities, depth + 1));
                i = end;
            }
            else
            {
                // Неизвестная сущность — выбрасываем, чтобы модель осталась валидной.
                i = end;
            }
        }

        var result = sb.ToString();
        return result.IndexOf('%') >= 0 ? ExpandEntities(result, entities, depth + 1) : result;
    }

    public static ContentModel Parse(string text)
    {
        var s = new Scanner(Normalize(text));
        s.SkipWs();
        if (s.TryKeyword("EMPTY"))
        {
            return ContentModel.Empty.Instance;
        }

        if (s.TryKeyword("ANY"))
        {
            return ContentModel.Any.Instance;
        }

        var model = ParseParticle(ref s);
        s.SkipWs();
        return model;
    }

    /// <summary>Убирает лишние разделители, оставшиеся после подстановки пустых сущностей.</summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
        }

        var s = sb.ToString();
        string prev;
        do
        {
            prev = s;
            s = s.Replace("| |", "|").Replace(", ,", ",")
                 .Replace("(|", "(").Replace("|)", ")")
                 .Replace("(,", "(").Replace(",)", ")")
                 .Replace("||", "|").Replace(",,", ",")
                 .Replace(" |", "|").Replace("| ", "|")
                 .Replace(" ,", ",").Replace(", ", ",");
        }
        while (prev != s);

        return s.Trim();
    }

    private static ContentModel ParseParticle(ref Scanner s)
    {
        var atom = ParseAtom(ref s);
        return ApplyOccurrence(ref s, atom);
    }

    private static ContentModel ApplyOccurrence(ref Scanner s, ContentModel atom)
    {
        var c = s.Peek();
        switch (c)
        {
            case '?':
                s.Next();
                return new ContentModel.Repeat(atom, 0, 1);
            case '*':
                s.Next();
                return new ContentModel.Repeat(atom, 0, -1);
            case '+':
                s.Next();
                return new ContentModel.Repeat(atom, 1, -1);
            default:
                return atom;
        }
    }

    private static ContentModel ParseAtom(ref Scanner s)
    {
        s.SkipWs();
        if (s.Peek() == '(')
        {
            s.Next();
            var items = new List<ContentModel>();
            var isChoice = false;
            var isSeq = false;

            while (true)
            {
                s.SkipWs();
                if (s.Eof)
                {
                    break;
                }

                if (s.Peek() == ')')
                {
                    s.Next();
                    break;
                }

                items.Add(ParseParticle(ref s));
                s.SkipWs();

                var sep = s.Peek();
                if (sep == '|')
                {
                    isChoice = true;
                    s.Next();
                }
                else if (sep == ',')
                {
                    isSeq = true;
                    s.Next();
                }
                else if (sep == ')')
                {
                    s.Next();
                    break;
                }
                else if (sep == '\0')
                {
                    break;
                }
                else
                {
                    // Непонятный символ — пропускаем, чтобы разбор не зациклился.
                    s.Next();
                }
            }

            if (items.Count == 1)
            {
                return items[0];
            }

            if (items.Count == 0)
            {
                return ContentModel.Empty.Instance;
            }

            return isChoice && !isSeq
                ? new ContentModel.Choice(Flatten(items, choice: true))
                : new ContentModel.Sequence(Flatten(items, choice: false));
        }

        if (s.StartsWith("#PCDATA"))
        {
            s.Advance("#PCDATA".Length);
            return ContentModel.Pcdata.Instance;
        }

        var name = s.ReadName();
        if (name.Length == 0)
        {
            s.Next();
            return ContentModel.Empty.Instance;
        }

        return new ContentModel.Name(name);
    }

    /// <summary>Схлопывает вложенные однотипные группы — модель становится компактнее.</summary>
    private static List<ContentModel> Flatten(List<ContentModel> items, bool choice)
    {
        var result = new List<ContentModel>(items.Count);
        foreach (var item in items)
        {
            if (choice && item is ContentModel.Choice c)
            {
                result.AddRange(c.Items);
            }
            else if (!choice && item is ContentModel.Sequence sq)
            {
                result.AddRange(sq.Items);
            }
            else
            {
                result.Add(item);
            }
        }

        return result;
    }

    private struct Scanner
    {
        private readonly string _text;
        private int _pos;

        public Scanner(string text)
        {
            _text = text;
            _pos = 0;
        }

        public bool Eof => _pos >= _text.Length;

        public char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

        public void Next() => _pos++;

        public void Advance(int n) => _pos += n;

        public void SkipWs()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }

        public bool StartsWith(string value) =>
            string.CompareOrdinal(_text, _pos, value, 0, value.Length) == 0;

        public bool TryKeyword(string value)
        {
            SkipWs();
            if (StartsWith(value))
            {
                Advance(value.Length);
                return true;
            }

            return false;
        }

        public string ReadName()
        {
            var start = _pos;
            while (_pos < _text.Length)
            {
                var ch = _text[_pos];
                if (char.IsLetterOrDigit(ch) || ch is '-' or '.' or '_' or ':')
                {
                    _pos++;
                }
                else
                {
                    break;
                }
            }

            return _text.Substring(start, _pos - start);
        }
    }
}
