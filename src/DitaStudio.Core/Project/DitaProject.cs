using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Validation;

namespace DitaStudio.Core.Project;

public sealed class ProjectFile
{
    public ProjectFile(string fullPath, string relativePath, DitaDocumentKind kind, string title)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        Kind = kind;
        Title = title;
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    public DitaDocumentKind Kind { get; internal set; }

    public string Title { get; internal set; }

    public string FileName => System.IO.Path.GetFileName(FullPath);

    public string RootElement { get; internal set; } = string.Empty;

    public override string ToString() => RelativePath;
}

/// <summary>Определение ключа из карты (keydef или topicref с @keys).</summary>
public sealed class KeyDefinition
{
    public KeyDefinition(string key, string? href, string? resolvedPath, DitaNode source, string sourceMap, string? scope, string? format)
    {
        Key = key;
        Href = href;
        ResolvedPath = resolvedPath;
        Source = source;
        SourceMap = sourceMap;
        Scope = scope;
        Format = format;
    }

    public string Key { get; }

    public string? Href { get; }

    public string? ResolvedPath { get; }

    public DitaNode Source { get; }

    public string SourceMap { get; }

    public string? Scope { get; }

    public string? Format { get; }

    /// <summary>Текст ключа из topicmeta/keywords/keyword — подставляется вместо keyref.</summary>
    public string? KeyText
    {
        get
        {
            var meta = Source.FirstElement("topicmeta");
            var keywords = meta?.FirstElement("keywords");
            var keyword = keywords?.FirstElement("keyword");
            var text = keyword?.InnerText.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }

            var navtitle = meta?.FirstElement("navtitle")?.InnerText.Trim();
            return string.IsNullOrEmpty(navtitle) ? null : navtitle;
        }
    }

    public DitaNode? KeyContent
    {
        get
        {
            var meta = Source.FirstElement("topicmeta");
            return meta?.FirstElement("keywords")?.FirstElement("keyword");
        }
    }

    public override string ToString() => $"{Key} -> {Href}";
}

/// <summary>
/// Проект документации: папка с топиками и картами. Отвечает за обход файлов,
/// кэш разобранных документов, пространство ключей и поиск.
/// </summary>
public sealed class DitaProject
{
    private static readonly string[] DitaExtensions = { ".dita", ".ditamap", ".bookmap", ".xml", ".ditaval" };

    private readonly Dictionary<string, DitaDocument> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProjectFile> _files = new();
    private readonly Dictionary<string, KeyDefinition> _keys = new(StringComparer.Ordinal);

    public DitaProject(string rootPath)
    {
        RootPath = System.IO.Path.GetFullPath(rootPath);
        Name = new DirectoryInfo(RootPath).Name;
    }

    public string RootPath { get; }

    public string Name { get; }

    public IReadOnlyList<ProjectFile> Files => _files;

    public IReadOnlyDictionary<string, KeyDefinition> Keys => _keys;

    public IEnumerable<ProjectFile> Maps => _files.Where(f => f.Kind == DitaDocumentKind.Map);

    public IEnumerable<ProjectFile> Topics => _files.Where(f => f.Kind == DitaDocumentKind.Topic);

    public event EventHandler? Reloaded;

    // ------------------------------------------------------------------ обход

    public void Scan()
    {
        _files.Clear();
        _cache.Clear();

        foreach (var path in EnumerateFiles(RootPath))
        {
            var relative = System.IO.Path.GetRelativePath(RootPath, path);
            var kind = DitaDocumentKind.Unknown;
            var title = System.IO.Path.GetFileNameWithoutExtension(path);
            var rootName = string.Empty;

            try
            {
                var doc = GetDocument(path);
                kind = doc.Kind;
                title = doc.Title;
                rootName = doc.Root.Name;
            }
            catch
            {
                // Файл с ошибкой разбора всё равно показываем в дереве проекта.
            }

            _files.Add(new ProjectFile(path, relative, kind, title) { RootElement = rootName });
        }

        _files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        RebuildKeySpace();
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subDirs;
            IEnumerable<string> files;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                var name = System.IO.Path.GetFileName(sub);
                if (name.StartsWith(".", StringComparison.Ordinal) ||
                    name is "out" or "bin" or "obj" or "temp" or "node_modules")
                {
                    continue;
                }

                stack.Push(sub);
            }

            foreach (var file in files)
            {
                var ext = System.IO.Path.GetExtension(file);
                if (DitaExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    // ----------------------------------------------------------- документы

    public DitaDocument GetDocument(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (_cache.TryGetValue(full, out var cached))
        {
            return cached;
        }

        var doc = DitaDocument.Load(full);
        _cache[full] = doc;
        return doc;
    }

    public DitaDocument? TryGetDocument(string path)
    {
        try
        {
            return GetDocument(path);
        }
        catch
        {
            return null;
        }
    }

    public bool IsOpen(string path) => _cache.ContainsKey(System.IO.Path.GetFullPath(path));

    public void Register(DitaDocument document)
    {
        if (document.FilePath is null)
        {
            return;
        }

        _cache[System.IO.Path.GetFullPath(document.FilePath)] = document;
    }

    public void Invalidate(string path)
    {
        _cache.Remove(System.IO.Path.GetFullPath(path));
    }

    public IEnumerable<DitaDocument> OpenDocuments => _cache.Values;

    public ProjectFile? FindFile(string fullPath)
    {
        var normalized = System.IO.Path.GetFullPath(fullPath);
        return _files.FirstOrDefault(f =>
            string.Equals(f.FullPath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void AddFile(string fullPath)
    {
        if (FindFile(fullPath) is not null)
        {
            return;
        }

        var relative = System.IO.Path.GetRelativePath(RootPath, fullPath);
        var doc = TryGetDocument(fullPath);
        _files.Add(new ProjectFile(
            fullPath,
            relative,
            doc?.Kind ?? DitaDocumentKind.Unknown,
            doc?.Title ?? System.IO.Path.GetFileNameWithoutExtension(fullPath))
        {
            RootElement = doc?.Root.Name ?? string.Empty
        });
        _files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshFileInfo(string fullPath)
    {
        var file = FindFile(fullPath);
        if (file is null)
        {
            AddFile(fullPath);
            return;
        }

        var doc = TryGetDocument(fullPath);
        if (doc is not null)
        {
            file.Kind = doc.Kind;
            file.Title = doc.Title;
            file.RootElement = doc.Root.Name;
        }
    }

    // ---------------------------------------------------------------- ключи

    public void RebuildKeySpace()
    {
        _keys.Clear();
        foreach (var map in Maps.ToList())
        {
            var doc = TryGetDocument(map.FullPath);
            if (doc is null)
            {
                continue;
            }

            CollectKeys(doc, doc.Root, map.FullPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private void CollectKeys(DitaDocument mapDoc, DitaNode node, string mapPath, HashSet<string> visited)
    {
        foreach (var child in node.ElementChildren())
        {
            var keys = child.GetAttribute("keys");
            if (!string.IsNullOrWhiteSpace(keys))
            {
                var href = child.GetAttribute("href");
                var resolved = href is null ? null : RefResolver.ResolvePath(mapPath, href);
                foreach (var key in keys!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!_keys.ContainsKey(key))
                    {
                        _keys[key] = new KeyDefinition(
                            key, href, resolved, child, mapPath,
                            child.GetAttribute("scope"), child.GetAttribute("format"));
                    }
                }
            }

            // Вложенные карты добавляют свои ключи.
            if (child.Name == "mapref" || (child.GetAttribute("format") == "ditamap"))
            {
                var href = child.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    var target = RefResolver.ResolvePath(mapPath, href!);
                    if (target is not null && File.Exists(target) && visited.Add(target))
                    {
                        var sub = TryGetDocument(target);
                        if (sub is not null)
                        {
                            CollectKeys(sub, sub.Root, target, visited);
                        }
                    }
                }
            }

            CollectKeys(mapDoc, child, mapPath, visited);
        }
    }

    public KeyDefinition? ResolveKey(string key) => _keys.TryGetValue(key, out var d) ? d : null;

    // ---------------------------------------------------------------- поиск

    public sealed record SearchHit(ProjectFile File, DitaNode Node, string Context);

    public IReadOnlyList<SearchHit> Search(string query, bool caseSensitive = false, bool elementNames = false)
    {
        var result = new List<SearchHit>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var file in _files)
        {
            var doc = TryGetDocument(file.FullPath);
            if (doc is null)
            {
                continue;
            }

            foreach (var node in doc.Root.DescendantsAndSelf())
            {
                if (elementNames)
                {
                    if (node.Kind == NodeKind.Element && node.Name.Equals(query, comparison))
                    {
                        result.Add(new SearchHit(file, node, node.Path));
                    }

                    continue;
                }

                if (node.Kind != NodeKind.Text)
                {
                    continue;
                }

                var index = node.Value.IndexOf(query, comparison);
                if (index < 0)
                {
                    continue;
                }

                var start = Math.Max(0, index - 30);
                var length = Math.Min(node.Value.Length - start, query.Length + 60);
                var context = node.Value.Substring(start, length).Replace('\n', ' ').Trim();
                result.Add(new SearchHit(file, node.Parent ?? node, context));
            }
        }

        return result;
    }

    // ------------------------------------------------------------- проверка

    public IReadOnlyList<ValidationIssue> ValidateAll()
    {
        var validator = new DitaValidator();
        var issues = new List<ValidationIssue>();

        foreach (var file in _files)
        {
            DitaDocument doc;
            try
            {
                doc = GetDocument(file.FullPath);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    $"Файл не разбирается как XML: {ex.Message}",
                    null,
                    file.FullPath));
                continue;
            }

            issues.AddRange(validator.Validate(doc));
            issues.AddRange(RefResolver.ValidateReferences(this, doc));
        }

        return issues;
    }
}
