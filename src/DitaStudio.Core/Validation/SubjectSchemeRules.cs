using System.Text.RegularExpressions;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Core.Validation;

/// <summary>
/// Проверка значений условных атрибутов по схемам категорий проекта (<c>subjectScheme</c>): <c>enumerationdef</c> задаёт, какие значения
/// допустимы у атрибута (<c>attributedef</c>), а допустимые значения — это категории (<c>subjectdef</c>), перечисленные в нём
/// или на которые он ссылается через <c>keyref</c> (берутся вложенные категории, нет вложенных — сама категория). Нет схем — проверки нет.
/// </summary>
public sealed class SubjectSchemeRules
{
    private sealed record Enumeration(string Attribute, HashSet<string> Elements, HashSet<string> Values);

    private static readonly Regex Group = new(@"(?<name>[\w:.-]+)\((?<values>[^)]*)\)", RegexOptions.Compiled);

    private readonly List<Enumeration> _enumerations;

    private SubjectSchemeRules(List<Enumeration> enumerations) => _enumerations = enumerations;

    /// <summary>Схем категорий в проекте нет или в них нет перечислений — проверять нечего.</summary>
    public bool IsEmpty => _enumerations.Count == 0;

    public static SubjectSchemeRules Build(DitaProject project)
    {
        var schemes = new List<DitaDocument>();
        foreach (var file in project.Maps)
        {
            try
            {
                if (project.TryGetDocument(file.FullPath) is { } scheme && scheme.Root.Name == "subjectScheme")
                {
                    schemes.Add(scheme);
                }
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                // нечитаемая схема сама попадёт в проверку проекта как ошибка разбора
            }
        }

        // Определения категорий по ключам — на них ссылаются перечисления.
        var definitions = new Dictionary<string, DitaNode>(StringComparer.Ordinal);
        foreach (var scheme in schemes)
        {
            foreach (var def in scheme.Root.DescendantsAndSelf().Where(n => n.Name == "subjectdef"))
            {
                foreach (var key in Tokens(def.GetAttribute("keys")))
                {
                    definitions.TryAdd(key, def);
                }
            }
        }

        var byAttribute = new Dictionary<string, Enumeration>(StringComparer.Ordinal);
        foreach (var scheme in schemes)
        {
            foreach (var enumeration in scheme.Root.DescendantsAndSelf().Where(n => n.Name == "enumerationdef"))
            {
                var values = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in enumeration.ElementChildren().Where(n => n.Name == "subjectdef"))
                {
                    var def = child.GetAttribute("keyref") is { Length: > 0 } reference && definitions.TryGetValue(reference, out var found) ? found : child;
                    CollectValues(def, values);
                }

                var elements = enumeration.ElementChildren().Where(n => n.Name == "elementdef")
                    .Select(n => n.GetAttribute("name")).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
                foreach (var attribute in enumeration.ElementChildren().Where(n => n.Name == "attributedef")
                             .Select(n => n.GetAttribute("name")).Where(n => !string.IsNullOrWhiteSpace(n)))
                {
                    if (!byAttribute.TryGetValue(attribute!, out var existing))
                    {
                        existing = new Enumeration(attribute!, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
                        byAttribute[attribute!] = existing;
                    }

                    // Перечисления одного атрибута складываются; без elementdef они действуют на все элементы.
                    existing.Values.UnionWith(values);
                    existing.Elements.UnionWith(elements);
                }
            }
        }

        return new SubjectSchemeRules(byAttribute.Values.Where(e => e.Values.Count > 0).ToList());
    }

    /// <summary>Значения условных атрибутов документа, которых нет в схемах: предупреждение на каждое.</summary>
    public IEnumerable<ValidationIssue> Check(DitaDocument document)
    {
        if (IsEmpty)
        {
            yield break;
        }

        foreach (var node in document.Root.DescendantsAndSelf())
        {
            foreach (var enumeration in _enumerations)
            {
                if (enumeration.Elements.Count > 0 && !enumeration.Elements.Contains(node.Name))
                {
                    continue;
                }

                // Значения: у самого атрибута (токены через пробел) и из групповой записи props="атрибут(значение значение)".
                var tokens = new List<string>();
                if (node.GetAttribute(enumeration.Attribute) is { } value && !string.IsNullOrWhiteSpace(value))
                {
                    tokens.AddRange(enumeration.Attribute == "props" ? PlainTokens(value) : Tokens(value));
                }

                if (node.GetAttribute("props") is { } props && props.Contains('('))
                {
                    foreach (Match group in Group.Matches(props))
                    {
                        if (group.Groups["name"].Value == enumeration.Attribute)
                        {
                            tokens.AddRange(Tokens(group.Groups["values"].Value));
                        }
                    }
                }

                foreach (var token in tokens)
                {
                    if (!enumeration.Values.Contains(token))
                    {
                        var allowed = string.Join(", ", enumeration.Values.OrderBy(v => v, StringComparer.Ordinal).Take(12)) +
                                      (enumeration.Values.Count > 12 ? "…" : string.Empty);
                        yield return new ValidationIssue(IssueSeverity.Warning,
                            Loc.T("Core_TheValue0OfTheAttribute1", token, enumeration.Attribute, allowed), node, document.FilePath);
                    }
                }
            }
        }
    }

    // Значения категорий внутри определения: вложенные subjectdef, а у определения без вложенных — оно само.
    private static void CollectValues(DitaNode def, HashSet<string> values)
    {
        var nested = def.ElementChildren().Where(n => n.Name == "subjectdef").ToList();
        if (nested.Count == 0)
        {
            values.UnionWith(Tokens(def.GetAttribute("keys")));
            return;
        }

        foreach (var child in nested)
        {
            values.UnionWith(Tokens(child.GetAttribute("keys")));
            CollectValues(child, values);
        }
    }

    // Значение props без групповых записей «атрибут(…)»: они проверяются по своим атрибутам.
    private static IEnumerable<string> PlainTokens(string value) => Tokens(Group.Replace(value, " "));

    private static IEnumerable<string> Tokens(string? value) =>
        (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
