using DitaStudio.Core.Model;

namespace DitaStudio.Core.Project;

/// <summary>Блок с пометкой правки (<c>status</c>): файл, путь к элементу (индексы среди элементов от корня), элемент, статус и начало текста.</summary>
public sealed record ProjectChange(ProjectFile File, IReadOnlyList<int> Path, string Element, string Status, string Text);

/// <summary>Правки по проекту: элементы с атрибутом <c>status</c> (<c>new</c>, <c>changed</c>, <c>deleted</c>) во всех топиках и картах.</summary>
public static class ProjectChanges
{
    /// <summary>Статусы правок, которые показывает панель «Правки».</summary>
    public static readonly IReadOnlyList<string> Statuses = new[] { "new", "changed", "deleted" };

    private const int TextLimit = 80;

    /// <summary>Все правки проекта в порядке файлов и документа. Нечитаемые файлы пропускаются (они видны в проверке проекта).</summary>
    public static IReadOnlyList<ProjectChange> Find(DitaProject project)
    {
        var result = new List<ProjectChange>();
        foreach (var file in project.Files)
        {
            DitaDocument? document;
            try
            {
                document = project.TryGetDocument(file.FullPath);
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                continue;
            }

            if (document is null)
            {
                continue;
            }

            Collect(file, document.Root, new List<int>(), result);
        }

        return result;
    }

    private static void Collect(ProjectFile file, DitaNode node, List<int> path, List<ProjectChange> result)
    {
        if (node.GetAttribute("status") is { } status && Statuses.Contains(status))
        {
            var text = string.Join(' ', node.InnerText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            result.Add(new ProjectChange(file, path.ToArray(), node.Name, status, text.Length > TextLimit ? text[..(TextLimit - 1)] + "…" : text));
        }

        var index = 0;
        foreach (var child in node.ElementChildren())
        {
            path.Add(index++);
            Collect(file, child, path, result);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>Элемент документа по пути из <see cref="ProjectChange.Path"/>; null — документ с тех пор изменился и пути нет.</summary>
    public static DitaNode? Resolve(DitaDocument document, IReadOnlyList<int> path)
    {
        var node = document.Root;
        foreach (var index in path)
        {
            if (node.ElementChildren().ElementAtOrDefault(index) is not { } child)
            {
                return null;
            }

            node = child;
        }

        return node;
    }
}
