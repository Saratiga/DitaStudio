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
    private MapTree(MapItem root, string mapPath)
    {
        Root = root;
        MapPath = mapPath;
    }

    public MapItem Root { get; }

    public string MapPath { get; }

    public IEnumerable<MapItem> Items => Root.DescendantsAndSelf();

    /// <summary>Топики публикации в порядке обхода карты (без resource-only).</summary>
    public IEnumerable<MapItem> PublicationOrder =>
        Items.Where(i => !i.IsResourceOnly && i.TargetPath is not null && !i.IsBroken);

    public static MapTree Build(DitaProject project, string mapPath)
    {
        var doc = project.GetDocument(mapPath);
        var root = new MapItem(doc.Root, mapPath)
        {
            Title = doc.Title
        };

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System.IO.Path.GetFullPath(mapPath) };
        AddChildren(project, doc, doc.Root, root, mapPath, visited, 0);
        return new MapTree(root, mapPath);
    }

    private static void AddChildren(
        DitaProject project,
        DitaDocument mapDoc,
        DitaNode parentNode,
        MapItem parentItem,
        string mapPath,
        HashSet<string> visited,
        int depth)
    {
        if (depth > 24)
        {
            return;
        }

        foreach (var child in parentNode.ElementChildren())
        {
            if (child.Name is "topicmeta" or "title" or "reltable" or "data" or "data-about" or "ditavalmeta")
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
                        AddChildren(project, subDoc, subDoc.Root, item, full, visited, depth + 1);
                    }

                    visited.Remove(full);
                }
            }

            AddChildren(project, mapDoc, child, item, mapPath, visited, depth + 1);
        }
    }

    private static void ResolveTarget(DitaProject project, MapItem item, string mapPath)
    {
        var href = item.Node.GetAttribute("href");
        var keyref = item.Node.GetAttribute("keyref");

        if (string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(keyref))
        {
            var keyDef = project.ResolveKey(keyref!.Split('/')[0]);
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
