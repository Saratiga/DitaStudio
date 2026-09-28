using System.Text;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Publishing;

// Рендер HTML: обход узлов DITA, фразовые элементы, текст ключей и сокращений.
public sealed partial class HtmlRenderer
{
    private string RenderChildren(DitaNode node, int level)
    {
        var sb = new StringBuilder();
        foreach (var child in node.Children)
        {
            sb.Append(RenderNode(child, level));
        }

        return sb.ToString();
    }

    private string RenderNode(DitaNode node, int level)
    {
        switch (node.Kind)
        {
            case NodeKind.Text:
                return Escape(node.Value);
            case NodeKind.Comment:
            case NodeKind.ProcessingInstruction:
                return string.Empty;
        }

        if (!Include(node))
        {
            return string.Empty;
        }

        switch (BlockElementCategoryMap.Of(node.Name))
        {
            case BlockElementCategory.ContainerDiv:
                return $"<div class=\"{node.Name}\"{Attrs(node)}>{RenderChildren(node, level)}</div>\n";
            case BlockElementCategory.ContainerSection:
                return RenderSection(node, level);
            case BlockElementCategory.Preformatted:
                return $"<pre class=\"{node.Name}\"{Attrs(node)}>{RenderPreContent(node)}</pre>\n";
            case BlockElementCategory.Figure:
                return RenderFigure(node, level);
            case BlockElementCategory.SimpleTable:
                return RenderSimpleTable(node, level);
            case BlockElementCategory.Skip:
                return string.Empty;
            case BlockElementCategory.ListUnordered:
            {
                var listClass = node.Name switch
                {
                    "sl" => " class=\"sl\"",
                    "choices" => " class=\"choices\"",
                    _ => string.Empty
                };
                return $"<ul{listClass}{Attrs(node)}>\n{RenderChildren(node, level)}</ul>\n";
            }
            case BlockElementCategory.StepsGroup:
            {
                var ordered = node.Name == "steps";
                var tag = ordered ? "ol" : "ul";
                return GeneratedTitle(L.Steps) + $"<{tag} class=\"steps\"{Attrs(node)}>\n{RenderChildren(node, level)}</{tag}>\n";
            }
        }

        switch (node.Name)
        {
            // ------------------------------------------------------- блоки
            case "p" when _options.Numbering is { } numbering && HeadingNumbering.IsNumbered(node):
                return $"<p{Attrs(node)}><span class=\"para-number\">{numbering.Paragraph()}</span> {RenderInlineChildren(node)}</p>\n";

            case "p":
                return $"<p{Attrs(node)}>{RenderInlineChildren(node)}</p>\n";

            case "ol":
                return $"<ol{Attrs(node)}>\n{RenderChildren(node, level)}</ol>\n";

            case "li":
            case "sli":
            case "choice":
            case "stepsection":
                return $"<li{Attrs(node)}>{RenderInlineChildren(node)}</li>\n";

            case "substeps":
                return $"<ol class=\"steps\"{Attrs(node)}>\n{RenderChildren(node, level)}</ol>\n";

            case "step":
            case "substep":
                return $"<li class=\"step\"{Attrs(node)}>{RenderChildren(node, level)}</li>\n";

            case "cmd":
                return $"<div class=\"cmd\">{RenderInlineChildren(node)}</div>\n";

            case "info":
            case "stepxmp":
            case "stepresult":
            case "steptroubleshooting":
            case "tutorialinfo":
                return $"<div class=\"{node.Name}\">{RenderInlineChildren(node)}</div>\n";

            case "dl":
                return RenderDl(node, level);

            case "parml":
                return RenderParml(node, level);

            case "note":
                return RenderNote(node, level);

            case "hazardstatement":
                return RenderHazard(node, level);

            case "lq":
                return $"<blockquote{Attrs(node)}>{RenderInlineChildren(node)}</blockquote>\n";

            case "table":
                return RenderTable(node, level);

            case "object":
                return RenderObject(node);

            case "video":
            case "audio":
                return RenderMedia(node);

            case "draft-comment":
                return _options.ShowDraftComments
                    ? $"<div class=\"draft-comment\">{RenderInlineChildren(node)}</div>\n"
                    : string.Empty;

            case "indexterm":
                return CollectIndexTerm(node);

            case "title":
                return $"<div class=\"title\">{RenderInlineChildren(node)}</div>\n";

            case "related-links":
                return RenderRelatedLinks(node);

            case "svg-container":
            case "foreign":
            case "mathml":
                return RenderForeign(node);
        }

        var def = _catalog.Get(node.Name);
        if (def is null)
        {
            return $"<div class=\"unknown-element\">{RenderChildren(node, level)}</div>\n";
        }

        return def.Display switch
        {
            DisplayKind.Inline => RenderInline(node),
            DisplayKind.Empty => RenderInline(node),
            DisplayKind.Meta => string.Empty,
            DisplayKind.Preformatted => $"<pre class=\"{node.Name}\">{RenderPreContent(node)}</pre>\n",
            _ => $"<div class=\"{node.Name}\"{Attrs(node)}>{RenderChildren(node, level)}</div>\n"
        };
    }

    /// <summary>Смешанное содержимое: текст и фразовые элементы, вложенные блоки — как есть.</summary>
    private string RenderInlineChildren(DitaNode node)
    {
        var sb = new StringBuilder();
        foreach (var child in node.Children)
        {
            switch (child.Kind)
            {
                case NodeKind.Text:
                    sb.Append(Escape(child.Value));
                    continue;
                case NodeKind.Comment:
                case NodeKind.ProcessingInstruction:
                    continue;
            }

            if (!Include(child))
            {
                continue;
            }

            var def = _catalog.Get(child.Name);
            if (def is not null && (def.Display == DisplayKind.Inline || def.Display == DisplayKind.Empty))
            {
                sb.Append(RenderInline(child));
            }
            else
            {
                sb.Append(RenderNode(child, 3));
            }
        }

        return sb.ToString();
    }

    private string RenderInline(DitaNode node)
    {
        switch (node.Name)
        {
            case "b":
                return $"<strong>{RenderInlineChildren(node)}</strong>";
            case "i":
                return $"<em>{RenderInlineChildren(node)}</em>";
            case "u":
                return $"<u>{RenderInlineChildren(node)}</u>";
            case "sup":
                return $"<sup>{RenderInlineChildren(node)}</sup>";
            case "sub":
                return $"<sub>{RenderInlineChildren(node)}</sub>";
            case "line-through":
                return $"<s>{RenderInlineChildren(node)}</s>";
            case "overline":
                return $"<span style=\"text-decoration:overline\">{RenderInlineChildren(node)}</span>";
            case "q":
                return $"<q>{RenderInlineChildren(node)}</q>";
            case "cite":
                return $"<cite>{RenderInlineChildren(node)}</cite>";
            case "image":
            case "glossSymbol":
            case "hazardsymbol":
                return RenderImage(node);
            case "xref":
            case "link":
                return RenderXref(node);
            case "fn":
                return RenderFootnoteRef(node);
            case "indexterm":
                return CollectIndexTerm(node);
            case "index-see":
            case "index-see-also":
            case "sort-as":
                return string.Empty;
            case "menucascade":
            {
                var parts = node.ElementChildren()
                    .Where(c => c.Name == "uicontrol")
                    .Select(RenderInlineChildren);
                return $"<span class=\"menucascade\">{string.Join(" <span class=\"sep\">&rarr;</span> ", parts)}</span>";
            }

            case "abbreviated-form":
                return RenderAbbreviatedForm(node);
            case "state":
                return $"<span class=\"state\">{Escape(node.GetAttribute("name") ?? string.Empty)}={Escape(node.GetAttribute("value") ?? string.Empty)}</span>";
            case "boolean":
                return $"<span class=\"boolean\">{Escape(node.GetAttribute("state") ?? string.Empty)}</span>";
            case "tm":
            {
                var mark = node.GetAttribute("tmtype") switch
                {
                    "reg" => "&reg;",
                    "service" => "&#8480;",
                    _ => "&trade;"
                };
                return $"<span class=\"tm\">{RenderInlineChildren(node)}{mark}</span>";
            }

            case "text":
                return RenderInlineChildren(node);
            case "coderef":
                return RenderCoderef(node);
            case "svgref":
            case "mathmlref":
                return string.Empty;
        }

        var keyText = KeyTextFor(node);
        if (keyText is not null && node.Children.Count == 0)
        {
            return $"<span class=\"{node.Name}\">{Escape(keyText)}</span>";
        }

        return $"<span class=\"{node.Name}\"{Attrs(node)}>{RenderInlineChildren(node)}</span>";
    }

    private string? KeyTextFor(DitaNode node)
    {
        var keyref = node.GetAttribute("keyref");
        if (string.IsNullOrWhiteSpace(keyref))
        {
            return null;
        }

        return _project.ResolveKey(keyref!.Split('/')[0], _options.CurrentKeyScope)?.KeyText;
    }

    private string RenderAbbreviatedForm(DitaNode node)
    {
        var keyref = node.GetAttribute("keyref");
        if (string.IsNullOrWhiteSpace(keyref))
        {
            return string.Empty;
        }

        var keyDef = _project.ResolveKey(keyref!, _options.CurrentKeyScope);
        if (keyDef?.ResolvedPath is not null && File.Exists(keyDef.ResolvedPath))
        {
            var doc = _project.TryGetDocument(keyDef.ResolvedPath);
            var glossentry = doc?.Root.Name == "glossentry" ? doc.Root : doc?.Root.FindDescendant("glossentry");
            var acronym = glossentry?.FindDescendant("glossAcronym")?.InnerText.Trim()
                          ?? glossentry?.FindDescendant("glossAbbreviation")?.InnerText.Trim()
                          ?? glossentry?.FirstElement("glossterm")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(acronym))
            {
                return $"<abbr class=\"abbreviated-form\">{Escape(acronym!)}</abbr>";
            }
        }

        return $"<abbr class=\"abbreviated-form\">{Escape(keyDef?.KeyText ?? keyref!)}</abbr>";
    }
}
