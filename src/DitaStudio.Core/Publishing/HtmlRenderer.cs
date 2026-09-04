using System.Text;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Publishing;

public sealed class RenderOptions
{
    public Labels Labels { get; set; } = Labels.Russian;

    public bool ShowDraftComments { get; set; }

    /// <summary>Ссылка на топик по абсолютному пути и идентификатору — возвращает href в выходном формате.</summary>
    public Func<string, string?, string>? TopicLink { get; set; }

    /// <summary>Преобразование пути к изображению в src выходного документа.</summary>
    public Func<string, string>? ImageSource { get; set; }

    /// <summary>Условная фильтрация: возвращает false, если элемент нужно исключить.</summary>
    public Func<DitaNode, bool>? Filter { get; set; }

    public bool NumberFiguresAndTables { get; set; } = true;
}

/// <summary>
/// Преобразование DITA в HTML. Обрабатывает базовые типы топиков, домены
/// и таблицы CALS; неизвестные элементы выводятся по их отображению из каталога.
/// </summary>
public sealed class HtmlRenderer
{
    private readonly DitaProject _project;
    private readonly RenderOptions _options;
    private readonly DitaCatalog _catalog = DitaCatalog.Default;

    private DitaDocument _document = null!;
    private List<DitaNode> _footnotes = new();
    private int _figureNumber;
    private int _tableNumber;

    public HtmlRenderer(DitaProject project, RenderOptions? options = null)
    {
        _project = project;
        _options = options ?? new RenderOptions();
    }

    public int FigureNumber
    {
        get => _figureNumber;
        set => _figureNumber = value;
    }

    public int TableNumber
    {
        get => _tableNumber;
        set => _tableNumber = value;
    }

    private Labels L => _options.Labels;

    /// <summary>Отрисовывает один топик. headingLevel = 1 для отдельной страницы.</summary>
    public string RenderTopic(DitaDocument document, DitaNode topic, int headingLevel = 1)
    {
        _document = document;
        _footnotes = new List<DitaNode>();

        var sb = new StringBuilder();
        var def = _catalog.Get(topic.Name);
        var cls = def?.ClassAttr.Contains("concept/concept") == true ? "concept"
            : def?.ClassAttr.Contains("task/task") == true ? "task"
            : def?.ClassAttr.Contains("reference/reference") == true ? "reference"
            : def?.ClassAttr.Contains("troubleshooting/") == true ? "troubleshooting"
            : def?.ClassAttr.Contains("glossentry/") == true ? "glossentry"
            : "topic";

        var id = topic.GetAttribute("id");
        sb.Append("<article class=\"").Append(cls).Append('"');
        if (!string.IsNullOrEmpty(id))
        {
            sb.Append(" id=\"").Append(Escape(id!)).Append('"');
        }

        sb.Append(">\n");

        foreach (var child in topic.ElementChildren())
        {
            if (!Include(child))
            {
                continue;
            }

            switch (child.Name)
            {
                case "title":
                case "glossterm":
                    sb.Append('<').Append(H(headingLevel)).Append('>')
                      .Append(RenderInlineChildren(child))
                      .Append("</").Append(H(headingLevel)).Append(">\n");
                    break;

                case "shortdesc":
                    sb.Append("<p class=\"shortdesc\">").Append(RenderInlineChildren(child)).Append("</p>\n");
                    break;

                case "abstract":
                case "glossdef":
                    sb.Append("<div class=\"abstract\">").Append(RenderChildren(child, headingLevel)).Append("</div>\n");
                    break;

                case "prolog":
                case "titlealts":
                    break;

                case "body":
                case "conbody":
                case "refbody":
                case "taskbody":
                case "troublebody":
                case "glossBody":
                case "learningBasebody":
                case "learningOverviewbody":
                case "learningContentbody":
                case "learningSummarybody":
                case "learningAssessmentbody":
                case "learningPlanbody":
                    sb.Append(RenderChildren(child, headingLevel));
                    break;

                case "related-links":
                    sb.Append(RenderRelatedLinks(child));
                    break;

                default:
                    if (_catalog.Get(child.Name)?.IsTopicType == true)
                    {
                        sb.Append(RenderNestedTopic(document, child, headingLevel + 1));
                    }
                    else
                    {
                        sb.Append(RenderNode(child, headingLevel));
                    }

                    break;
            }
        }

        sb.Append(RenderFootnotes());
        sb.Append("</article>\n");
        return sb.ToString();
    }

    private string RenderNestedTopic(DitaDocument document, DitaNode topic, int level)
    {
        var saved = _footnotes;
        var html = RenderTopic(document, topic, Math.Min(level, 6));
        _footnotes = saved;
        return html;
    }

    private static string H(int level) => "h" + Math.Clamp(level, 1, 6);

    private bool Include(DitaNode node) => _options.Filter?.Invoke(node) ?? true;

    // ------------------------------------------------------------------ узлы

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

        switch (node.Name)
        {
            // ------------------------------------------------------- блоки
            case "p":
                return $"<p{Attrs(node)}>{RenderInlineChildren(node)}</p>\n";

            case "div":
            case "bodydiv":
            case "conbodydiv":
            case "refbodydiv":
            case "sectiondiv":
            case "itemgroup":
            case "equation-block":
                return $"<div class=\"{node.Name}\"{Attrs(node)}>{RenderChildren(node, level)}</div>\n";

            case "section":
            case "example":
            case "refsyn":
            case "prereq":
            case "context":
            case "result":
            case "postreq":
            case "tasktroubleshooting":
            case "condition":
            case "cause":
            case "remedy":
            case "troubleSolution":
            case "steps-informal":
            case "lcIntro":
            case "lcObjectives":
            case "lcSummary":
            case "lcReview":
            case "lcNextSteps":
            case "lcPrereqs":
            case "lcResources":
            case "lcAudience":
            case "lcDuration":
                return RenderSection(node, level);

            case "ul":
                return $"<ul{Attrs(node)}>\n{RenderChildren(node, level)}</ul>\n";

            case "ol":
                return $"<ol{Attrs(node)}>\n{RenderChildren(node, level)}</ol>\n";

            case "sl":
                return $"<ul class=\"sl\"{Attrs(node)}>\n{RenderChildren(node, level)}</ul>\n";

            case "li":
            case "sli":
            case "choice":
            case "stepsection":
                return $"<li{Attrs(node)}>{RenderInlineChildren(node)}</li>\n";

            case "choices":
                return $"<ul class=\"choices\"{Attrs(node)}>\n{RenderChildren(node, level)}</ul>\n";

            case "steps":
                return GeneratedTitle(L.Steps) +
                       $"<ol class=\"steps\"{Attrs(node)}>\n{RenderChildren(node, level)}</ol>\n";

            case "substeps":
                return $"<ol class=\"steps\"{Attrs(node)}>\n{RenderChildren(node, level)}</ol>\n";

            case "steps-unordered":
                return GeneratedTitle(L.Steps) +
                       $"<ul class=\"steps\"{Attrs(node)}>\n{RenderChildren(node, level)}</ul>\n";

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

            case "pre":
            case "codeblock":
            case "screen":
            case "msgblock":
            case "lines":
                return $"<pre class=\"{node.Name}\"{Attrs(node)}>{RenderPreContent(node)}</pre>\n";

            case "fig":
            case "equation-figure":
            case "imagemap":
                return RenderFigure(node, level);

            case "table":
                return RenderTable(node, level);

            case "simpletable":
            case "properties":
            case "choicetable":
                return RenderSimpleTable(node, level);

            case "object":
                return RenderObject(node);

            case "video":
            case "audio":
                return RenderMedia(node);

            case "draft-comment":
                return _options.ShowDraftComments
                    ? $"<div class=\"draft-comment\">{RenderInlineChildren(node)}</div>\n"
                    : string.Empty;

            case "required-cleanup":
                return string.Empty;

            case "indexterm":
            case "data":
            case "data-about":
            case "resourceid":
            case "titlealts":
            case "prolog":
                return string.Empty;

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

        return _project.ResolveKey(keyref!.Split('/')[0])?.KeyText;
    }

    private string RenderAbbreviatedForm(DitaNode node)
    {
        var keyref = node.GetAttribute("keyref");
        if (string.IsNullOrWhiteSpace(keyref))
        {
            return string.Empty;
        }

        var keyDef = _project.ResolveKey(keyref!);
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

    // ------------------------------------------------------------ конструкции

    private string RenderSection(DitaNode node, int level)
    {
        var sb = new StringBuilder();
        sb.Append("<section class=\"").Append(node.Name).Append('"').Append(Attrs(node)).Append(">\n");

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
            sb.Append('<').Append(H(level + 1)).Append(" class=\"title\">")
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
            var keyDef = node.GetAttribute("keyref") is { } k ? _project.ResolveKey(k) : null;
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
            var keyDef = _project.ResolveKey(keyref!.Split('/')[0]);
            if (keyDef is not null)
            {
                label = keyDef.KeyText;
                if (keyDef.ResolvedPath is not null)
                {
                    target = _options.TopicLink?.Invoke(keyDef.ResolvedPath, null);
                }
                else if (keyDef.Href is not null)
                {
                    target = keyDef.Href;
                }
            }
        }

        if (target is null && !string.IsNullOrWhiteSpace(href))
        {
            if (RefResolver.IsExternal(href!) || node.GetAttribute("scope") is "external" or "peer")
            {
                target = href;
            }
            else if (_document.FilePath is not null)
            {
                var reference = RefResolver.Parse(_document.FilePath, href!);
                if (reference.Path is not null)
                {
                    target = _options.TopicLink?.Invoke(reference.Path, reference.ElementId ?? reference.TopicId)
                             ?? href;
                    label ??= TitleOf(reference);
                }
                else
                {
                    target = "#" + (reference.ElementId ?? reference.TopicId ?? string.Empty);
                    label ??= TitleOf(reference);
                }
            }
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

    // ------------------------------------------------------------- таблицы

    private string RenderTable(DitaNode node, int level)
    {
        var sb = new StringBuilder();
        var title = node.FirstElement("title");
        if (title is not null)
        {
            _tableNumber++;
            var caption = _options.NumberFiguresAndTables
                ? $"{L.Table} {_tableNumber}. {RenderInlineChildren(title)}"
                : RenderInlineChildren(title);
            sb.Append("<div class=\"table-title\">").Append(caption).Append("</div>\n");
        }

        foreach (var tgroup in node.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            sb.Append(RenderTgroup(tgroup, level));
        }

        return sb.ToString();
    }

    private string RenderTgroup(DitaNode tgroup, int level)
    {
        var colspecs = tgroup.ElementChildren().Where(e => e.Name == "colspec").ToList();
        var colNames = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < colspecs.Count; i++)
        {
            var name = colspecs[i].GetAttribute("colname");
            if (!string.IsNullOrEmpty(name))
            {
                colNames[name!] = i;
            }
        }

        var sb = new StringBuilder("<table>\n");
        if (colspecs.Count > 0)
        {
            sb.Append("<colgroup>\n");
            foreach (var cs in colspecs)
            {
                var width = cs.GetAttribute("colwidth");
                sb.Append("<col");
                if (!string.IsNullOrWhiteSpace(width) && width!.EndsWith("*", StringComparison.Ordinal) is false)
                {
                    sb.Append(" style=\"width:").Append(Escape(width!)).Append('"');
                }

                sb.Append(" />\n");
            }

            sb.Append("</colgroup>\n");
        }

        var thead = tgroup.FirstElement("thead");
        if (thead is not null)
        {
            sb.Append("<thead>\n");
            foreach (var row in thead.ElementChildren().Where(e => e.Name == "row"))
            {
                sb.Append(RenderRow(row, colNames, "th", level));
            }

            sb.Append("</thead>\n");
        }

        var tbody = tgroup.FirstElement("tbody");
        if (tbody is not null)
        {
            sb.Append("<tbody>\n");
            foreach (var row in tbody.ElementChildren().Where(e => e.Name == "row"))
            {
                sb.Append(RenderRow(row, colNames, "td", level));
            }

            sb.Append("</tbody>\n");
        }

        sb.Append("</table>\n");
        return sb.ToString();
    }

    private string RenderRow(DitaNode row, Dictionary<string, int> colNames, string cellTag, int level)
    {
        var sb = new StringBuilder("<tr>\n");
        foreach (var entry in row.ElementChildren().Where(e => e.Name == "entry"))
        {
            sb.Append('<').Append(cellTag);

            var namest = entry.GetAttribute("namest");
            var nameend = entry.GetAttribute("nameend");
            if (namest is not null && nameend is not null &&
                colNames.TryGetValue(namest, out var start) && colNames.TryGetValue(nameend, out var end))
            {
                var span = end - start + 1;
                if (span > 1)
                {
                    sb.Append(" colspan=\"").Append(span).Append('"');
                }
            }

            var morerows = entry.GetAttribute("morerows");
            if (int.TryParse(morerows, out var more) && more > 0)
            {
                sb.Append(" rowspan=\"").Append(more + 1).Append('"');
            }

            var align = entry.GetAttribute("align");
            var valign = entry.GetAttribute("valign");
            if (align is not null || valign is not null)
            {
                sb.Append(" style=\"");
                if (align is not null)
                {
                    sb.Append("text-align:").Append(Escape(align)).Append(';');
                }

                if (valign is not null)
                {
                    sb.Append("vertical-align:").Append(Escape(valign)).Append(';');
                }

                sb.Append('"');
            }

            sb.Append('>').Append(RenderInlineChildren(entry)).Append("</").Append(cellTag).Append(">\n");
        }

        sb.Append("</tr>\n");
        return sb.ToString();
    }

    private string RenderSimpleTable(DitaNode node, int level)
    {
        var headNames = node.Name switch
        {
            "properties" => new[] { "proptypehd", "propvaluehd", "propdeschd" },
            "choicetable" => new[] { "choptionhd", "chdeschd" },
            _ => new[] { "stentry" }
        };
        var rowNames = node.Name switch
        {
            "properties" => new[] { "proptype", "propvalue", "propdesc" },
            "choicetable" => new[] { "choption", "chdesc" },
            _ => new[] { "stentry" }
        };

        var sb = new StringBuilder();
        var spectitle = node.GetAttribute("spectitle");
        if (!string.IsNullOrWhiteSpace(spectitle))
        {
            sb.Append("<div class=\"table-title\">").Append(Escape(spectitle!)).Append("</div>\n");
        }

        sb.Append("<table class=\"").Append(node.Name).Append("\">\n");

        var head = node.ElementChildren().FirstOrDefault(e => e.Name is "sthead" or "prophead" or "chhead");
        if (head is not null)
        {
            sb.Append("<thead>\n<tr>\n");
            foreach (var cell in head.ElementChildren().Where(c => headNames.Contains(c.Name)))
            {
                sb.Append("<th>").Append(RenderInlineChildren(cell)).Append("</th>\n");
            }

            sb.Append("</tr>\n</thead>\n");
        }
        else if (node.Name is "properties" or "choicetable")
        {
            sb.Append("<thead>\n<tr>");
            if (node.Name == "properties")
            {
                sb.Append("<th>").Append(Escape(L.Type)).Append("</th><th>")
                  .Append(Escape(L.Value)).Append("</th><th>")
                  .Append(Escape(L.Description)).Append("</th>");
            }
            else
            {
                sb.Append("<th>").Append(Escape(L.Options)).Append("</th><th>")
                  .Append(Escape(L.Description)).Append("</th>");
            }

            sb.Append("</tr>\n</thead>\n");
        }

        sb.Append("<tbody>\n");
        foreach (var row in node.ElementChildren().Where(e => e.Name is "strow" or "property" or "chrow"))
        {
            sb.Append("<tr>\n");
            foreach (var cell in row.ElementChildren().Where(c => rowNames.Contains(c.Name)))
            {
                sb.Append("<td>").Append(RenderInlineChildren(cell)).Append("</td>\n");
            }

            sb.Append("</tr>\n");
        }

        sb.Append("</tbody>\n</table>\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------ служебное

    private string Attrs(DitaNode node)
    {
        var sb = new StringBuilder();
        var id = node.GetAttribute("id");
        if (!string.IsNullOrEmpty(id))
        {
            sb.Append(" id=\"").Append(Escape(id!)).Append('"');
        }

        var outputclass = node.GetAttribute("outputclass");
        if (!string.IsNullOrEmpty(outputclass))
        {
            sb.Append(" class=\"").Append(Escape(outputclass!)).Append('"');
        }

        return sb.ToString();
    }

    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string StripTags(string html)
    {
        var sb = new StringBuilder();
        var inside = false;
        foreach (var ch in html)
        {
            if (ch == '<')
            {
                inside = true;
            }
            else if (ch == '>')
            {
                inside = false;
            }
            else if (!inside)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Trim();
    }
}
