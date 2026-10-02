using System.Xml;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Project;

/// <summary>Правило подсветки (<c>action="flag"</c>) из .ditaval — цвет текста/фона, начертание,
/// полоса изменений. <see cref="Value"/> = null означает «при любом значении атрибута».</summary>
public sealed record DitavalFlagRule(string Attribute, string? Value, string? Color, string? BackgroundColor, string? Style, string? ChangeBar);

/// <summary>Правило <c>action="passthrough"</c>: значение атрибута выводится в HTML как есть (<c>data-атрибут</c>).
/// <see cref="Value"/> = null — при любом значении.</summary>
public sealed record DitavalPassthroughRule(string Attribute, string? Value);

/// <summary>
/// Правила условной сборки, прочитанные из одного .ditaval-файла.
/// </summary>
/// <param name="Exclude">Исключаемые значения: атрибут → значения (<c>exclude</c> с <c>val</c>).</param>
/// <param name="Flags">Правила подсветки (<c>flag</c>).</param>
/// <param name="ExcludeUnlisted">Атрибуты, у которых исключены все значения (<c>exclude</c> без <c>val</c>), кроме возвращённых через <c>include</c>.</param>
/// <param name="Include">Возвращаемые значения: атрибут → значения (<c>include</c> с <c>val</c>).</param>
/// <param name="Passthrough">Правила <c>passthrough</c>.</param>
public sealed record DitavalRules(
    Dictionary<string, HashSet<string>> Exclude,
    List<DitavalFlagRule> Flags,
    HashSet<string>? ExcludeUnlisted = null,
    Dictionary<string, HashSet<string>>? Include = null,
    List<DitavalPassthroughRule>? Passthrough = null);

/// <summary>Читает файл условий сборки в формате DITAVAL: <c>exclude</c>, <c>include</c>, <c>flag</c> (цвет/фон/начертание/полоса
/// изменений) и <c>passthrough</c>. <c>exclude</c> без <c>val</c> исключает все значения атрибута, кроме перечисленных в <c>include</c>.</summary>
public static class DitavalReader
{
    public static DitavalRules Read(string path)
    {
        var exclude = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var flags = new List<DitavalFlagRule>();
        var excludeUnlisted = new HashSet<string>(StringComparer.Ordinal);
        var include = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var passthrough = new List<DitavalPassthroughRule>();

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null, // внешние DTD не загружаем
            MaxCharactersFromEntities = DitaDocument.MaxEntityCharacters
        };

        using var reader = XmlReader.Create(path, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "prop")
            {
                continue;
            }

            var attribute = reader.GetAttribute("att");
            if (string.IsNullOrWhiteSpace(attribute))
            {
                continue;
            }

            switch (reader.GetAttribute("action"))
            {
                case "exclude":
                    if (string.IsNullOrWhiteSpace(reader.GetAttribute("val")))
                    {
                        excludeUnlisted.Add(attribute!);
                    }
                    else
                    {
                        ReadExcludeRule(reader, attribute!, exclude);
                    }

                    break;
                case "include":
                    ReadExcludeRule(reader, attribute!, include);
                    break;
                case "flag":
                    flags.Add(ReadFlagRule(reader, attribute!));
                    break;
                case "passthrough":
                    passthrough.Add(new DitavalPassthroughRule(attribute!, reader.GetAttribute("val") is { Length: > 0 } passed ? passed : null));
                    break;
            }
        }

        return new DitavalRules(exclude, flags, excludeUnlisted, include, passthrough);
    }

    private static void ReadExcludeRule(XmlReader reader, string attribute, Dictionary<string, HashSet<string>> exclude)
    {
        var value = reader.GetAttribute("val");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!exclude.TryGetValue(attribute, out var values))
        {
            values = new HashSet<string>(StringComparer.Ordinal);
            exclude[attribute] = values;
        }

        values.Add(value!);
    }

    private static DitavalFlagRule ReadFlagRule(XmlReader reader, string attribute) => new(
        attribute,
        reader.GetAttribute("val"),
        reader.GetAttribute("color"),
        reader.GetAttribute("backgroundcolor"),
        reader.GetAttribute("style"),
        reader.GetAttribute("changebar"));

    /// <summary>Только правила исключения — для вызовов, которым не нужна подсветка.</summary>
    public static Dictionary<string, HashSet<string>> ReadExcludeRules(string path) => Read(path).Exclude;
}
