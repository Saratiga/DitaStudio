using System.Xml;

namespace DitaStudio.Core.Project;

/// <summary>Правило подсветки (<c>action="flag"</c>) из .ditaval — цвет текста/фона, начертание,
/// полоса изменений. <see cref="Value"/> = null означает «при любом значении атрибута».</summary>
public sealed record DitavalFlagRule(string Attribute, string? Value, string? Color, string? BackgroundColor, string? Style, string? ChangeBar);

/// <summary>Правила условной сборки, прочитанные из одного .ditaval-файла.</summary>
public sealed record DitavalRules(Dictionary<string, HashSet<string>> Exclude, List<DitavalFlagRule> Flags);

/// <summary>Читает файл условий сборки в формате DITAVAL: <c>action="exclude"</c> и
/// <c>action="flag"</c> (цвет/фон/начертание/полоса изменений). Остальные действия (include,
/// passthrough) не поддерживаются и пропускаются.</summary>
public static class DitavalReader
{
    public static DitavalRules Read(string path)
    {
        var exclude = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var flags = new List<DitavalFlagRule>();

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null, // внешние DTD не загружаем
            MaxCharactersFromEntities = 0
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
                    ReadExcludeRule(reader, attribute!, exclude);
                    break;
                case "flag":
                    flags.Add(ReadFlagRule(reader, attribute!));
                    break;
            }
        }

        return new DitavalRules(exclude, flags);
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
