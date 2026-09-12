using System.Xml;

namespace DitaStudio.Core.Project;

/// <summary>Читает файл условий сборки в формате DITAVAL (только правила исключения —
/// <c>&lt;prop action="exclude" att="…" val="…"/&gt;</c>). Остальные действия (include,
/// flag, passthrough) не поддерживаются и пропускаются.</summary>
public static class DitavalReader
{
    public static Dictionary<string, HashSet<string>> ReadExcludeRules(string path)
    {
        var exclude = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

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

            var action = reader.GetAttribute("action");
            var attribute = reader.GetAttribute("att");
            var value = reader.GetAttribute("val");
            if (action != "exclude" || string.IsNullOrWhiteSpace(attribute) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!exclude.TryGetValue(attribute!, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                exclude[attribute!] = values;
            }

            values.Add(value!);
        }

        return exclude;
    }
}
