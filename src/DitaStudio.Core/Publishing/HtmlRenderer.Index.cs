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

    private sealed record IndexEntry(IndexTermEntry Term, string? Href);

    /// <summary>Отмечает вхождение и рекурсивно собирает вложенные indexterm (подпункты) — сам
    /// термин в тексте топика не выводится, только накапливается для <see cref="RenderIndexSection"/>.</summary>
    private string CollectIndexTerm(DitaNode node)
    {
        foreach (var term in IndexTermReader.Read(node))
        {
            _indexTerms.Add(new IndexEntry(term, _currentTopicHref));
        }

        return string.Empty;
    }

    /// <summary>Термины из пролога топика: в тексте их нет, но в указатель они входят.</summary>
    private void CollectPrologIndexTerms(DitaNode prolog)
    {
        foreach (var term in IndexTermReader.TermsIn(prolog))
        {
            CollectIndexTerm(term);
        }
    }

    private sealed class IndexNode
    {
        public List<string> Hrefs { get; } = new();

        public string? SortAs { get; set; }

        public List<string> See { get; } = new();

        public List<string> SeeAlso { get; } = new();

        public Dictionary<string, IndexNode> Children { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Алфавитный указатель по всем indexterm, встреченным с начала публикации
    /// (счётчик не сбрасывается между топиками, в отличие от сносок) — вызывается один раз в
    /// конце публикации. Ссылки ведут на топик, где стоит термин (не на точное место в тексте).
    /// Порядок — по <c>sort-as</c>, нет его — по самому термину; <c>index-see</c> и <c>index-see-also</c> выводятся отсылками.</summary>
    /// <param name="heading">Выводить ли заголовок «Указатель» (для отдельной страницы указателя он уже есть в заголовке страницы).</param>
    public string RenderIndexSection(bool heading = true)
    {
        if (_indexTerms.Count == 0)
        {
            return string.Empty;
        }

        var root = new IndexNode();
        foreach (var entry in _indexTerms)
        {
            var node = root;
            foreach (var segment in entry.Term.Path)
            {
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new IndexNode();
                    node.Children[segment] = child;
                }

                node = child;
            }

            if (entry.Href is not null)
            {
                node.Hrefs.Add(entry.Href);
            }

            node.SortAs ??= entry.Term.SortAs;
            foreach (var see in entry.Term.See.Where(t => !node.See.Contains(t)))
            {
                node.See.Add(see);
            }

            foreach (var seeAlso in entry.Term.SeeAlso.Where(t => !node.SeeAlso.Contains(t)))
            {
                node.SeeAlso.Add(seeAlso);
            }
        }

        var sb = new StringBuilder("<div class=\"index-terms\">\n");
        if (heading)
        {
            sb.Append("<h2>").Append(Escape(L.Index)).Append("</h2>\n");
        }

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

        var comparer = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(L == Labels.Russian ? "ru-RU" : "en-US"), ignoreCase: true);
        sb.Append("<ul>\n");
        foreach (var (term, child) in node.Children.OrderBy(kv => kv.Value.SortAs ?? kv.Key, comparer).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append("<li>").Append(Escape(term));
            foreach (var (href, i) in child.Hrefs.Distinct().Select((h, i) => (h, i)))
            {
                sb.Append(i == 0 ? " " : ", ").Append("<a href=\"").Append(Escape(href)).Append("\">")
                  .Append(i + 1).Append("</a>");
            }

            if (child.See.Count > 0)
            {
                sb.Append(" — <em>").Append(Escape(L.IndexSee)).Append("</em> ").Append(Escape(string.Join(", ", child.See)));
            }

            if (child.SeeAlso.Count > 0)
            {
                sb.Append(" — <em>").Append(Escape(L.IndexSeeAlso)).Append("</em> ").Append(Escape(string.Join(", ", child.SeeAlso)));
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
