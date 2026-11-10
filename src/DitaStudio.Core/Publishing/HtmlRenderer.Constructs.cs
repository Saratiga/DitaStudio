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
            _lastHeadingNumber = null;
            var sectionNumber = HeadingNumber(level + 1, TocRules.IsUnnumbered(explicitTitle));
            _xrefs.Record(_document.FilePath, node.GetAttribute("id"), XrefTargetKind.Section, null, _lastHeadingNumber, explicitTitle.InnerText);
            sb.Append('<').Append(H(level + 1)).Append(MergedClassAttr("title", explicitTitle)).Append('>')
              .Append(sectionNumber)
              .Append(RenderInlineChildren(explicitTitle))
              .Append("</").Append(H(level + 1)).Append(">\n");
        }
        else if (label is not null)
        {
            sb.Append('<').Append(H(level + 1)).Append(" class=\"generated-title\">")
              .Append(HeadingNumber(level + 1, unnumbered: false))
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
        return $"<div{ClassedAttrs("note " + type, node)}><span class=\"label\">{Escape(label)}:</span> {RenderInlineChildren(node)}</div>\n";
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
                sb.Append("<dt").Append(OptionalClassAttr(dt)).Append('>').Append(RenderInlineChildren(dt)).Append("</dt>\n");
            }

            foreach (var dd in entry.ElementChildren().Where(e => e.Name == "dd"))
            {
                sb.Append("<dd").Append(OptionalClassAttr(dd)).Append('>').Append(RenderInlineChildren(dd)).Append("</dd>\n");
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
                sb.Append("<dt").Append(OptionalClassAttr(pt)).Append('>').Append(RenderInlineChildren(pt)).Append("</dt>\n");
            }

            foreach (var pd in entry.ElementChildren().Where(e => e.Name == "pd"))
            {
                sb.Append("<dd").Append(OptionalClassAttr(pd)).Append('>').Append(RenderInlineChildren(pd)).Append("</dd>\n");
            }
        }

        sb.Append("</dl>\n");
        return sb.ToString();
    }

    private string RenderFigure(DitaNode node, int level)
    {
        var sb = new StringBuilder("<figure").Append(Attrs(node)).Append(">\n");
        var title = node.FirstElement("title");
        if (CaptionRules.HasCaption(title))
        {
            _figureNumber++;
            var hasText = CaptionRules.HasContent(title);
            RecordCaption(node, XrefTargetKind.Figure, L.Figure, _figureNumber, title!);
            var caption = _options.NumberFiguresAndTables
                ? $"{L.Figure} {_figureNumber}" + (hasText ? CaptionRules.Separator(_options.CaptionSeparator) + RenderInlineChildren(title!) : string.Empty)
                : RenderInlineChildren(title!);
            if (caption.Length > 0)
            {
                sb.Append("<figcaption class=\"fig-title\">").Append(caption).Append("</figcaption>\n");
            }
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
                if (_options.MarkMissingImages && !File.Exists(abs))
                {
                    var missing = $"<span class=\"image-missing\">Картинка не найдена: {Escape(href!)}</span>";
                    return node.GetAttribute("placement") == "break" ? $"<div class=\"image-block\">{missing}</div>\n" : missing;
                }

                src = _options.ImageSource?.Invoke(abs) ?? href!;
            }
        }

        var alt = node.GetAttribute("alt") ?? node.FirstElement("alt")?.InnerText ?? string.Empty;
        var sb = new StringBuilder("<img src=\"").Append(Escape(src)).Append('"');
        sb.Append(" alt=\"").Append(Escape(alt)).Append('"');

        // Атрибуты width/height в HTML понимают только пиксели: width="170mm" браузер читает как
        // 170px. Поэтому пиксели (число без единицы или px) идут атрибутом, прочие единицы DITA
        // (mm, cm, in, pt, pc, em) — в style как есть, без пересчёта.
        var style = new StringBuilder();
        foreach (var name in new[] { "width", "height" })
        {
            switch (ImageDimension(node.GetAttribute(name)))
            {
                case (var number, ""):
                    sb.Append(' ').Append(name).Append("=\"").Append(number).Append('"');
                    break;
                case (var number, var unit):
                    style.Append(name).Append(':').Append(number).Append(unit).Append(';');
                    break;
            }
        }

        if (style.Length > 0)
        {
            sb.Append(" style=\"").Append(style).Append('"');
        }

        sb.Append(" />");

        return node.GetAttribute("placement") == "break"
            ? $"<div class=\"image-block\">{sb}</div>\n"
            : sb.ToString();
    }

    /// <summary>Размер картинки из атрибута DITA: число (в инвариантной записи) и единица CSS;
    /// пиксели — с пустой единицей. Нераспознанное значение — null: его не выводим совсем, чтобы
    /// произвольный текст не попал в style.</summary>
    private static (string Number, string Unit)? ImageDimension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().ToLowerInvariant();
        var numberLength = 0;
        while (numberLength < text.Length && (char.IsDigit(text[numberLength]) || text[numberLength] is '.' or ','))
        {
            numberLength++;
        }

        if (!double.TryParse(text[..numberLength].Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return null;
        }

        var unit = text[numberLength..].Trim();
        var formatted = number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return unit switch
        {
            "" or "px" => (formatted, string.Empty),
            "mm" or "cm" or "in" or "pt" or "pc" or "em" or "%" => (formatted, unit),
            _ => null
        };
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
            var fallback = label ?? target ?? href ?? string.Empty;
            // Пустая ссылка на рисунок, таблицу, пункт, сноску, раздел: номер или подпись цели — после отрисовки всего издания.
            inner = string.IsNullOrWhiteSpace(keyref) && XrefTargetOf(node, href) is var (path, id)
                ? _xrefs.Token(path, id, CrossReferenceLabels.FormOf(node), fallback)
                : Escape(fallback);
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return $"<span class=\"xref-broken\">{inner}</span>";
        }

        return $"<a href=\"{Escape(target!)}\">{inner}</a>";
    }

    /// <summary>Цель ссылки внутри DITA-файла для подписи по номеру: путь и id элемента (у ссылки на топик с видом текста,
    /// отличным от названия, — id топика). null — внешняя ссылка или ссылка на топик целиком.</summary>
    private (string? Path, string Id)? XrefTargetOf(DitaNode node, string? href) => CrossReferenceLabels.TargetOf(node, _document.FilePath);

    /// <summary>Подпись рисунка или таблицы — цель перекрёстных ссылок.</summary>
    private void RecordCaption(DitaNode node, XrefTargetKind kind, string word, int number, DitaNode title)
    {
        var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _xrefs.Record(_document.FilePath, node.GetAttribute("id"), kind,
            _options.NumberFiguresAndTables ? word + " " + text : null, text, title.InnerText, CaptionRules.Separator(_options.CaptionSeparator));
    }

    /// <summary>Пункт нумерованного списка или шаг — цель ссылок с номером («3»); у маркированного списка номера нет.</summary>
    private void RecordListItem(DitaNode node)
    {
        if (string.IsNullOrWhiteSpace(node.GetAttribute("id")) || node.Parent is not { } list)
        {
            return;
        }

        var ordered = list.Name is "ol" or "steps" or "substeps";
        var number = ordered
            ? (list.ElementChildren().Where(e => e.Name == node.Name).TakeWhile(e => !ReferenceEquals(e, node)).Count() + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
        var text = node.Name is "step" or "substep" ? node.FirstElement("cmd")?.InnerText ?? node.InnerText : node.InnerText;
        _xrefs.Record(_document.FilePath, node.GetAttribute("id"), XrefTargetKind.NumberedItem, null, number, text);
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

        var title = topic.FirstElement("title")?.InnerText.Trim();
        return string.IsNullOrEmpty(title) ? doc.Title : title;
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
