using System.Globalization;
using System.Text;
using DitaStudio.Core.Model;
using DitaStudio.Core.Localization;

namespace DitaStudio.Docx.Styling;

/// <summary>
/// Селектор CSS уровня 3, сопоставляемый прямо с деревом DITA: имя элемента, класс (значение
/// <c>outputclass</c> и имя элемента), <c>#id</c>, атрибуты <c>[a]</c> <c>[a=v]</c> <c>[a~=v]</c> <c>[a|=v]</c>
/// <c>[a^=v]</c> <c>[a$=v]</c> <c>[a*=v]</c>, потомок <c>a b</c>, ребёнок <c>a &gt; b</c>, соседи <c>a + b</c>
/// и <c>a ~ b</c>, псевдоклассы <c>:first-child</c>, <c>:last-child</c>, <c>:only-child</c>, <c>:nth-child()</c>,
/// <c>:nth-last-child()</c>, <c>:first-of-type</c>, <c>:last-of-type</c>, <c>:nth-of-type()</c>, <c>:empty</c>,
/// <c>:root</c>, <c>:not()</c>. Псевдоэлементы <c>::before</c> и <c>::after</c> запоминаются отдельно.
/// </summary>
public sealed class CssSelector
{
    private readonly List<Step> _steps;

    private CssSelector(string text, List<Step> steps, string? pseudoElement)
    {
        Text = text;
        _steps = steps;
        PseudoElement = pseudoElement;
    }

    public string Text { get; }

    /// <summary>"before" или "after", если селектор оканчивается псевдоэлементом; иначе null.</summary>
    public string? PseudoElement { get; }

    /// <summary>Специфичность одним числом: id·10000 + (классы, атрибуты, псевдоклассы)·100 + типы.</summary>
    public int Specificity => _steps.Sum(step => step.Compound.Specificity) + (PseudoElement is null ? 0 : 1);

    // ------------------------------------------------------------------ разбор

    /// <summary>Разбирает селектор; null и причина в <paramref name="error"/> — если он не поддерживается.</summary>
    public static CssSelector? TryParse(string text, out string? error)
    {
        error = null;
        try
        {
            var parser = new Parser(text.Trim());
            var (steps, pseudoElement) = parser.ParseComplex();
            if (!parser.AtEnd)
            {
                throw new FormatException(Loc.T("Core_UnexpectedCharacter0", parser.Current));
            }

            return new CssSelector(text.Trim(), steps, pseudoElement);
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    // ------------------------------------------------------------------ сопоставление

    /// <summary>Подходит ли элемент под селектор (псевдоэлемент при этом не учитывается — он относится к тому же элементу).</summary>
    public bool Matches(DitaNode node) => node.Kind == NodeKind.Element && MatchFrom(node, _steps.Count - 1);

    private bool MatchFrom(DitaNode node, int index)
    {
        var step = _steps[index];
        if (!step.Compound.Matches(node))
        {
            return false;
        }

        if (index == 0)
        {
            return true;
        }

        switch (step.Combinator)
        {
            case ' ':
                for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (ancestor.Kind == NodeKind.Element && MatchFrom(ancestor, index - 1))
                    {
                        return true;
                    }
                }

                return false;
            case '>':
                return node.Parent is { Kind: NodeKind.Element } parent && MatchFrom(parent, index - 1);
            case '+':
                return PreviousElement(node) is { } previous && MatchFrom(previous, index - 1);
            case '~':
                for (var sibling = PreviousElement(node); sibling is not null; sibling = PreviousElement(sibling))
                {
                    if (MatchFrom(sibling, index - 1))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    internal static DitaNode? PreviousElement(DitaNode node)
    {
        if (node.Parent is not { } parent)
        {
            return null;
        }

        DitaNode? previous = null;
        foreach (var child in parent.Children)
        {
            if (ReferenceEquals(child, node))
            {
                return previous;
            }

            if (child.Kind == NodeKind.Element)
            {
                previous = child;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ модель

    private sealed record Step(char Combinator, Compound Compound);

    /// <summary>Простой селектор без комбинаторов: тип, классы, id, атрибуты, псевдоклассы.</summary>
    private sealed class Compound
    {
        public string? Type;
        public readonly List<string> Classes = new();
        public string? Id;
        public readonly List<AttributeTest> Attributes = new();
        public readonly List<Pseudo> Pseudos = new();

        public int Specificity =>
            (Id is null ? 0 : 10000) +
            (Classes.Count + Attributes.Count + Pseudos.Sum(p => p.Specificity)) * 100 +
            (Type is null ? 0 : 1);

        public bool Matches(DitaNode node)
        {
            if (Type is not null && !string.Equals(Type, node.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Id is not null && node.GetAttribute("id") != Id)
            {
                return false;
            }

            if (Classes.Count > 0)
            {
                var tokens = ClassTokens(node);
                if (!Classes.All(tokens.Contains))
                {
                    return false;
                }
            }

            return Attributes.All(a => a.Matches(node)) && Pseudos.All(p => p.Matches(node));
        }
    }

    /// <summary>Классы элемента: значения <c>outputclass</c> и имя элемента — публикация HTML ставит его классом.</summary>
    internal static HashSet<string> ClassTokens(DitaNode node)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal) { node.Name };
        foreach (var token in (node.GetAttribute("outputclass") ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            tokens.Add(token);
        }

        return tokens;
    }

    private sealed record AttributeTest(string Name, string? Operator, string? Value, bool IgnoreCase)
    {
        public bool Matches(DitaNode node)
        {
            var actual = node.GetAttribute(Name);
            if (actual is null)
            {
                return false;
            }

            if (Operator is null)
            {
                return true;
            }

            var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var expected = Value ?? string.Empty;
            return Operator switch
            {
                "=" => string.Equals(actual, expected, comparison),
                "~=" => expected.Length > 0 && actual.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(t => string.Equals(t, expected, comparison)),
                "|=" => string.Equals(actual, expected, comparison) || actual.StartsWith(expected + "-", comparison),
                "^=" => expected.Length > 0 && actual.StartsWith(expected, comparison),
                "$=" => expected.Length > 0 && actual.EndsWith(expected, comparison),
                "*=" => expected.Length > 0 && actual.Contains(expected, comparison),
                _ => false
            };
        }
    }

    private sealed class Pseudo
    {
        public string Name = string.Empty;
        public int A;
        public int B;
        public List<Compound> Not = new();

        public int Specificity => Name == "not" ? Not.Count == 0 ? 0 : Not.Max(c => c.Specificity) / 100 : 1;

        public bool Matches(DitaNode node)
        {
            switch (Name)
            {
                case "first-child":
                    return IndexOf(node, false, false) == 1;
                case "last-child":
                    return IndexOf(node, true, false) == 1;
                case "only-child":
                    return IndexOf(node, false, false) == 1 && IndexOf(node, true, false) == 1;
                case "first-of-type":
                    return IndexOf(node, false, true) == 1;
                case "last-of-type":
                    return IndexOf(node, true, true) == 1;
                case "nth-child":
                    return Nth(IndexOf(node, false, false));
                case "nth-last-child":
                    return Nth(IndexOf(node, true, false));
                case "nth-of-type":
                    return Nth(IndexOf(node, false, true));
                case "nth-last-of-type":
                    return Nth(IndexOf(node, true, true));
                case "empty":
                    return node.Children.All(c => c.Kind == NodeKind.Comment || c.Kind == NodeKind.Text && c.Value.Length == 0);
                case "root":
                    return node.Parent is null || node.Parent.Kind != NodeKind.Element;
                case "not":
                    return !Not.Any(c => c.Matches(node));
                default:
                    return false;
            }
        }

        // Номер элемента среди сестёр (с 1), с начала или с конца; ofType — считаются только сёстры с тем же именем.
        private static int IndexOf(DitaNode node, bool fromEnd, bool ofType)
        {
            if (node.Parent is not { } parent)
            {
                return 1;
            }

            var siblings = parent.Children.Where(c => c.Kind == NodeKind.Element && (!ofType || c.Name == node.Name)).ToList();
            var position = siblings.FindIndex(c => ReferenceEquals(c, node));
            return position < 0 ? 0 : fromEnd ? siblings.Count - position : position + 1;
        }

        // Есть ли целое n ≥ 0, при котором A·n + B = позиции.
        private bool Nth(int position)
        {
            if (position <= 0)
            {
                return false;
            }

            if (A == 0)
            {
                return position == B;
            }

            var difference = position - B;
            return difference % A == 0 && difference / A >= 0;
        }
    }

    // ------------------------------------------------------------------ разборщик

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;

        public Parser(string text)
        {
            _text = text;
        }

        public bool AtEnd => _position >= _text.Length;

        public char Current => _text[_position];

        public (List<Step> Steps, string? PseudoElement) ParseComplex()
        {
            var steps = new List<Step>();
            string? pseudoElement = null;
            var combinator = ' ';
            SkipSpaces();
            while (!AtEnd)
            {
                var compound = ParseCompound(out var element);
                steps.Add(new Step(steps.Count == 0 ? ' ' : combinator, compound));
                if (element is not null)
                {
                    pseudoElement = element;
                    if (!AtEnd)
                    {
                        throw new FormatException(Loc.T("Core_NothingCanFollowAPseudoElement"));
                    }

                    break;
                }

                var hadSpace = SkipSpaces();
                if (AtEnd || Current == ')' || Current == ',')
                {
                    break;
                }

                if (Current is '>' or '+' or '~')
                {
                    combinator = Current;
                    _position++;
                    SkipSpaces();
                }
                else if (hadSpace)
                {
                    combinator = ' ';
                }
                else
                {
                    throw new FormatException(Loc.T("Core_UnexpectedCharacter0", Current));
                }
            }

            if (steps.Count == 0)
            {
                throw new FormatException(Loc.T("Core_EmptySelector"));
            }

            return (steps, pseudoElement);
        }

        private bool SkipSpaces()
        {
            var start = _position;
            while (!AtEnd && char.IsWhiteSpace(Current))
            {
                _position++;
            }

            return _position > start;
        }

        private Compound ParseCompound(out string? pseudoElement)
        {
            pseudoElement = null;
            var compound = new Compound();
            var any = false;
            if (!AtEnd && Current == '*')
            {
                _position++;
                any = true;
            }
            else if (!AtEnd && (char.IsLetter(Current) || Current == '_'))
            {
                compound.Type = Identifier();
                any = true;
            }

            while (!AtEnd)
            {
                switch (Current)
                {
                    case '.':
                        _position++;
                        compound.Classes.Add(Identifier());
                        break;
                    case '#':
                        _position++;
                        compound.Id = Identifier();
                        break;
                    case '[':
                        compound.Attributes.Add(ParseAttribute());
                        break;
                    case ':':
                        _position++;
                        if (!AtEnd && Current == ':')
                        {
                            _position++;
                            var element = Identifier().ToLowerInvariant();
                            if (element is not ("before" or "after"))
                            {
                                throw new FormatException(Loc.T("Core_ThePseudoElement0IsNot", element));
                            }

                            pseudoElement = element;
                            return compound;
                        }

                        var name = Identifier().ToLowerInvariant();
                        if (name is "before" or "after")
                        {
                            pseudoElement = name; // старая запись :before
                            return compound;
                        }

                        compound.Pseudos.Add(ParsePseudo(name));
                        break;
                    default:
                        if (!any && compound.Classes.Count == 0 && compound.Id is null && compound.Attributes.Count == 0 && compound.Pseudos.Count == 0)
                        {
                            throw new FormatException(Loc.T("Core_UnexpectedCharacter0", Current));
                        }

                        return compound;
                }

                any = true;
            }

            return compound;
        }

        private Pseudo ParsePseudo(string name)
        {
            var pseudo = new Pseudo { Name = name };
            switch (name)
            {
                case "first-child" or "last-child" or "only-child" or "first-of-type" or "last-of-type" or "empty" or "root":
                    return pseudo;
                case "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type":
                {
                    var argument = Arguments();
                    (pseudo.A, pseudo.B) = ParseNth(argument);
                    return pseudo;
                }
                case "not":
                {
                    if (AtEnd || Current != '(')
                    {
                        throw new FormatException(Loc.T("Core_NotWithoutParentheses"));
                    }

                    _position++;
                    while (true)
                    {
                        SkipSpaces();
                        pseudo.Not.Add(ParseCompound(out var element));
                        if (element is not null)
                        {
                            throw new FormatException(Loc.T("Core_PseudoElementsCannotAppearInsideNot"));
                        }

                        SkipSpaces();
                        if (AtEnd)
                        {
                            throw new FormatException(Loc.T("Core_NotIsNotClosed"));
                        }

                        if (Current == ',')
                        {
                            _position++;
                            continue;
                        }

                        if (Current == ')')
                        {
                            _position++;
                            return pseudo;
                        }

                        throw new FormatException(Loc.T("Core_OnlySimpleSelectorsAreAllowedInside"));
                    }
                }

                default:
                    throw new FormatException(Loc.T("Core_ThePseudoClass0IsNot", name));
            }
        }

        private string Arguments()
        {
            if (AtEnd || Current != '(')
            {
                throw new FormatException(Loc.T("Core_NoArgumentInParentheses"));
            }

            var close = _text.IndexOf(')', _position);
            if (close < 0)
            {
                throw new FormatException(Loc.T("Core_ParenthesisIsNotClosed"));
            }

            var argument = _text[(_position + 1)..close].Trim();
            _position = close + 1;
            return argument;
        }

        private static (int A, int B) ParseNth(string argument)
        {
            var text = argument.Replace(" ", string.Empty).ToLowerInvariant();
            if (text == "odd")
            {
                return (2, 1);
            }

            if (text == "even")
            {
                return (2, 0);
            }

            var n = text.IndexOf('n');
            if (n < 0)
            {
                return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var only)
                    ? (0, only)
                    : throw new FormatException(Loc.T("Core_UnclearNumber0", argument));
            }

            var aText = text[..n];
            var a = aText switch
            {
                "" or "+" => 1,
                "-" => -1,
                _ => int.TryParse(aText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new FormatException(Loc.T("Core_UnclearNumber0", argument))
            };
            var bText = text[(n + 1)..];
            var b = 0;
            if (bText.Length > 0 && !int.TryParse(bText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out b))
            {
                throw new FormatException(Loc.T("Core_UnclearNumber0", argument));
            }

            return (a, b);
        }

        private AttributeTest ParseAttribute()
        {
            var close = _text.IndexOf(']', _position);
            if (close < 0)
            {
                throw new FormatException(Loc.T("Core_BracketIsNotClosed"));
            }

            var body = _text[(_position + 1)..close].Trim();
            _position = close + 1;
            var ignoreCase = false;
            if (body.EndsWith(" i", StringComparison.OrdinalIgnoreCase))
            {
                ignoreCase = true;
                body = body[..^2].TrimEnd();
            }

            var opIndex = body.IndexOfAny(new[] { '=', '~', '|', '^', '$', '*' });
            if (opIndex < 0)
            {
                return new AttributeTest(body, null, null, ignoreCase);
            }

            var end = body.IndexOf('=', opIndex);
            if (end < 0)
            {
                throw new FormatException(Loc.T("Core_UnclearAttributeSelector0", body));
            }

            var op = body[opIndex..(end + 1)];
            var name = body[..opIndex].Trim();
            var value = body[(end + 1)..].Trim();
            if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            if (name.Length == 0 || op is not ("=" or "~=" or "|=" or "^=" or "$=" or "*="))
            {
                throw new FormatException(Loc.T("Core_UnclearAttributeSelector0", body));
            }

            return new AttributeTest(name, op, value, ignoreCase);
        }

        private string Identifier()
        {
            var start = _position;
            var text = new StringBuilder();
            while (!AtEnd && (char.IsLetterOrDigit(Current) || Current is '-' or '_'))
            {
                text.Append(Current);
                _position++;
            }

            if (_position == start)
            {
                throw new FormatException(Loc.T("Core_ANameWasExpected"));
            }

            return text.ToString();
        }
    }
}
