using DitaStudio.Core.Model;

namespace DitaStudio.Core.Publishing;

/// <summary>Термин предметного указателя: путь «термин → подтермин», ключ сортировки и отсылки «см.»/«см. также».</summary>
public sealed record IndexTermEntry(IReadOnlyList<string> Path, string? SortAs, IReadOnlyList<string> See, IReadOnlyList<string> SeeAlso);

/// <summary>Разбор <c>indexterm</c> в записи указателя — общий для HTML и DOCX.</summary>
public static class IndexTermReader
{
    /// <summary>Записи элемента <c>indexterm</c> и всех вложенных в него подтерминов (у каждого — полный путь).</summary>
    public static IReadOnlyList<IndexTermEntry> Read(DitaNode indexterm)
    {
        var result = new List<IndexTermEntry>();
        Collect(indexterm, Array.Empty<string>(), result);
        return result;
    }

    /// <summary>Термины пролога топика (<c>prolog/metadata/keywords/indexterm</c>) — только верхнего уровня, вложенные разбираются рекурсивно.</summary>
    public static IEnumerable<DitaNode> TermsIn(DitaNode prolog) =>
        prolog.DescendantsAndSelf().Where(n => n.Name == "indexterm" && n.Parent?.Name != "indexterm");

    private static string TextOf(DitaNode node) =>
        string.Concat(node.DescendantsAndSelf().Where(c => c.Kind == NodeKind.Text).Select(c => c.Value)).Trim();

    private static void Collect(DitaNode node, IReadOnlyList<string> parentPath, List<IndexTermEntry> result)
    {
        var ownText = string.Concat(node.Children.Where(c => c.Kind == NodeKind.Text).Select(c => c.Value)).Trim();
        var path = ownText.Length > 0 ? parentPath.Append(ownText).ToList() : parentPath;
        if (ownText.Length > 0)
        {
            var sortAs = node.ElementChildren().Where(c => c.Name is "index-sort-as" or "sort-as").Select(TextOf).FirstOrDefault(t => t.Length > 0);
            var see = node.ElementChildren().Where(c => c.Name == "index-see").Select(TextOf).Where(t => t.Length > 0).ToList();
            var seeAlso = node.ElementChildren().Where(c => c.Name == "index-see-also").Select(TextOf).Where(t => t.Length > 0).ToList();
            result.Add(new IndexTermEntry(path, sortAs, see, seeAlso));
        }

        foreach (var child in node.ElementChildren().Where(c => c.Name == "indexterm"))
        {
            Collect(child, path, result);
        }
    }
}
