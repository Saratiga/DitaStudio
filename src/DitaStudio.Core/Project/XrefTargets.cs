using System.Globalization;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Project;

/// <summary>
/// Элемент, на который можно поставить перекрёстную ссылку (окно «Перекрёстная ссылка», как в Word):
/// <paramref name="Number"/> — номер по порядку в этом документе («Рисунок 2»), в издании он может быть другим
/// (нумерация сквозная), поэтому в ссылке номер ставится при публикации.
/// </summary>
public sealed record XrefTarget(XrefTargetKind Kind, DitaNode Node, string? TopicId, string Text, string? Number)
{
    /// <summary>Строка списка: «Рисунок 2. Схема», «3. Нажмите кнопку», «Установка».</summary>
    public string Display(Labels labels) => Kind switch
    {
        XrefTargetKind.Figure => labels.Figure + " " + Number + (Text.Length > 0 ? ". " + Text : string.Empty),
        XrefTargetKind.Table => labels.Table + " " + Number + (Text.Length > 0 ? ". " + Text : string.Empty),
        XrefTargetKind.NumberedItem or XrefTargetKind.Footnote when Number is not null => Number + ". " + Text,
        _ => Text
    };

    public override string ToString() => Display(Labels.For(Loc.Instance.Language));
}

/// <summary>Цели перекрёстных ссылок в документе по видам — для окна вставки ссылки.</summary>
public static class XrefTargets
{
    private const int TextLimit = 80;

    /// <summary>Префикс id, который получает цель без id (id нужен ссылке).</summary>
    public static string IdPrefix(XrefTargetKind kind) => kind switch
    {
        XrefTargetKind.Figure => "fig",
        XrefTargetKind.Table => "table",
        XrefTargetKind.NumberedItem => "item",
        XrefTargetKind.Footnote => "fn",
        XrefTargetKind.Section => "section",
        _ => "topic"
    };

    /// <summary>Все цели вида <paramref name="kind"/> в документе по порядку текста.</summary>
    public static IReadOnlyList<XrefTarget> In(DitaDocument document, XrefTargetKind kind)
    {
        var result = new List<XrefTarget>();
        var catalog = DitaCatalog.Default;
        int figures = 0, tables = 0, footnotes = 0, numbered = 0;
        foreach (var node in document.Root.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element))
        {
            var topic = TopicOf(node, catalog);
            switch (node.Name)
            {
                case "fig" when CaptionRules.HasCaption(node.FirstElement("title")):
                    figures++;
                    Add(XrefTargetKind.Figure, node, topic, node.FirstElement("title")!.InnerText, figures);
                    break;

                case "table" when CaptionRules.HasCaption(node.FirstElement("title")):
                    tables++;
                    Add(XrefTargetKind.Table, node, topic, node.FirstElement("title")!.InnerText, tables);
                    break;

                case "fn":
                    footnotes++;
                    Add(XrefTargetKind.Footnote, node, topic, node.InnerText,
                        string.IsNullOrWhiteSpace(node.GetAttribute("callout")) ? footnotes : null, node.GetAttribute("callout"));
                    break;

                case "p" when HeadingNumbering.IsNumbered(node):
                    numbered++;
                    Add(XrefTargetKind.NumberedItem, node, topic, node.InnerText, numbered);
                    break;

                case "li" when node.Parent?.Name == "ol":
                case "step" or "substep":
                    var index = node.Parent!.ElementChildren().Where(e => e.Name == node.Name).TakeWhile(e => !ReferenceEquals(e, node)).Count() + 1;
                    var text = node.Name is "step" or "substep" ? node.FirstElement("cmd")?.InnerText ?? node.InnerText : OwnText(node);
                    Add(XrefTargetKind.NumberedItem, node, topic, text, index);
                    break;

                case "section" or "example" when node.FirstElement("title") is { } sectionTitle:
                    Add(XrefTargetKind.Section, node, topic, sectionTitle.InnerText, null);
                    break;

                default:
                    if (catalog.Get(node.Name)?.IsTopicType == true)
                    {
                        Add(XrefTargetKind.Topic, node, node.GetAttribute("id"), node.FirstElement("title")?.InnerText ?? document.Title, null);
                    }

                    break;
            }
        }

        return result;

        void Add(XrefTargetKind found, DitaNode node, string? topicId, string text, int? number, string? callout = null)
        {
            if (found != kind)
            {
                return;
            }

            text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length > TextLimit)
            {
                text = text[..(TextLimit - 1)] + "…";
            }

            result.Add(new XrefTarget(found, node, topicId, text, callout ?? number?.ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>Текст пункта без вложенных списков и блоков.</summary>
    private static string OwnText(DitaNode item) =>
        string.Concat(item.Children.Where(c => c.Kind == NodeKind.Text || (c.Kind == NodeKind.Element && DitaCatalog.Default.Get(c.Name)?.IsInline == true))
            .Select(c => c.Kind == NodeKind.Text ? c.Value : c.InnerText));

    private static string? TopicOf(DitaNode node, DitaCatalog catalog)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (catalog.Get(current.Name)?.IsTopicType == true)
            {
                return current.GetAttribute("id");
            }
        }

        return null;
    }

    /// <summary>
    /// id цели (есть — как есть, нет — новый уникальный в документе, записывается в элемент). Возвращает id и признак,
    /// что документ изменён.
    /// </summary>
    public static (string Id, bool Added) EnsureId(DitaDocument document, XrefTarget target)
    {
        if (target.Node.GetAttribute("id") is { Length: > 0 } existing)
        {
            return (existing, false);
        }

        var id = EditCommands.GenerateId(document, IdPrefix(target.Kind));
        target.Node.SetAttribute("id", id);
        document.IsDirty = true;
        return (id, true);
    }

    /// <summary>href ссылки из <paramref name="fromFile"/> на цель в <paramref name="targetFile"/>: «file.dita#topic/element».</summary>
    public static string Href(string fromFile, string targetFile, XrefTarget target, string id)
    {
        var href = string.Equals(Path.GetFullPath(fromFile), Path.GetFullPath(targetFile), StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : RefResolver.MakeRelative(fromFile, targetFile);
        if (target.Kind == XrefTargetKind.Topic)
        {
            return href.Length == 0 ? "#" + id : href + "#" + id;
        }

        return (href.Length == 0 ? "#" : href + "#") + (target.TopicId is { Length: > 0 } topic ? topic + "/" + id : id);
    }

    /// <summary>
    /// Ссылка на сторонний ресурс (сайт, программа, файл вне проекта): <c>href</c> и <c>format</c> для
    /// <c>&lt;xref scope="external"&gt;</c>. Адрес без схемы вида «example.com» получает https://, путь к файлу
    /// («C:\Program Files\app.exe», «/usr/bin/app») — URI file:///, остальные схемы (mailto:, ftp:, tel:…) остаются как есть.
    /// Пустой адрес — null.
    /// </summary>
    public static (string Href, string Format)? ExternalHref(string? address)
    {
        var text = (address ?? string.Empty).Trim().Trim('"');
        if (text.Length == 0)
        {
            return null;
        }

        var isWindowsPath = text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && (text[2] == '\\' || text[2] == '/');
        var isUncPath = text.StartsWith(@"\\", StringComparison.Ordinal);
        var isUnixPath = text.StartsWith('/') && !text.StartsWith("//", StringComparison.Ordinal);
        if (isWindowsPath || isUncPath || isUnixPath)
        {
            var uri = new Uri(isUnixPath ? "file://" + text : text).AbsoluteUri;
            return (uri, FormatOfPath(text));
        }

        var schemeEnd = text.IndexOf(':');
        var hasScheme = schemeEnd > 1 && text[..schemeEnd].All(c => char.IsLetterOrDigit(c) || c is '+' or '-' or '.');
        if (!hasScheme)
        {
            text = "https://" + text.TrimStart('/');
        }

        if (text.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            return (text, "html");
        }

        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(text, UriKind.Absolute, out var file))
        {
            return (text, FormatOfPath(file.LocalPath));
        }

        return (text, "html");
    }

    /// <summary>format для файла — его расширение (exe, pdf, docx…), как велит DITA для не-DITA ресурсов; без расширения — html.</summary>
    private static string FormatOfPath(string path)
    {
        var extension = Path.GetExtension(path.TrimEnd('/', '\\')).TrimStart('.').ToLowerInvariant();
        return extension.Length > 0 ? extension : "html";
    }
}
