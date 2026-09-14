using DitaStudio.Core.Model;

namespace DitaStudio.Core.Project;

/// <summary>Узел дерева карты — то, что видно в панели структуры публикации.</summary>
public sealed class MapItem
{
    public MapItem(DitaNode node, string mapPath)
    {
        Node = node;
        MapPath = mapPath;
    }

    /// <summary>Элемент карты (topicref, chapter, topichead...).</summary>
    public DitaNode Node { get; }

    /// <summary>Файл карты, в котором объявлен этот узел.</summary>
    public string MapPath { get; }

    public MapItem? Parent { get; internal set; }

    public List<MapItem> Children { get; } = new();

    /// <summary>Абсолютный путь к целевому топику, если ссылка разрешилась.</summary>
    public string? TargetPath { get; internal set; }

    /// <summary>Идентификатор топика внутри файла (для файлов с несколькими топиками).</summary>
    public string? TargetTopicId { get; internal set; }

    public string Title { get; internal set; } = string.Empty;

    public string ElementName => Node.Name;

    public string? Href => Node.GetAttribute("href");

    public string? Keys => Node.GetAttribute("keys");

    public bool IsResourceOnly => Node.GetAttribute("processing-role") == "resource-only" || Node.Name == "keydef";

    public bool IsMapRef => Node.Name == "mapref" || Node.GetAttribute("format") == "ditamap";

    /// <summary>Узел ссылается на файл, который не найден.</summary>
    public bool IsBroken { get; internal set; }

    /// <summary>Глубина вложенности (0 — корень карты).</summary>
    public int Level => Parent is null ? 0 : Parent.Level + 1;

    /// <summary>Цепочка имён областей ключей (keyscope) от корня карты до этого узла — по одному
    /// (первому) имени на каждый уровень, где задан атрибут. Передаётся в
    /// <see cref="DitaProject.ResolveKey(string, IReadOnlyList{string}?)"/>, чтобы ключи внутри
    /// разных веток карты могли иметь разные значения при одинаковом имени.</summary>
    public IReadOnlyList<string> KeyScopeChain
    {
        get
        {
            var own = Node.GetAttribute("keyscope");
            var ownName = string.IsNullOrWhiteSpace(own)
                ? null
                : own!.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            var parentChain = Parent?.KeyScopeChain ?? Array.Empty<string>();
            if (ownName is null)
            {
                return parentChain;
            }

            var chain = new List<string>(parentChain) { ownName };
            return chain;
        }
    }

    public IEnumerable<MapItem> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var d in child.DescendantsAndSelf())
            {
                yield return d;
            }
        }
    }

    public override string ToString() => $"{ElementName}: {Title}";
}

/// <summary>
/// Построение дерева публикации по карте: раскрывает вложенные карты,
/// подставляет заголовки из топиков и помечает битые ссылки.
/// </summary>
public sealed class MapTree
{
    private MapTree(MapItem root, string mapPath, Dictionary<string, List<RelatedLink>> relatedLinks)
    {
        Root = root;
        MapPath = mapPath;
        RelatedLinks = relatedLinks.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyList<RelatedLink>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    public MapItem Root { get; }

    public string MapPath { get; }

    public IEnumerable<MapItem> Items => Root.DescendantsAndSelf();

    /// <summary>Топики публикации в порядке обхода карты (без resource-only).</summary>
    public IEnumerable<MapItem> PublicationOrder =>
        Items.Where(i => !i.IsResourceOnly && i.TargetPath is not null && !i.IsBroken);

    /// <summary>Связи из таблиц соответствий (reltable), ключ — абсолютный путь топика. Топики
    /// в одной строке reltable, но в разных ячейках, взаимно связываются; топики одной ячейки
    /// друг с другом не связываются (это одна "роль" в строке).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RelatedLink>> RelatedLinks { get; }

    public static MapTree Build(DitaProject project, string mapPath)
    {
        var doc = project.GetDocument(mapPath);
        var root = new MapItem(doc.Root, mapPath)
        {
            Title = doc.Title
        };

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System.IO.Path.GetFullPath(mapPath) };
        var relatedLinks = new Dictionary<string, List<RelatedLink>>(StringComparer.OrdinalIgnoreCase);
        AddChildren(project, doc, doc.Root, root, mapPath, visited, 0, relatedLinks);
        return new MapTree(root, mapPath, relatedLinks);
    }

    private static void AddChildren(
        DitaProject project,
        DitaDocument mapDoc,
        DitaNode parentNode,
        MapItem parentItem,
        string mapPath,
        HashSet<string> visited,
        int depth,
        Dictionary<string, List<RelatedLink>> relatedLinks)
    {
        if (depth > 24)
        {
            return;
        }

        foreach (var child in parentNode.ElementChildren())
        {
            if (child.Name == "reltable")
            {
                CollectRelTable(child, mapPath, relatedLinks);
                continue;
            }

            if (child.Name is "topicmeta" or "title" or "data" or "data-about" or "ditavalmeta")
            {
                continue;
            }

            var item = new MapItem(child, mapPath) { Parent = parentItem };
            parentItem.Children.Add(item);

            ResolveTarget(project, item, mapPath);
            item.Title = ResolveTitle(project, item);

            if (item.IsMapRef && item.TargetPath is not null && File.Exists(item.TargetPath))
            {
                var full = System.IO.Path.GetFullPath(item.TargetPath);
                if (visited.Add(full))
                {
                    var subDoc = project.TryGetDocument(full);
                    if (subDoc is not null)
                    {
                        item.Title = string.IsNullOrEmpty(item.Title) ? subDoc.Title : item.Title;
                        AddChildren(project, subDoc, subDoc.Root, item, full, visited, depth + 1, relatedLinks);
                    }

                    visited.Remove(full);
                }
            }

            AddChildren(project, mapDoc, child, item, mapPath, visited, depth + 1, relatedLinks);
        }
    }

    // ------------------------------------------------------- таблицы соответствий

    /// <summary>Один целевой топик из связи reltable.</summary>
    public sealed record RelatedLink(string Path, string? TopicId);

    private static void CollectRelTable(
        DitaNode reltable, string mapPath, Dictionary<string, List<RelatedLink>> relatedLinks)
    {
        foreach (var relrow in reltable.ElementChildren().Where(n => n.Name == "relrow"))
        {
            var cells = relrow.ElementChildren().Where(n => n.Name == "relcell")
                .Select(cell => cell.ElementChildren()
                    .Where(n => n.Name == "topicref")
                    .Select(tr => ResolveRelRef(mapPath, tr))
                    .Where(r => r is not null)
                    .Select(r => r!.Value)
                    .ToList())
                .Where(refs => refs.Count > 0)
                .ToList();

            for (var i = 0; i < cells.Count; i++)
            {
                for (var j = 0; j < cells.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    foreach (var from in cells[i])
                    {
                        foreach (var to in cells[j])
                        {
                            AddRelatedLink(relatedLinks, from, new RelatedLink(to.Path, to.TopicId));
                        }
                    }
                }
            }
        }
    }

    private static void AddRelatedLink(
        Dictionary<string, List<RelatedLink>> relatedLinks, (string Path, string? TopicId) from, RelatedLink to)
    {
        var key = System.IO.Path.GetFullPath(from.Path);
        if (!relatedLinks.TryGetValue(key, out var list))
        {
            list = new List<RelatedLink>();
            relatedLinks[key] = list;
        }

        if (!list.Any(l => string.Equals(l.Path, to.Path, StringComparison.OrdinalIgnoreCase) && l.TopicId == to.TopicId))
        {
            list.Add(to);
        }
    }

    /// <summary>Разрешает topicref внутри relcell в путь топика — только по href (без keyref,
    /// как и остальная упрощённая модель редактора reltable в этой версии).</summary>
    private static (string Path, string? TopicId)? ResolveRelRef(string mapPath, DitaNode topicref)
    {
        var href = topicref.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href) || RefResolver.IsExternal(href!))
        {
            return null;
        }

        var reference = RefResolver.Parse(mapPath, href!);
        return reference.Path is null ? null : (reference.Path, reference.TopicId);
    }

    private static void ResolveTarget(DitaProject project, MapItem item, string mapPath)
    {
        var href = item.Node.GetAttribute("href");
        var keyref = item.Node.GetAttribute("keyref");

        if (string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(keyref))
        {
            var keyDef = project.ResolveKey(keyref!.Split('/')[0], item.Parent?.KeyScopeChain);
            if (keyDef?.ResolvedPath is not null)
            {
                item.TargetPath = keyDef.ResolvedPath;
                item.IsBroken = !File.Exists(keyDef.ResolvedPath);
            }
            else
            {
                item.IsBroken = true;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(href))
        {
            return;
        }

        if (RefResolver.IsExternal(href!) || item.Node.GetAttribute("scope") is "external" or "peer")
        {
            return;
        }

        var reference = RefResolver.Parse(mapPath, href!);
        item.TargetPath = reference.Path;
        item.TargetTopicId = reference.TopicId;
        item.IsBroken = reference.Path is null || !File.Exists(reference.Path);
    }

    private static string ResolveTitle(DitaProject project, MapItem item)
    {
        var meta = item.Node.FirstElement("topicmeta");
        var navtitle = meta?.FirstElement("navtitle")?.InnerText.Trim();
        if (!string.IsNullOrEmpty(navtitle))
        {
            return navtitle!;
        }

        var navAttr = item.Node.GetAttribute("navtitle");
        if (!string.IsNullOrWhiteSpace(navAttr))
        {
            return navAttr!;
        }

        var linktext = meta?.FirstElement("linktext")?.InnerText.Trim();
        if (!string.IsNullOrEmpty(linktext))
        {
            return linktext!;
        }

        if (item.TargetPath is not null && File.Exists(item.TargetPath))
        {
            var doc = project.TryGetDocument(item.TargetPath);
            if (doc is not null)
            {
                if (item.TargetTopicId is not null)
                {
                    var topic = RefResolver.FindById(doc.Root, item.TargetTopicId);
                    var title = topic?.FirstElement("title")?.InnerText.Trim()
                                ?? topic?.FirstElement("glossterm")?.InnerText.Trim();
                    if (!string.IsNullOrEmpty(title))
                    {
                        return title!;
                    }
                }

                return doc.Title;
            }
        }

        if (item.Node.Name == "keydef")
        {
            return item.Keys is null ? "keydef" : $"ключ: {item.Keys}";
        }

        var href = item.Href;
        return string.IsNullOrWhiteSpace(href) ? $"<{item.Node.Name}>" : href!;
    }
}
