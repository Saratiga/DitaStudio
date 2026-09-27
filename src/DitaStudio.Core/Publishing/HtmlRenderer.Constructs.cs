using System.Text;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Publishing;

// Рендер HTML: разделы, примечания и опасности, списки определений и параметров, рисунки, изображения, ссылки.
public sealed partial class HtmlRenderer
{
    private string RenderSection(DitaNode node, int level)
    {
        var sb = new StringBuilder();
        sb.Append("<section").Append(MergedClassAttr(node.Name, node)).Append(IdAttr(node)).Append(">\n");

        var label = node.Name switch
        {
            "prereq" => L.Prerequisites,
            "context" => L.Context,
            "result" => L.Result,
            "postreq" => L.PostRequisites,
            "tasktroubleshooting" => L.TaskTroubleshooting,
            "condition" => L.Condition,
            "cause" => L.Cause,
            "remedy" => L.Remedy,
            "example" => L.Example,
            _ => null
        };

        var explicitTitle = node.FirstElement("title");
        if (explicitTitle is not null)
        {
            sb.Append('<').Append(H(level + 1)).Append(MergedClassAttr("title", explicitTitle)).Append('>')
              .Append(RenderInlineChildren(explicitTitle))
              .Append("</").Append(H(level + 1)).Append(">\n");
        }
        else if (label is not null)
        {
            sb.Append('<').Append(H(level + 1)).Append(" class=\"generated-title\">")
              .Append(Escape(label)).Append("</").Append(H(level + 1)).Append(">\n");
        }

        var spectitle = node.GetAttribute("spectitle");
        if (!string.IsNullOrWhiteSpace(spectitle) && explicitTitle is null && label is null)
        {
            sb.Append('<').Append(H(level + 1)).Append(" class=\"title\">")
              .Append(Escape(spectitle!)).Append("</").Append(H(level + 1)).Append(">\n");
        }

        foreach (var child in node.Children)
        {
            if (child.Kind == NodeKind.Element && ReferenceEquals(child, explicitTitle))
            {
                continue;
            }

            sb.Append(RenderNode(child, level + 1));
        }

        sb.Append("</section>\n");
        return sb.ToString();
    }

    /// <summary>Подпись, которую генератор добавляет сам (как «Порядок действий» перед шагами).</summary>
    private static string GeneratedTitle(string label) =>
        string.IsNullOrEmpty(label)
            ? string.Empty
            : $"<div class=\"generated-title\">{Escape(label)}</div>\n";

    private string RenderNote(DitaNode node, int level)
    {
        var type = node.GetAttribute("type") ?? "note";
        var label = L.NoteLabel(type);
        return $"<div class=\"note {Escape(type)}\"{Attrs(node)}><span class=\"label\">{Escape(label)}:</span> {RenderInlineChildren(node)}</div>\n";
    }

    private string RenderHazard(DitaNode node, int level)
    {
        var sb = new StringBuilder();
        var type = node.GetAttribute("type") ?? "caution";
        sb.Append("<div class=\"hazard ").Append(Escape(type)).Append("\">\n");
        var panel = node.FirstElement("messagepanel");
        if (panel is not null)
        {
            foreach (var part in panel.ElementChildren())
            {
                sb.Append("<div class=\"").Append(part.Name).Append("\">")
                  .Append(RenderInlineChildren(part)).Append("</div>\n");
            }
        }

        sb.Append("</div>\n");
        return sb.ToString();
    }

    private string RenderDl(DitaNode node, int level)
    {
        var sb = new StringBuilder();
        var head = node.FirstElement("dlhead");
        if (head is not null)
        {
            sb.Append("<div class=\"dlhead\">");
            foreach (var h in head.ElementChildren())
            {
                sb.Append("<span class=\"").Append(h.Name).Append("\">")
                  .Append(RenderInlineChildren(h)).Append("</span>");
            }

            sb.Append("</div>\n");
        }

        sb.Append("<dl").Append(Attrs(node)).Append(">\n");
        foreach (var entry in node.ElementChildren().Where(e => e.Name == "dlentry"))
        {
            foreach (var dt in entry.ElementChildren().Where(e => e.Name == "dt"))
            {
                sb.Append("<dt>").Append(RenderInlineChildren(dt)).Append("</dt>\n");
            }

            foreach (var dd in entry.ElementChildren().Where(e => e.Name == "dd"))
            {
                sb.Append("<dd>").Append(RenderInlineChildren(dd)).Append("</dd>\n");
            }
        }

        sb.Append("</dl>\n");
        return sb.ToString();
    }

    private string RenderParml(DitaNode node, int level)
    {
        var sb = new StringBuilder("<dl class=\"parml\">\n");
        foreach (var entry in node.ElementChildren().Where(e => e.Name == "plentry"))
        {
            foreach (var pt in entry.ElementChildren().Where(e => e.Name == "pt"))
            {
                sb.Append("<dt>").Append(RenderInlineChildren(pt)).Append("</dt>\n");
            }

            foreach (var pd in entry.ElementChildren().Where(e => e.Name == "pd"))
            {
                sb.Append("<dd>").Append(RenderInlineChildren(pd)).Append("</dd>\n");
            }
        }

        sb.Append("</dl>\n");
        return sb.ToString();
    }

    private string RenderFigure(DitaNode node, int level)
    {
        var sb = new StringBuilder("<figure").Append(Attrs(node)).Append(">\n");
        var title = node.FirstElement("title");
        if (title is not null)
        {
            _figureNumber++;
            var caption = _options.NumberFiguresAndTables
                ? $"{L.Figure} {_figureNumber}. {RenderInlineChildren(title)}"
                : RenderInlineChildren(title);
            sb.Append("<figcaption class=\"fig-title\">").Append(caption).Append("</figcaption>\n");
        }

        foreach (var child in node.Children)
        {
            if (child.Kind == NodeKind.Element && (child.Name == "title" || child.Name == "desc"))
            {
                continue;
            }

            sb.Append(RenderNode(child, level));
        }

        var desc = node.FirstElement("desc");
        if (desc is not null)
        {
            sb.Append("<div class=\"desc\">").Append(RenderInlineChildren(desc)).Append("</div>\n");
        }

        sb.Append("</figure>\n");
        return sb.ToString();
    }

    private string RenderImage(DitaNode node)
    {
        var href = node.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            var keyDef = node.GetAttribute("keyref") is { } k ? _project.ResolveKey(k, _options.CurrentKeyScope) : null;
            href = keyDef?.Href;
        }

        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        var src = href!;
        if (!RefResolver.IsExternal(href!) && _document.FilePath is not null)
        {
            var abs = RefResolver.ResolvePath(_document.FilePath, href!);
            if (abs is not null)
            {
                src = _options.ImageSource?.Invoke(abs) ?? href!;
            }
        }

        var alt = node.GetAttribute("alt") ?? node.FirstElement("alt")?.InnerText ?? string.Empty;
        var sb = new StringBuilder("<img src=\"").Append(Escape(src)).Append('"');
        sb.Append(" alt=\"").Append(Escape(alt)).Append('"');

        var width = node.GetAttribute("width");
        if (!string.IsNullOrWhiteSpace(width))
        {
            sb.Append(" width=\"").Append(Escape(width!)).Append('"');
        }

        var height = node.GetAttribute("height");
        if (!string.IsNullOrWhiteSpace(height))
        {
            sb.Append(" height=\"").Append(Escape(height!)).Append('"');
        }

        sb.Append(" />");

        return node.GetAttribute("placement") == "break"
            ? $"<div class=\"image-block\">{sb}</div>\n"
            : sb.ToString();
    }

    private string RenderXref(DitaNode node)
    {
        var href = node.GetAttribute("href");
        var keyref = node.GetAttribute("keyref");
        string? target = null;
        string? label = null;

        if (!string.IsNullOrWhiteSpace(keyref))
        {
            (target, label) = ResolveXrefKeyref(keyref!);
        }

        if (target is null && !string.IsNullOrWhiteSpace(href))
        {
            (target, label) = ResolveXrefHref(node, href!, label);
        }

        var inner = RenderInlineChildren(node);
        if (string.IsNullOrWhiteSpace(StripTags(inner)))
        {
            inner = Escape(label ?? target ?? href ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return $"<span class=\"xref-broken\">{inner}</span>";
        }

        return $"<a href=\"{Escape(target!)}\">{inner}</a>";
    }

    private (string? Target, string? Label) ResolveXrefKeyref(string keyref)
    {
        var keyDef = _project.ResolveKey(keyref.Split('/')[0], _options.CurrentKeyScope);
        if (keyDef is null)
        {
            return (null, null);
        }

        var target = keyDef.ResolvedPath is not null
            ? _options.TopicLink?.Invoke(keyDef.ResolvedPath, null)
            : keyDef.Href;

        return (target, keyDef.KeyText);
    }

    private (string? Target, string? Label) ResolveXrefHref(DitaNode node, string href, string? label)
    {
        if (RefResolver.IsExternal(href) || node.GetAttribute("scope") is "external" or "peer")
        {
            return (href, label);
        }

        if (_document.FilePath is null)
        {
            return (null, label);
        }

        var reference = RefResolver.Parse(_document.FilePath, href);
        if (reference.Path is not null)
        {
            var target = _options.TopicLink?.Invoke(reference.Path, reference.ElementId ?? reference.TopicId) ?? href;
            return (target, label ?? TitleOf(reference));
        }

        var fallback = "#" + (reference.ElementId ?? reference.TopicId ?? string.Empty);
        return (fallback, label ?? TitleOf(reference));
    }

    private string? TitleOf(DitaReference reference)
    {
        if (reference.Path is null || !File.Exists(reference.Path))
        {
            return null;
        }

        var doc = _project.TryGetDocument(reference.Path);
        if (doc is null)
        {
            return null;
        }

        if (reference.TopicId is null)
        {
            return doc.Title;
        }

        var topic = RefResolver.FindById(doc.Root, reference.TopicId);
        if (topic is null)
        {
            return doc.Title;
        }

        if (reference.ElementId is not null)
        {
            var element = RefResolver.FindById(topic, reference.ElementId);
            var elementTitle = element?.FirstElement("title")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(elementTitle))
            {
                return elementTitle;
            }
        }

        return topic.FirstElement("title")?.InnerText.Trim() ?? doc.Title;
    }

    private string RenderRelatedLinks(DitaNode node)
    {
        var links = node.DescendantsAndSelf()
            .Where(n => n.Kind == NodeKind.Element && n.Name == "link" && Include(n))
            .ToList();
        if (links.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder("<nav class=\"related-links\">\n<h2>")
            .Append(Escape(L.RelatedLinks)).Append("</h2>\n<ul>\n");
        foreach (var link in links)
        {
            var linktext = link.FirstElement("linktext");
            var anchor = RenderXref(link);
            if (linktext is not null && string.IsNullOrWhiteSpace(StripTags(anchor)))
            {
                anchor = RenderInlineChildren(linktext);
            }

            sb.Append("<li>").Append(anchor);
            var desc = link.FirstElement("desc");
            if (desc is not null)
            {
                sb.Append(" — <span class=\"desc\">").Append(RenderInlineChildren(desc)).Append("</span>");
            }

            sb.Append("</li>\n");
        }

        sb.Append("</ul>\n</nav>\n");
        return sb.ToString();
    }
}
