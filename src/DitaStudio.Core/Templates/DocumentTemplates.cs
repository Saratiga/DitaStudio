using System.Text;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Templates;

public sealed record DocumentTemplate(string Key, string RootElement, string DisplayName, string Description, string Extension)
{
    public override string ToString() => DisplayName;
}

/// <summary>Заготовки новых документов DITA.</summary>
public static class DocumentTemplates
{
    public static readonly IReadOnlyList<DocumentTemplate> All = new List<DocumentTemplate>
    {
        new("concept", "concept", "Концепция", "Объясняет, что это такое и зачем нужно", ".dita"),
        new("task", "task", "Задача", "Пошаговая инструкция", ".dita"),
        new("reference", "reference", "Справка", "Таблицы, параметры, синтаксис", ".dita"),
        new("troubleshooting", "troubleshooting", "Устранение неполадки", "Признак, причина, решение", ".dita"),
        new("topic", "topic", "Универсальный топик", "Свободная структура", ".dita"),
        new("glossentry", "glossentry", "Статья глоссария", "Термин и его определение", ".dita"),
        new("map", "map", "Карта", "Структура публикации", ".ditamap"),
        new("bookmap", "bookmap", "Карта книги", "Главы, части, приложения", ".ditamap"),
        new("subjectScheme", "subjectScheme", "Схема категорий", "Значения условных атрибутов", ".ditamap")
    };

    public static DocumentTemplate? Find(string key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    public static DitaDocument Create(string templateKey, string title, string? id = null)
    {
        var template = Find(templateKey) ?? All[0];
        id ??= SuggestId(title, template.RootElement);

        var xml = template.Key switch
        {
            "concept" => Concept(id, title),
            "task" => Task(id, title),
            "reference" => Reference(id, title),
            "troubleshooting" => Troubleshooting(id, title),
            "glossentry" => GlossEntry(id, title),
            "map" => Map(title),
            "bookmap" => BookMap(title),
            "subjectScheme" => SubjectScheme(title),
            _ => Topic(id, title)
        };

        return DitaDocument.Parse(xml);
    }

    public static string SuggestId(string title, string prefix)
    {
        var sb = new StringBuilder();
        foreach (var ch in title.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(ch);
            }
            else if (Translit.TryGetValue(ch, out var replacement))
            {
                sb.Append(replacement);
            }
            else if (ch is ' ' or '-' or '_')
            {
                if (sb.Length > 0 && sb[^1] != '_')
                {
                    sb.Append('_');
                }
            }
        }

        var result = sb.ToString().Trim('_');
        if (result.Length == 0)
        {
            result = prefix;
        }

        if (char.IsDigit(result[0]))
        {
            result = prefix + "_" + result;
        }

        return result.Length > 60 ? result[..60].TrimEnd('_') : result;
    }

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = string.Empty,
        ['ы'] = "y", ['ь'] = string.Empty, ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
    };

    private static string Header(string root, string publicId, string systemId) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE {root} PUBLIC \"{publicId}\" \"{systemId}\">\n";

    private static string E(string value) => Model.XmlSerializer.EscapeText(value);

    private static string Topic(string id, string title) =>
        Header("topic", "-//OASIS//DTD DITA Topic//EN", "topic.dtd") +
        $"""
        <topic id="{id}" xml:lang="ru-RU">
          <title>{E(title)}</title>
          <shortdesc></shortdesc>
          <body>
            <p></p>
          </body>
        </topic>
        """;

    private static string Concept(string id, string title) =>
        Header("concept", "-//OASIS//DTD DITA Concept//EN", "concept.dtd") +
        $"""
        <concept id="{id}" xml:lang="ru-RU">
          <title>{E(title)}</title>
          <shortdesc></shortdesc>
          <conbody>
            <p></p>
          </conbody>
        </concept>
        """;

    private static string Task(string id, string title) =>
        Header("task", "-//OASIS//DTD DITA Task//EN", "task.dtd") +
        $"""
        <task id="{id}" xml:lang="ru-RU">
          <title>{E(title)}</title>
          <shortdesc></shortdesc>
          <taskbody>
            <context>
              <p></p>
            </context>
            <steps>
              <step>
                <cmd></cmd>
              </step>
            </steps>
            <result>
              <p></p>
            </result>
          </taskbody>
        </task>
        """;

    private static string Reference(string id, string title) =>
        Header("reference", "-//OASIS//DTD DITA Reference//EN", "reference.dtd") +
        $"""
        <reference id="{id}" xml:lang="ru-RU">
          <title>{E(title)}</title>
          <shortdesc></shortdesc>
          <refbody>
            <section>
              <title>Описание</title>
              <p></p>
            </section>
          </refbody>
        </reference>
        """;

    private static string Troubleshooting(string id, string title) =>
        Header("troubleshooting", "-//OASIS//DTD DITA Troubleshooting//EN", "troubleshooting.dtd") +
        $"""
        <troubleshooting id="{id}" xml:lang="ru-RU">
          <title>{E(title)}</title>
          <shortdesc></shortdesc>
          <troublebody>
            <condition>
              <p></p>
            </condition>
            <troubleSolution>
              <cause>
                <p></p>
              </cause>
              <remedy>
                <steps>
                  <step>
                    <cmd></cmd>
                  </step>
                </steps>
              </remedy>
            </troubleSolution>
          </troublebody>
        </troubleshooting>
        """;

    private static string GlossEntry(string id, string title) =>
        Header("glossentry", "-//OASIS//DTD DITA Glossary Entry//EN", "glossentry.dtd") +
        $"""
        <glossentry id="{id}" xml:lang="ru-RU">
          <glossterm>{E(title)}</glossterm>
          <glossdef>
            <p></p>
          </glossdef>
        </glossentry>
        """;

    private static string Map(string title) =>
        Header("map", "-//OASIS//DTD DITA Map//EN", "map.dtd") +
        $"""
        <map xml:lang="ru-RU">
          <title>{E(title)}</title>
        </map>
        """;

    private static string BookMap(string title) =>
        Header("bookmap", "-//OASIS//DTD DITA BookMap//EN", "bookmap.dtd") +
        $"""
        <bookmap xml:lang="ru-RU">
          <booktitle>
            <mainbooktitle>{E(title)}</mainbooktitle>
          </booktitle>
          <frontmatter>
            <booklists>
              <toc/>
            </booklists>
          </frontmatter>
        </bookmap>
        """;

    private static string SubjectScheme(string title) =>
        Header("subjectScheme", "-//OASIS//DTD DITA Subject Scheme Map//EN", "subjectScheme.dtd") +
        $"""
        <subjectScheme xml:lang="ru-RU">
          <title>{E(title)}</title>
        </subjectScheme>
        """;
}
