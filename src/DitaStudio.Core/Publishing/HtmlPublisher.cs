using System.Text;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Core.Publishing;

public sealed class PublishOptions
{
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Собрать всё в один HTML-файл (удобно для печати в PDF).</summary>
    public bool SingleFile { get; set; }

    public bool ShowDraftComments { get; set; }

    public string Language { get; set; } = "ru";

    /// <summary>Условная фильтрация: атрибут -> значения, которые нужно исключить.</summary>
    public Dictionary<string, HashSet<string>> ExcludeConditions { get; } = new(StringComparer.Ordinal);

    /// <summary>Правила подсветки (action="flag" из .ditaval) — цвет/фон/начертание по атрибуту.</summary>
    public List<DitavalFlagRule> FlagConditions { get; } = new();

    public bool CopyImages { get; set; } = true;
}

public sealed class PublishResult
{
    public PublishResult(string entryFile, IReadOnlyList<string> files, IReadOnlyList<string> warnings)
    {
        EntryFile = entryFile;
        Files = files;
        Warnings = warnings;
    }

    public string EntryFile { get; }

    public IReadOnlyList<string> Files { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Сборка публикации: карта превращается в набор HTML-страниц с оглавлением
/// или в один файл, пригодный для печати в PDF.
/// </summary>
public sealed class HtmlPublisher
{
    private readonly DitaProject _project;

    public HtmlPublisher(DitaProject project)
    {
        _project = project;
    }

    public PublishResult Publish(string mapPath, PublishOptions options)
    {
        var tree = MapTree.Build(_project, mapPath);
        var labels = Labels.For(options.Language);
        var warnings = new List<string>();
        var written = new List<string>();

        Directory.CreateDirectory(options.OutputDirectory);

        var topics = tree.PublicationOrder.ToList();
        var fileNames = AssignFileNames(topics);
        var mediaMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var cssPath = Path.Combine(options.OutputDirectory, "style.css");
        File.WriteAllText(cssPath, Assets.StyleSheet, new UTF8Encoding(false));
        written.Add(cssPath);

        var customCss = LoadCustomCss(out var customCssWarning);
        if (customCssWarning is not null)
        {
            warnings.Add(customCssWarning);
        }

        string ImageSource(string absolute)
        {
            if (!options.CopyImages)
            {
                return absolute.Replace('\\', '/');
            }

            if (mediaMap.TryGetValue(absolute, out var existing))
            {
                return existing;
            }

            if (!File.Exists(absolute))
            {
                warnings.Add($"Изображение не найдено: {absolute}");
                return Path.GetFileName(absolute);
            }

            var mediaDir = Path.Combine(options.OutputDirectory, "media");
            Directory.CreateDirectory(mediaDir);
            var name = Path.GetFileName(absolute);
            var target = Path.Combine(mediaDir, name);
            var counter = 1;
            while (File.Exists(target) &&
                   !string.Equals(Path.GetFullPath(target), absolute, StringComparison.OrdinalIgnoreCase))
            {
                name = $"{Path.GetFileNameWithoutExtension(absolute)}-{counter++}{Path.GetExtension(absolute)}";
                target = Path.Combine(mediaDir, name);
            }

            try
            {
                File.Copy(absolute, target, true);
                written.Add(target);
            }
            catch (Exception ex)
            {
                warnings.Add($"Не удалось скопировать {absolute}: {ex.Message}");
            }

            var relative = "media/" + name;
            mediaMap[absolute] = relative;
            return relative;
        }

        var renderOptions = new RenderOptions
        {
            Labels = labels,
            ShowDraftComments = options.ShowDraftComments,
            ImageSource = ImageSource,
            Filter = node => PublishFilter.IsIncluded(node, options),
            FlagRules = options.FlagConditions
        };

        if (options.SingleFile)
        {
            renderOptions.SingleFileAnchors = true;
            renderOptions.TopicLink = (path, id) =>
            {
                var full = Path.GetFullPath(path);
                if (fileNames.TryGetValue(full, out _))
                {
                    return "#" + AnchorFor(full, id ?? RootIdOf(full));
                }

                return id is null ? "#" : "#" + id;
            };

            var entry = PublishSingleFile(tree, topics, renderOptions, options, labels, customCss);
            written.Add(entry);
            return new PublishResult(entry, written, warnings);
        }

        renderOptions.TopicLink = (path, id) =>
        {
            var full = Path.GetFullPath(path);
            if (fileNames.TryGetValue(full, out var name))
            {
                return id is null ? name : $"{name}#{id}";
            }

            return Path.GetFileName(path);
        };

        var renderer = new HtmlRenderer(_project, renderOptions);
        var toc = BuildToc(tree, fileNames, null);

        for (var i = 0; i < topics.Count; i++)
        {
            var item = topics[i];
            var path = item.TargetPath!;
            var doc = _project.TryGetDocument(path);
            if (doc is null)
            {
                warnings.Add($"Не удалось прочитать {path}");
                continue;
            }

            var expanded = RefResolver.ExpandConrefs(_project, doc);
            var topicNode = item.TargetTopicId is null
                ? expanded.Root
                : RefResolver.FindById(expanded.Root, item.TargetTopicId) ?? expanded.Root;

            renderOptions.CurrentKeyScope = item.KeyScopeChain;
            renderOptions.RelatedTopics = tree.RelatedLinks.TryGetValue(Path.GetFullPath(path), out var related) ? related : null;
            var body = renderer.RenderTopic(expanded, topicNode);
            var pageToc = BuildToc(tree, fileNames, item);
            var previous = i > 0 ? topics[i - 1] : null;
            var next = i + 1 < topics.Count ? topics[i + 1] : null;

            var html = Page(
                item.Title,
                pageToc,
                body + Pager(previous, next, fileNames),
                labels,
                customCss);

            var outPath = Path.Combine(options.OutputDirectory, fileNames[Path.GetFullPath(path)]);
            File.WriteAllText(outPath, html, new UTF8Encoding(false));
            written.Add(outPath);
        }

        var indexPath = Path.Combine(options.OutputDirectory, "index.html");
        if (!written.Any(w => string.Equals(w, indexPath, StringComparison.OrdinalIgnoreCase)))
        {
            var first = topics.FirstOrDefault();
            var indexBody = new StringBuilder()
                .Append("<h1>").Append(HtmlRenderer.Escape(tree.Root.Title)).Append("</h1>\n")
                .Append(first is null
                    ? "<p>В карте нет топиков.</p>"
                    : $"<p><a href=\"{fileNames[Path.GetFullPath(first.TargetPath!)]}\">{HtmlRenderer.Escape(first.Title)}</a></p>")
                .ToString();
            File.WriteAllText(indexPath, Page(tree.Root.Title, toc, indexBody, labels, customCss), new UTF8Encoding(false));
            written.Add(indexPath);
        }

        return new PublishResult(indexPath, written, warnings);
    }

    private string PublishSingleFile(
        MapTree tree,
        List<MapItem> topics,
        RenderOptions renderOptions,
        PublishOptions options,
        Labels labels,
        string? customCss)
    {
        var renderer = new HtmlRenderer(_project, renderOptions);
        var body = new StringBuilder();

        body.Append("<h1 class=\"book-title\">").Append(HtmlRenderer.Escape(tree.Root.Title)).Append("</h1>\n");
        body.Append("<nav class=\"toc-inline\">\n<h2>").Append(HtmlRenderer.Escape(labels.Contents)).Append("</h2>\n<ul>\n");
        foreach (var item in topics)
        {
            var full = Path.GetFullPath(item.TargetPath!);
            var anchor = AnchorFor(full, item.TargetTopicId ?? RootIdOf(full));
            body.Append("<li style=\"margin-left:").Append(Math.Max(0, item.Level - 1) * 16).Append("px\">")
                .Append("<a href=\"#").Append(anchor).Append("\">")
                .Append(HtmlRenderer.Escape(item.Title)).Append("</a></li>\n");
        }

        body.Append("</ul>\n</nav>\n");

        foreach (var item in topics)
        {
            var doc = _project.TryGetDocument(item.TargetPath!);
            if (doc is null)
            {
                continue;
            }

            var expanded = RefResolver.ExpandConrefs(_project, doc);
            var topicNode = item.TargetTopicId is null
                ? expanded.Root
                : RefResolver.FindById(expanded.Root, item.TargetTopicId) ?? expanded.Root;

            var anchor = AnchorFor(Path.GetFullPath(item.TargetPath!), item.TargetTopicId ?? doc.Root.GetAttribute("id"));
            var level = Math.Clamp(item.Level, 1, 5);
            body.Append("<div class=\"topic-chunk").Append(level == 1 ? " chapter-heading" : string.Empty)
                .Append("\" id=\"").Append(anchor).Append("\">\n");
            renderOptions.CurrentKeyScope = item.KeyScopeChain;
            renderOptions.RelatedTopics = tree.RelatedLinks.TryGetValue(Path.GetFullPath(item.TargetPath!), out var related)
                ? related
                : null;
            body.Append(renderer.RenderTopic(expanded, topicNode, level));
            body.Append("</div>\n");
        }

        body.Append(renderer.RenderIndexSection());

        var html = Page(tree.Root.Title, null, body.ToString(), labels, customCss);
        var outPath = Path.Combine(options.OutputDirectory, SafeFileName(tree.Root.Title) + ".html");
        File.WriteAllText(outPath, html, new UTF8Encoding(false));
        return outPath;
    }

    /// <summary>Отрисовка одного топика для панели предпросмотра.</summary>
    public string RenderPreview(DitaDocument document, PublishOptions? options = null)
    {
        options ??= new PublishOptions();
        var labels = Labels.For(options.Language);
        var baseDir = document.FilePath is null ? _project.RootPath : Path.GetDirectoryName(document.FilePath)!;

        var renderOptions = new RenderOptions
        {
            Labels = labels,
            ShowDraftComments = true,
            ImageSource = absolute => new Uri(absolute).AbsoluteUri,
            TopicLink = (path, id) => id is null ? new Uri(path).AbsoluteUri : new Uri(path).AbsoluteUri + "#" + id,
            // В отличие от Publish() — предпросмотр показывает помеченное на удаление содержимое
            // (зачёркнутым, см. .tc-deleted), чтобы правку можно было принять/отклонить осознанно.
            Filter = node => PublishFilter.IsIncluded(node, options, showTrackedDeletions: true),
            FlagRules = options.FlagConditions
        };

        var renderer = new HtmlRenderer(_project, renderOptions);

        string body;
        if (document.Kind == DitaDocumentKind.Map)
        {
            body = RenderMapPreview(document, labels);
        }
        else
        {
            DitaDocument expanded;
            try
            {
                expanded = RefResolver.ExpandConrefs(_project, document);
            }
            catch
            {
                expanded = document;
            }

            body = renderer.RenderTopic(expanded, expanded.Root);
        }

        _ = baseDir;
        return Page(document.Title, null, body, labels, LoadCustomCss(out _));
    }

    private string RenderMapPreview(DitaDocument document, Labels labels)
    {
        if (document.FilePath is null)
        {
            return "<p>Карта не сохранена.</p>";
        }

        var tree = MapTree.Build(_project, document.FilePath);
        var sb = new StringBuilder();
        sb.Append("<h1>").Append(HtmlRenderer.Escape(tree.Root.Title)).Append("</h1>\n");
        sb.Append("<nav class=\"toc-inline\"><ul>\n");
        foreach (var item in tree.Items.Skip(1))
        {
            sb.Append("<li style=\"margin-left:").Append(Math.Max(0, item.Level - 1) * 16).Append("px\">")
              .Append(HtmlRenderer.Escape(item.Title));
            if (item.IsBroken)
            {
                sb.Append(" <span style=\"color:#c0392b\">— файл не найден</span>");
            }
            else if (item.IsResourceOnly)
            {
                sb.Append(" <span style=\"color:#5b6472\">— только ресурс</span>");
            }

            sb.Append("</li>\n");
        }

        sb.Append("</ul></nav>\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------- служебное

    private static Dictionary<string, string> AssignFileNames(IEnumerable<MapItem> topics)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "index.html", "style.css" };

        foreach (var item in topics)
        {
            var full = Path.GetFullPath(item.TargetPath!);
            if (result.ContainsKey(full))
            {
                continue;
            }

            var baseName = SafeFileName(Path.GetFileNameWithoutExtension(full));
            var name = baseName + ".html";
            var counter = 1;
            while (!used.Add(name))
            {
                name = $"{baseName}-{counter++}.html";
            }

            result[full] = name;
        }

        return result;
    }

    internal static string AnchorFor(string fullPath, string? id)
    {
        var basePart = SafeFileName(Path.GetFileNameWithoutExtension(fullPath));
        return id is null ? basePart : $"{basePart}--{id}";
    }

    /// <summary>Id корневого элемента топика — используется как запасной идентификатор для
    /// якоря, когда ссылка на файл не повторяет id в фрагменте (обычный случай для topicref
    /// без "#…" в href), чтобы такой self-anchor совпадал с id, который получит div-обёртка
    /// топика в однофайловой сборке.</summary>
    private string? RootIdOf(string fullPath) => _project.TryGetDocument(fullPath)?.Root.GetAttribute("id");

    private static string SafeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is ' ' or '.' or '/' or '\\')
            {
                sb.Append('-');
            }
        }

        var result = sb.ToString().Trim('-');
        return result.Length == 0 ? "topic" : result;
    }

    private static string BuildToc(MapTree tree, IReadOnlyDictionary<string, string> fileNames, MapItem? current)
    {
        var sb = new StringBuilder();
        sb.Append("<ul>\n");
        foreach (var child in tree.Root.Children)
        {
            AppendTocItem(sb, child, fileNames, current);
        }

        sb.Append("</ul>\n");
        return sb.ToString();
    }

    private static void AppendTocItem(
        StringBuilder sb,
        MapItem item,
        IReadOnlyDictionary<string, string> fileNames,
        MapItem? current)
    {
        if (item.IsResourceOnly && item.Children.Count == 0)
        {
            return;
        }

        sb.Append("<li");
        if (item.TargetPath is null || item.IsBroken || item.IsResourceOnly)
        {
            sb.Append(" class=\"head\"><span>").Append(HtmlRenderer.Escape(item.Title)).Append("</span>");
        }
        else
        {
            var full = Path.GetFullPath(item.TargetPath);
            var href = fileNames.TryGetValue(full, out var name) ? name : "#";
            var cls = ReferenceEquals(item, current) ? " class=\"current\"" : string.Empty;
            sb.Append("><a href=\"").Append(href).Append('"').Append(cls).Append('>')
              .Append(HtmlRenderer.Escape(item.Title)).Append("</a>");
        }

        if (item.Children.Count > 0)
        {
            sb.Append("\n<ul>\n");
            foreach (var child in item.Children)
            {
                AppendTocItem(sb, child, fileNames, current);
            }

            sb.Append("</ul>\n");
        }

        sb.Append("</li>\n");
    }

    private static string Pager(MapItem? previous, MapItem? next, IReadOnlyDictionary<string, string> fileNames)
    {
        if (previous is null && next is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder("<div class=\"pager\">");
        sb.Append("<span>");
        if (previous?.TargetPath is not null && fileNames.TryGetValue(Path.GetFullPath(previous.TargetPath), out var p))
        {
            sb.Append("&larr; <a href=\"").Append(p).Append("\">").Append(HtmlRenderer.Escape(previous.Title)).Append("</a>");
        }

        sb.Append("</span><span>");
        if (next?.TargetPath is not null && fileNames.TryGetValue(Path.GetFullPath(next.TargetPath), out var n))
        {
            sb.Append("<a href=\"").Append(n).Append("\">").Append(HtmlRenderer.Escape(next.Title)).Append("</a> &rarr;");
        }

        sb.Append("</span></div>\n");
        return sb.ToString();
    }

    private static string Page(string title, string? toc, string body, Labels labels, string? customCss = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"ru\">\n<head>\n<meta charset=\"utf-8\" />\n");
        sb.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
        sb.Append("<title>").Append(HtmlRenderer.Escape(title)).Append("</title>\n");
        sb.Append("<link rel=\"stylesheet\" href=\"style.css\" />\n");
        sb.Append("<style>\n").Append(Assets.StyleSheet).Append("\n</style>\n");
        if (!string.IsNullOrEmpty(customCss))
        {
            sb.Append("<style class=\"custom-css\">\n").Append(customCss).Append("\n</style>\n");
        }

        sb.Append("</head>\n<body>\n<div class=\"layout\">\n");

        if (toc is not null)
        {
            sb.Append("<aside class=\"toc\">\n<h2>").Append(HtmlRenderer.Escape(labels.Contents)).Append("</h2>\n")
              .Append(toc).Append("</aside>\n");
        }

        sb.Append("<main>\n").Append(body).Append("\n</main>\n</div>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Читает подключённый к проекту файл пользовательских стилей (см. DitaProject.CustomCssPath).
    /// Возвращает null, если CSS не подключён; предупреждение — если подключён, но файл не найден.</summary>
    private string? LoadCustomCss(out string? warning)
    {
        warning = null;
        if (string.IsNullOrEmpty(_project.CustomCssPath))
        {
            return null;
        }

        var path = Path.Combine(_project.RootPath, _project.CustomCssPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            warning = $"Пользовательский файл стилей не найден: {path}";
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            warning = $"Не удалось прочитать файл стилей {path}: {ex.Message}";
            return null;
        }
    }

}
