using System.Text.RegularExpressions;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Project;

public sealed partial class DitaProject
{
    // ---------------------------------------------------------------- поиск

    public sealed record SearchHit(ProjectFile File, DitaNode Node, string Context);

    public sealed record ReplaceResult(int ReplacementCount, IReadOnlyList<ProjectFile> ChangedFiles);

    private static Regex? TryBuildRegex(string pattern, bool caseSensitive)
    {
        try
        {
            return new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Все ссылки проекта на файл <paramref name="targetPath"/> — @href и @conref в картах и топиках
    /// (topicref, keydef, xref, conref…), кроме внешних.
    /// </summary>
    public IReadOnlyList<SearchHit> FindReferencesTo(string targetPath)
    {
        var target = System.IO.Path.GetFullPath(targetPath);
        var result = new List<SearchHit>();
        foreach (var file in _files)
        {
            // Ссылки файла на самого себя (href="#id") — не ссылки «на файл».
            if (string.Equals(file.FullPath, target, StringComparison.OrdinalIgnoreCase) ||
                TryGetDocument(file.FullPath) is not { } doc)
            {
                continue;
            }

            foreach (var node in doc.Root.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element))
            {
                foreach (var attribute in new[] { "href", "conref" })
                {
                    var value = node.GetAttribute(attribute);
                    if (string.IsNullOrWhiteSpace(value) || RefResolver.IsExternal(value!) ||
                        node.GetAttribute("scope") is "external" or "peer")
                    {
                        continue;
                    }

                    var reference = RefResolver.Parse(file.FullPath, value!);
                    if (reference.Path is not null && string.Equals(System.IO.Path.GetFullPath(reference.Path), target, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(new SearchHit(file, node, $"<{node.Name} {attribute}=\"{value}\">"));
                    }
                }
            }
        }

        return result;
    }

    public IReadOnlyList<SearchHit> Search(string query, bool caseSensitive = false, bool elementNames = false,
        bool regex = false)
    {
        var result = new List<SearchHit>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Regex? pattern = null;
        if (regex)
        {
            pattern = TryBuildRegex(query, caseSensitive);
            if (pattern is null)
            {
                return result;
            }
        }

        foreach (var file in _files)
        {
            var doc = TryGetDocument(file.FullPath);
            if (doc is null)
            {
                continue;
            }

            foreach (var node in doc.Root.DescendantsAndSelf())
            {
                var hit = elementNames
                    ? MatchElementName(node, file, pattern, query, comparison)
                    : MatchText(node, file, pattern, query, comparison);

                if (hit is not null)
                {
                    result.Add(hit);
                }
            }
        }

        return result;
    }

    private static SearchHit? MatchElementName(
        DitaNode node, ProjectFile file, Regex? pattern, string query, StringComparison comparison)
    {
        if (node.Kind != NodeKind.Element)
        {
            return null;
        }

        var isMatch = pattern?.IsMatch(node.Name) ?? node.Name.Equals(query, comparison);
        return isMatch ? new SearchHit(file, node, node.Path) : null;
    }

    private static SearchHit? MatchText(
        DitaNode node, ProjectFile file, Regex? pattern, string query, StringComparison comparison)
    {
        if (node.Kind != NodeKind.Text)
        {
            return null;
        }

        int index;
        int matchLength;
        if (pattern is not null)
        {
            var match = pattern.Match(node.Value);
            if (!match.Success)
            {
                return null;
            }

            index = match.Index;
            matchLength = match.Length;
        }
        else
        {
            index = node.Value.IndexOf(query, comparison);
            if (index < 0)
            {
                return null;
            }

            matchLength = query.Length;
        }

        var start = Math.Max(0, index - 30);
        var length = Math.Min(node.Value.Length - start, matchLength + 60);
        var context = node.Value.Substring(start, length).Replace('\n', ' ').Trim();
        return new SearchHit(file, node.Parent ?? node, context);
    }

    /// <summary>Заменяет все вхождения запроса во всех текстовых узлах проекта. Изменённые
    /// документы помечаются как несохранённые (IsDirty) — запись на диск делает пользователь
    /// (Ctrl+Shift+S), как и после любой другой структурной правки.</summary>
    public ReplaceResult ReplaceAll(string query, string replacement, bool caseSensitive = false, bool regex = false)
    {
        var changed = new List<ProjectFile>();
        var count = 0;
        if (string.IsNullOrEmpty(query))
        {
            return new ReplaceResult(0, changed);
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Regex? pattern = null;
        if (regex)
        {
            pattern = TryBuildRegex(query, caseSensitive);
            if (pattern is null)
            {
                return new ReplaceResult(0, changed);
            }
        }

        foreach (var file in _files)
        {
            var doc = TryGetDocument(file.FullPath);
            if (doc is null)
            {
                continue;
            }

            var fileChanged = false;
            foreach (var node in doc.Root.DescendantsAndSelf())
            {
                if (node.Kind != NodeKind.Text)
                {
                    continue;
                }

                if (pattern is not null)
                {
                    var matches = pattern.Matches(node.Value);
                    if (matches.Count == 0)
                    {
                        continue;
                    }

                    node.Value = pattern.Replace(node.Value, replacement);
                    count += matches.Count;
                }
                else
                {
                    var occurrences = CountOccurrences(node.Value, query, comparison);
                    if (occurrences == 0)
                    {
                        continue;
                    }

                    node.Value = node.Value.Replace(query, replacement, comparison);
                    count += occurrences;
                }

                fileChanged = true;
            }

            if (fileChanged)
            {
                doc.IsDirty = true;
                changed.Add(file);
            }
        }

        return new ReplaceResult(count, changed);
    }

    private static int CountOccurrences(string text, string query, StringComparison comparison)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(query, index, comparison)) >= 0)
        {
            count++;
            index += query.Length;
        }

        return count;
    }
}
