using System.Text;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Publishing;

// Рендер HTML: предметный указатель, сноски, код (pre, coderef) и внешние объекты (object, media, foreign).
public sealed partial class HtmlRenderer
{
    public bool HasIndexTerms => _indexTerms.Count > 0;

    /// <summary>Отмечает вхождение и рекурсивно собирает вложенные indexterm (подпункты) — сам
    /// термин в тексте топика не выводится, только накапливается для <see cref="RenderIndexSection"/>.</summary>
    private string CollectIndexTerm(DitaNode node)
    {
        CollectIndexTermPath(node, Array.Empty<string>());
        return string.Empty;
    }

    private void CollectIndexTermPath(DitaNode node, IReadOnlyList<string> parentPath)
    {
        var ownText = string.Concat(node.Children.Where(c => c.Kind == NodeKind.Text).Select(c => c.Value)).Trim();
        var path = ownText.Length > 0 ? parentPath.Append(ownText).ToList() : parentPath;
        if (ownText.Length > 0)
        {
            _indexTerms.Add((path, _currentTopicHref));
        }

        foreach (var child in node.ElementChildren().Where(c => c.Name == "indexterm"))
        {
            CollectIndexTermPath(child, path);
        }
    }

    private sealed class IndexNode
    {
        public List<string> Hrefs { get; } = new();

        public SortedDictionary<string, IndexNode> Children { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Алфавитный указатель по всем indexterm, встреченным с начала публикации
    /// (счётчик не сбрасывается между топиками, в отличие от сносок) — вызывается один раз в
    /// конце публикации. Ссылки ведут на топик, где стоит термин (не на точное место в тексте).</summary>
    public string RenderIndexSection()
    {
        if (_indexTerms.Count == 0)
        {
            return string.Empty;
        }

        var root = new IndexNode();
        foreach (var (path, href) in _indexTerms)
        {
            var node = root;
            foreach (var segment in path)
            {
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new IndexNode();
                    node.Children[segment] = child;
                }

                node = child;
            }

            if (href is not null)
            {
                node.Hrefs.Add(href);
            }
        }

        var sb = new StringBuilder("<div class=\"index-terms\">\n<h2>").Append(Escape(L.Index)).Append("</h2>\n");
        AppendIndexNode(sb, root);
        sb.Append("</div>\n");
        return sb.ToString();
    }

    private void AppendIndexNode(StringBuilder sb, IndexNode node)
    {
        if (node.Children.Count == 0)
        {
            return;
        }

        sb.Append("<ul>\n");
        foreach (var (term, child) in node.Children)
        {
            sb.Append("<li>").Append(Escape(term));
            foreach (var (href, i) in child.Hrefs.Distinct().Select((h, i) => (h, i)))
            {
                sb.Append(i == 0 ? " " : ", ").Append("<a href=\"").Append(Escape(href)).Append("\">")
                  .Append(i + 1).Append("</a>");
            }

            AppendIndexNode(sb, child);
            sb.Append("</li>\n");
        }

        sb.Append("</ul>\n");
    }

    private string RenderFootnoteRef(DitaNode node)
    {
        _footnotes.Add(node);
        var number = _footnotes.Count;
        var callout = node.GetAttribute("callout");
        var marker = string.IsNullOrWhiteSpace(callout) ? number.ToString() : callout!;
        return $"<a class=\"fn-ref\" href=\"#fn{number}\" id=\"fnref{number}\">[{Escape(marker)}]</a>";
    }

    private string RenderFootnotes()
    {
        if (_footnotes.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder("<div class=\"footnotes\">\n<h2>").Append(Escape(L.Footnotes)).Append("</h2>\n<ol>\n");
        for (var i = 0; i < _footnotes.Count; i++)
        {
            sb.Append("<li id=\"fn").Append(i + 1).Append("\">")
              .Append(RenderInlineChildren(_footnotes[i]))
              .Append(" <a href=\"#fnref").Append(i + 1).Append("\">&#8617;</a></li>\n");
        }

        sb.Append("</ol>\n</div>\n");
        return sb.ToString();
    }

    private string RenderPreContent(DitaNode node)
    {
        var sb = new StringBuilder();
        foreach (var child in node.Children)
        {
            switch (child.Kind)
            {
                case NodeKind.Text:
                    sb.Append(Escape(child.Value));
                    break;
                case NodeKind.Element when child.Name == "coderef":
                    sb.Append(RenderCoderef(child));
                    break;
                case NodeKind.Element:
                    sb.Append(RenderInline(child));
                    break;
            }
        }

        return sb.ToString();
    }

    private string RenderCoderef(DitaNode node)
    {
        var href = node.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href) || _document.FilePath is null)
        {
            return string.Empty;
        }

        var path = RefResolver.ResolvePath(_document.FilePath, href!);
        if (path is null || !File.Exists(path))
        {
            return $"<!-- coderef не найден: {Escape(href!)} -->";
        }

        try
        {
            return Escape(File.ReadAllText(path));
        }
        catch
        {
            return string.Empty;
        }
    }

    private string RenderObject(DitaNode node)
    {
        var data = node.GetAttribute("data");
        var type = node.GetAttribute("type");
        if (string.IsNullOrWhiteSpace(data))
        {
            return RenderChildren(node, 3);
        }

        var src = data!;
        if (_document.FilePath is not null && !RefResolver.IsExternal(data!))
        {
            var abs = RefResolver.ResolvePath(_document.FilePath, data!);
            if (abs is not null)
            {
                src = _options.ImageSource?.Invoke(abs) ?? data!;
            }
        }

        return $"<object data=\"{Escape(src)}\"{(type is null ? string.Empty : $" type=\"{Escape(type)}\"")}></object>\n";
    }

    private string RenderMedia(DitaNode node)
    {
        var tag = node.Name == "video" ? "video" : "audio";
        var href = node.GetAttribute("href")
                   ?? node.FirstElement("media-source")?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        var src = href!;
        if (_document.FilePath is not null && !RefResolver.IsExternal(href!))
        {
            var abs = RefResolver.ResolvePath(_document.FilePath, href!);
            if (abs is not null)
            {
                src = _options.ImageSource?.Invoke(abs) ?? href!;
            }
        }

        var controls = node.GetAttribute("controls") != "false" ? " controls" : string.Empty;
        return $"<{tag} src=\"{Escape(src)}\"{controls}></{tag}>\n";
    }

    private string RenderForeign(DitaNode node)
    {
        // SVG и MathML переносятся в вывод как есть.
        var sb = new StringBuilder();
        foreach (var child in node.Children)
        {
            sb.Append(XmlSerializer.ToXml(child));
        }

        return sb.ToString();
    }
}
