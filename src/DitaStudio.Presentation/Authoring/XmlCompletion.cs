using System.Text;
using DitaStudio.Core.Schema;

namespace DitaStudio.Presentation.Authoring;

/// <summary>Пункт автодополнения в исходном XML: что показать, что вставить и на сколько
/// символов вернуть курсор назад (внутрь вставленного тега или кавычек).</summary>
public sealed record XmlSuggestion(string Text, string Insert, int CaretBack, string Description)
{
    public static XmlSuggestion Element(string name, string description, bool empty) =>
        empty
            ? new XmlSuggestion(name, $"{name}/>", 2, description)
            : new XmlSuggestion(name, $"{name}></{name}>", name.Length + 3, description);

    public static XmlSuggestion Attribute(string name, string description) => new(name, $"{name}=\"\"", 1, description);

    public static XmlSuggestion Value(string value) => new(value, value, 0, string.Empty);
}

/// <summary>Автодополнение исходного XML по каталогу DITA — общее для редакторов обеих оболочек
/// (логика перенесена из WPF XmlSourceEditor). Что подсказывать, решает <see cref="XmlContext"/>:
/// имя элемента (допустимые по контент-модели родителя), имя атрибута, значение перечисления.</summary>
public static class XmlCompletion
{
    /// <summary>Подсказки для позиции курсора; <paramref name="textBeforeCaret"/> — текст до курсора
    /// (достаточно последних ~20 000 символов). Prefix — уже набранная часть слова.</summary>
    public static (IReadOnlyList<XmlSuggestion> Items, string Prefix) Suggest(string textBeforeCaret, DitaCatalog? catalog = null)
    {
        var context = XmlContext.Analyze(textBeforeCaret);
        catalog ??= DitaCatalog.Default;
        var result = new List<XmlSuggestion>();

        switch (context.Kind)
        {
            case XmlContextKind.ElementName:
            {
                var parent = context.OpenElements.Count > 0 ? context.OpenElements[^1] : null;
                var def = parent is null ? null : catalog.Get(parent);
                var names = def is null
                    ? catalog.Elements.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList()
                    : def.Automaton.AllowedNames.ToList();

                foreach (var name in names)
                {
                    var child = catalog.Get(name);
                    result.Add(XmlSuggestion.Element(name, child?.Description ?? string.Empty, child?.IsEmpty ?? false));
                }

                break;
            }

            case XmlContextKind.AttributeName:
                if (catalog.Get(context.CurrentElement ?? string.Empty) is { } element)
                {
                    result.AddRange(element.Attributes.Values
                        .OrderBy(a => a.Name, StringComparer.Ordinal)
                        .Select(a => XmlSuggestion.Attribute(a.Name, a.Description)));
                }

                break;

            case XmlContextKind.AttributeValue:
                if (catalog.Get(context.CurrentElement ?? string.Empty) is { } owner && context.CurrentAttribute is not null &&
                    owner.Attributes.TryGetValue(context.CurrentAttribute, out var attr))
                {
                    result.AddRange(attr.Values.Select(XmlSuggestion.Value));
                }

                break;
        }

        if (context.Prefix.Length > 0)
        {
            result = result.Where(i => i.Text.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return (result, context.Prefix);
    }
}

public enum XmlContextKind
{
    None,
    ElementName,
    AttributeName,
    AttributeValue,
    Content
}

/// <summary>Разбор состояния разметки перед курсором — что уместно подсказать.</summary>
public sealed class XmlContext
{
    public XmlContextKind Kind { get; private init; } = XmlContextKind.None;

    public string Prefix { get; private init; } = string.Empty;

    public string? CurrentElement { get; private init; }

    public string? CurrentAttribute { get; private init; }

    public List<string> OpenElements { get; private init; } = new();

    public static XmlContext Analyze(string before)
    {
        var open = new List<string>();
        var i = 0;
        var insideTag = false;
        var tagStart = 0;

        while (i < before.Length)
        {
            var ch = before[i];
            if (ch == '<')
            {
                insideTag = true;
                tagStart = i;
            }
            else if (ch == '>' && insideTag)
            {
                insideTag = false;
                ApplyTag(before[tagStart..(i + 1)], open);
            }

            i++;
        }

        if (!insideTag)
        {
            return new XmlContext
            {
                Kind = XmlContextKind.Content,
                OpenElements = open,
                CurrentElement = open.Count > 0 ? open[^1] : null
            };
        }

        var current = before[tagStart..];
        if (current.StartsWith("<!", StringComparison.Ordinal) || current.StartsWith("<?", StringComparison.Ordinal))
        {
            return new XmlContext { Kind = XmlContextKind.None, OpenElements = open };
        }

        var nameEnd = 1;
        while (nameEnd < current.Length && (char.IsLetterOrDigit(current[nameEnd]) || current[nameEnd] is '-' or '_' or ':'))
        {
            nameEnd++;
        }

        if (nameEnd >= current.Length)
        {
            return new XmlContext
            {
                Kind = XmlContextKind.ElementName,
                Prefix = current[1..],
                OpenElements = open,
                CurrentElement = open.Count > 0 ? open[^1] : null
            };
        }

        var elementName = current[1..nameEnd];
        var rest = current[nameEnd..];

        if (rest.Count(c => c == '"') % 2 == 1)
        {
            var lastQuote = rest.LastIndexOf('"');
            var beforeQuote = rest[..lastQuote].TrimEnd();
            var eq = beforeQuote.LastIndexOf('=');
            var attrName = eq > 0 ? beforeQuote[..eq].Trim().Split(' ', '\t', '\n').Last() : null;
            return new XmlContext
            {
                Kind = XmlContextKind.AttributeValue,
                Prefix = rest[(lastQuote + 1)..],
                CurrentElement = elementName,
                CurrentAttribute = attrName,
                OpenElements = open
            };
        }

        var tail = rest.Split(' ', '\t', '\n').Last();
        return new XmlContext
        {
            Kind = XmlContextKind.AttributeName,
            Prefix = tail.Contains('=') ? string.Empty : tail,
            CurrentElement = elementName,
            OpenElements = open
        };
    }

    private static void ApplyTag(string tag, List<string> open)
    {
        if (tag.StartsWith("<!", StringComparison.Ordinal) || tag.StartsWith("<?", StringComparison.Ordinal))
        {
            return;
        }

        if (tag.StartsWith("</", StringComparison.Ordinal))
        {
            if (open.Count > 0)
            {
                open.RemoveAt(open.Count - 1);
            }

            return;
        }

        if (tag.EndsWith("/>", StringComparison.Ordinal))
        {
            return;
        }

        var name = new StringBuilder();
        for (var i = 1; i < tag.Length; i++)
        {
            var ch = tag[i];
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or ':')
            {
                name.Append(ch);
            }
            else
            {
                break;
            }
        }

        if (name.Length > 0)
        {
            open.Add(name.ToString());
        }
    }
}
