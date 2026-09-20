using DitaStudio.Core.Model;

namespace DitaStudio.Core.Schema;

/// <summary>
/// Каталог DITA 1.3: элементы, их контент-модели, атрибуты и служебные сведения
/// (DOCTYPE, домены, читаемые описания). Загружается из компактного текстового
/// описания в <see cref="CatalogSource"/> — аналог набора DTD-модулей.
/// </summary>
public sealed class DitaCatalog
{
    private static readonly Lazy<DitaCatalog> Lazy = new(() => Load(CatalogSource.All));

    private readonly Dictionary<string, ElementDef> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AttributeDef> _attrLibrary = new(StringComparer.Ordinal);

    public static DitaCatalog Default => Lazy.Value;

    public IReadOnlyDictionary<string, ElementDef> Elements => _elements;

    public ElementDef? Get(string name) => _elements.TryGetValue(name, out var d) ? d : null;

    public bool IsKnown(string name) => _elements.ContainsKey(name);

    public bool IsInline(string name) => Get(name)?.IsInline ?? false;

    public bool IsMixed(string name) => Get(name)?.IsMixed ?? false;

    public IEnumerable<ElementDef> TopicTypes => _elements.Values.Where(e => e.IsTopicType);

    public IEnumerable<ElementDef> MapTypes => _elements.Values.Where(e => e.IsMapType);

    // ------------------------------------------------------------------ DOCTYPE

    private static readonly Dictionary<string, (string Public, string System)> Doctypes = new(StringComparer.Ordinal)
    {
        ["topic"] = ("-//OASIS//DTD DITA Topic//EN", "topic.dtd"),
        ["concept"] = ("-//OASIS//DTD DITA Concept//EN", "concept.dtd"),
        ["task"] = ("-//OASIS//DTD DITA Task//EN", "task.dtd"),
        ["reference"] = ("-//OASIS//DTD DITA Reference//EN", "reference.dtd"),
        ["troubleshooting"] = ("-//OASIS//DTD DITA Troubleshooting//EN", "troubleshooting.dtd"),
        ["glossentry"] = ("-//OASIS//DTD DITA Glossary Entry//EN", "glossentry.dtd"),
        ["glossgroup"] = ("-//OASIS//DTD DITA Glossary Group//EN", "glossgroup.dtd"),
        ["map"] = ("-//OASIS//DTD DITA Map//EN", "map.dtd"),
        ["bookmap"] = ("-//OASIS//DTD DITA BookMap//EN", "bookmap.dtd"),
        ["subjectScheme"] = ("-//OASIS//DTD DITA Subject Scheme Map//EN", "subjectScheme.dtd"),
        ["learningBase"] = ("-//OASIS//DTD DITA Learning Base//EN", "learningBase.dtd"),
        ["learningOverview"] = ("-//OASIS//DTD DITA Learning Overview//EN", "learningOverview.dtd"),
        ["learningContent"] = ("-//OASIS//DTD DITA Learning Content//EN", "learningContent.dtd"),
        ["learningSummary"] = ("-//OASIS//DTD DITA Learning Summary//EN", "learningSummary.dtd"),
        ["learningAssessment"] = ("-//OASIS//DTD DITA Learning Assessment//EN", "learningAssessment.dtd"),
        ["learningPlan"] = ("-//OASIS//DTD DITA Learning Plan//EN", "learningPlan.dtd")
    };

    public string? PublicIdFor(string rootName) =>
        Doctypes.TryGetValue(rootName, out var d) ? d.Public : null;

    public string? SystemIdFor(string rootName) =>
        Doctypes.TryGetValue(rootName, out var d) ? d.System : null;

    // ------------------------------------------------------------------ загрузка

    public static DitaCatalog Load(string source)
    {
        var catalog = new DitaCatalog();
        var currentDomain = "topic";

        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("@domain ", StringComparison.Ordinal))
            {
                currentDomain = line["@domain ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("@group ", StringComparison.Ordinal))
            {
                var body = line["@group ".Length..];
                var eq = body.IndexOf('=');
                if (eq > 0)
                {
                    var name = body[..eq].Trim();
                    var value = body[(eq + 1)..].Trim();
                    catalog._entities[name] = value;
                }

                continue;
            }

            if (line.StartsWith("@attrdef ", StringComparison.Ordinal))
            {
                // @attrdef имя :: описание
                var body = line["@attrdef ".Length..];
                var parts = body.Split("::", 2);
                var spec = parts[0].Trim();
                var descr = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                var def = ParseAttribute(spec, descr);
                if (def is not null)
                {
                    catalog._attrLibrary[def.Name] = def;
                }

                continue;
            }

            var cols = line.Split("::");
            if (cols.Length < 4)
            {
                continue;
            }

            var elementName = cols[0].Trim();
            var classAttr = cols[1].Trim();
            var display = ParseDisplay(cols[2].Trim());
            var modelText = ModelParser.ExpandEntities(cols[3].Trim(), catalog._entities);
            var attrText = cols.Length > 4 ? cols[4].Trim() : string.Empty;
            var description = cols.Length > 5 ? cols[5].Trim() : string.Empty;

            var attributes = catalog.BuildAttributes(attrText);

            catalog._elements[elementName] = new ElementDef(
                elementName, classAttr, display, modelText, attributes, description)
            {
                Domain = currentDomain
            };
        }

        return catalog;
    }

    private static DisplayKind ParseDisplay(string value) => value switch
    {
        "block" => DisplayKind.Block,
        "inline" => DisplayKind.Inline,
        "container" => DisplayKind.Container,
        "topic" => DisplayKind.Topic,
        "map" => DisplayKind.Map,
        "table" => DisplayKind.Table,
        "meta" => DisplayKind.Meta,
        "empty" => DisplayKind.Empty,
        "pre" => DisplayKind.Preformatted,
        _ => DisplayKind.Container
    };

    private Dictionary<string, AttributeDef> BuildAttributes(string attrText)
    {
        var result = new Dictionary<string, AttributeDef>(StringComparer.Ordinal);
        var expanded = ModelParser.ExpandEntities(attrText, _entities);
        foreach (var token in expanded.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = BaseAttributeName(token);
            var description = _attrLibrary.TryGetValue(name, out var byName) ? byName.Description : string.Empty;
            var def = ParseAttribute(token, description);
            if (def is not null)
            {
                result[def.Name] = def;
            }
        }

        return result;
    }

    /// <summary>Имя атрибута без модификаторов: id#ID -> id, scope(local|peer)=local -> scope.</summary>
    private static string BaseAttributeName(string token)
    {
        var cut = token.Length;
        foreach (var ch in new[] { '(', '#', '=', '!' })
        {
            var i = token.IndexOf(ch);
            if (i > 0 && i < cut)
            {
                cut = i;
            }
        }

        return token[..cut];
    }

    /// <summary>
    /// Разбор описания атрибута: name, name!, name(a|b|c), name(a|b|c)=b, name#ID.
    /// </summary>
    private static AttributeDef? ParseAttribute(string spec, string description)
    {
        spec = spec.Trim();
        if (spec.Length == 0)
        {
            return null;
        }

        var required = false;
        if (spec.EndsWith("!", StringComparison.Ordinal))
        {
            required = true;
            spec = spec[..^1];
        }

        string? defaultValue = null;
        var values = Array.Empty<string>();
        var type = AttrType.CData;

        var openParen = spec.IndexOf('(');
        if (openParen > 0)
        {
            var closeParen = spec.IndexOf(')', openParen);
            if (closeParen > openParen)
            {
                values = spec[(openParen + 1)..closeParen]
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .ToArray();
                type = AttrType.Enumeration;

                var rest = spec[(closeParen + 1)..];
                if (rest.StartsWith("=", StringComparison.Ordinal))
                {
                    defaultValue = rest[1..].Trim();
                }

                spec = spec[..openParen];
            }
        }
        else
        {
            var hash = spec.IndexOf('#');
            if (hash > 0)
            {
                var kind = spec[(hash + 1)..];
                type = kind switch
                {
                    "ID" => AttrType.Id,
                    "IDREF" => AttrType.IdRef,
                    "NMTOKEN" => AttrType.NmToken,
                    _ => AttrType.CData
                };
                spec = spec[..hash];
            }
            else
            {
                var eq = spec.IndexOf('=');
                if (eq > 0)
                {
                    defaultValue = spec[(eq + 1)..].Trim().Trim('"');
                    spec = spec[..eq];
                }
            }
        }

        var name = spec.Trim();
        return name.Length == 0
            ? null
            : new AttributeDef(name, type, values, required, defaultValue, description);
    }

    // ------------------------------------------------------- запросы для редактора

    /// <summary>Имена элементов-детей узла (без текста и комментариев).</summary>
    public static List<string> ChildNames(DitaNode node)
    {
        var names = new List<string>();
        foreach (var c in node.Children)
        {
            if (c.Kind == NodeKind.Element)
            {
                names.Add(c.Name);
            }
        }

        return names;
    }

    /// <summary>Индекс среди элементов-детей, соответствующий позиции в общем списке детей.</summary>
    public static int ElementIndexFor(DitaNode parent, int childIndex)
    {
        var index = 0;
        for (var i = 0; i < childIndex && i < parent.Children.Count; i++)
        {
            if (parent.Children[i].Kind == NodeKind.Element)
            {
                index++;
            }
        }

        return index;
    }

    /// <summary>Какие элементы допустимо вставить в parent на позицию (индекс среди элементов).</summary>
    public IReadOnlyList<ElementDef> InsertableAt(DitaNode parent, int elementIndex)
    {
        var def = Get(parent.Name);
        if (def is null)
        {
            return Array.Empty<ElementDef>();
        }

        var names = ChildNames(parent);
        if (elementIndex < 0)
        {
            elementIndex = 0;
        }

        if (elementIndex > names.Count)
        {
            elementIndex = names.Count;
        }

        var result = new List<ElementDef>();
        foreach (var candidate in def.Automaton.InsertableAt(names, elementIndex))
        {
            var cd = Get(candidate);
            if (cd is not null)
            {
                result.Add(cd);
            }
        }

        return result;
    }

    public bool CanInsert(DitaNode parent, string childName, int elementIndex)
    {
        var def = Get(parent.Name);
        if (def is null)
        {
            return false;
        }

        return def.Automaton.CanInsertAt(ChildNames(parent), elementIndex, childName);
    }

    /// <summary>Элементы, которыми можно заменить данный (для «Изменить тип элемента»).</summary>
    public IReadOnlyList<ElementDef> ReplacementsFor(DitaNode node)
    {
        if (node.Parent is null)
        {
            return Array.Empty<ElementDef>();
        }

        var parentDef = Get(node.Parent.Name);
        if (parentDef is null)
        {
            return Array.Empty<ElementDef>();
        }

        var names = DitaCatalog.ChildNames(node.Parent);
        var index = FindElementIndex(node.Parent, node, names.Count);

        var without = new List<string>(names);
        if (index < without.Count)
        {
            without.RemoveAt(index);
        }

        return FilterReplacementCandidates(parentDef, node, without, index);
    }

    /// <summary>Позиция <paramref name="node"/> среди дочерних элементов <paramref name="parent"/> (без текстовых узлов);
    /// <paramref name="fallback"/>, если узел не найден.</summary>
    private static int FindElementIndex(DitaNode parent, DitaNode node, int fallback)
    {
        var counter = 0;
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i].Kind != NodeKind.Element)
            {
                continue;
            }

            if (ReferenceEquals(parent.Children[i], node))
            {
                return counter;
            }

            counter++;
        }

        return fallback;
    }

    private List<ElementDef> FilterReplacementCandidates(ElementDef parentDef, DitaNode node, List<string> without, int index)
    {
        var result = new List<ElementDef>();
        var current = Get(node.Name);

        foreach (var candidate in parentDef.Automaton.AllowedNames)
        {
            if (candidate == node.Name)
            {
                continue;
            }

            if (!parentDef.Automaton.CanInsertAt(without, index, candidate))
            {
                continue;
            }

            var cd = Get(candidate);
            if (cd is null)
            {
                continue;
            }

            // Меняем только на элемент со «совместимым» типом отображения.
            if (current is not null && current.IsInline != cd.IsInline)
            {
                continue;
            }

            result.Add(cd);
        }

        return result;
    }

    /// <summary>Создаёт новый элемент с обязательными детьми и атрибутами по умолчанию.</summary>
    public DitaNode CreateElement(string name)
    {
        var node = DitaNode.Element(name);
        var def = Get(name);
        if (def is null)
        {
            return node;
        }

        foreach (var attr in def.Attributes.Values)
        {
            if (attr.Required && attr.DefaultValue is not null)
            {
                node.SetAttribute(attr.Name, attr.DefaultValue);
            }
        }

        FillRequiredChildren(node, def, 0);
        return node;
    }

    private void FillRequiredChildren(DitaNode node, ElementDef def, int depth)
    {
        if (depth > 6)
        {
            return;
        }

        foreach (var childName in RequiredChildren(def))
        {
            var childDef = Get(childName);
            if (childDef is null)
            {
                continue;
            }

            var child = DitaNode.Element(childName);
            foreach (var attr in childDef.Attributes.Values)
            {
                if (attr.Required && attr.DefaultValue is not null)
                {
                    child.SetAttribute(attr.Name, attr.DefaultValue);
                }
            }

            FillRequiredChildren(child, childDef, depth + 1);
            node.Add(child);
        }
    }

    /// <summary>Обязательные (неповторяемые, безальтернативные) дети верхнего уровня модели.</summary>
    public static IReadOnlyList<string> RequiredChildren(ElementDef def)
    {
        var result = new List<string>();
        Walk(def.Model, result);
        return result;

        static void Walk(ContentModel model, List<string> acc)
        {
            switch (model)
            {
                case ContentModel.Name n:
                    acc.Add(n.Value);
                    break;
                case ContentModel.Sequence s:
                    foreach (var item in s.Items)
                    {
                        Walk(item, acc);
                    }

                    break;
                case ContentModel.Repeat r when r.Min >= 1:
                    Walk(r.Item, acc);
                    break;
            }
        }
    }
}
