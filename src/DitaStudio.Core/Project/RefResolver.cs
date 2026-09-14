using DitaStudio.Core.Model;
using DitaStudio.Core.Validation;

namespace DitaStudio.Core.Project;

/// <summary>Разобранная ссылка DITA: путь к файлу, идентификатор топика и элемента.</summary>
public readonly struct DitaReference
{
    public DitaReference(string? path, string? topicId, string? elementId, string raw)
    {
        Path = path;
        TopicId = topicId;
        ElementId = elementId;
        Raw = raw;
    }

    public string? Path { get; }

    public string? TopicId { get; }

    public string? ElementId { get; }

    public string Raw { get; }

    public bool IsLocalFragment => Path is null && TopicId is not null;

    public override string ToString() => Raw;
}

/// <summary>
/// Разрешение ссылок: href, conref, conkeyref, keyref. Работает по правилам DITA:
/// «файл#идентификатор_топика/идентификатор_элемента».
/// </summary>
public static class RefResolver
{
    public static bool IsExternal(string href) =>
        href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        href.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
        href.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);

    public static DitaReference Parse(string baseFile, string href)
    {
        var raw = href;
        string? filePart = href;
        string? fragment = null;

        var hash = href.IndexOf('#');
        if (hash >= 0)
        {
            filePart = hash == 0 ? null : href[..hash];
            fragment = href[(hash + 1)..];
        }

        string? topicId = null;
        string? elementId = null;
        if (!string.IsNullOrEmpty(fragment))
        {
            var slash = fragment!.IndexOf('/');
            if (slash >= 0)
            {
                topicId = fragment[..slash];
                elementId = fragment[(slash + 1)..];
            }
            else
            {
                topicId = fragment;
            }
        }

        string? resolved = null;
        if (!string.IsNullOrEmpty(filePart) && !IsExternal(filePart!))
        {
            resolved = ResolvePath(baseFile, filePart!);
        }

        return new DitaReference(resolved, topicId, elementId, raw);
    }

    /// <summary>Абсолютный путь к цели относительно файла-источника.</summary>
    public static string? ResolvePath(string baseFile, string href)
    {
        if (string.IsNullOrWhiteSpace(href) || IsExternal(href))
        {
            return null;
        }

        var filePart = href;
        var hash = href.IndexOf('#');
        if (hash >= 0)
        {
            if (hash == 0)
            {
                return System.IO.Path.GetFullPath(baseFile);
            }

            filePart = href[..hash];
        }

        filePart = Uri.UnescapeDataString(filePart);

        try
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(baseFile));
            return dir is null
                ? System.IO.Path.GetFullPath(filePart)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, filePart));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Относительная ссылка от одного файла к другому (в стиле href).</summary>
    public static string MakeRelative(string fromFile, string toFile)
    {
        var fromDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(fromFile)) ?? string.Empty;
        var relative = System.IO.Path.GetRelativePath(fromDir, System.IO.Path.GetFullPath(toFile));
        return relative.Replace('\\', '/');
    }

    // ------------------------------------------------------------ поиск цели

    /// <summary>Находит элемент по разобранной ссылке.</summary>
    public static DitaNode? FindTarget(DitaProject project, DitaDocument sourceDoc, DitaReference reference)
    {
        DitaDocument? doc;
        if (reference.Path is null)
        {
            doc = sourceDoc;
        }
        else
        {
            if (!File.Exists(reference.Path))
            {
                return null;
            }

            doc = project.TryGetDocument(reference.Path);
        }

        if (doc is null)
        {
            return null;
        }

        if (reference.TopicId is null)
        {
            return doc.Root;
        }

        var topic = FindById(doc.Root, reference.TopicId);
        if (topic is null)
        {
            return null;
        }

        if (reference.ElementId is null)
        {
            return topic;
        }

        return FindById(topic, reference.ElementId);
    }

    public static DitaNode? FindById(DitaNode scope, string id)
    {
        foreach (var node in scope.DescendantsAndSelf())
        {
            if (node.Kind == NodeKind.Element && node.GetAttribute("id") == id)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>Разрешает conref / conkeyref в исходный элемент.</summary>
    public static DitaNode? ResolveConref(DitaProject project, DitaDocument sourceDoc, DitaNode node)
    {
        var conref = node.GetAttribute("conref");
        if (!string.IsNullOrWhiteSpace(conref))
        {
            var reference = Parse(sourceDoc.FilePath ?? project.RootPath, conref!);
            return FindTarget(project, sourceDoc, reference);
        }

        var conkeyref = node.GetAttribute("conkeyref");
        if (string.IsNullOrWhiteSpace(conkeyref))
        {
            return null;
        }

        var parts = conkeyref!.Split('/', 2);
        var keyDef = project.ResolveKey(parts[0]);
        if (keyDef?.ResolvedPath is null || !File.Exists(keyDef.ResolvedPath))
        {
            return null;
        }

        var target = project.TryGetDocument(keyDef.ResolvedPath);
        if (target is null)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return target.Root;
        }

        return FindById(target.Root, parts[1]);
    }

    /// <summary>
    /// Возвращает копию документа с раскрытыми conref/conkeyref — используется при публикации.
    /// </summary>
    public static DitaDocument ExpandConrefs(DitaProject project, DitaDocument document, int depth = 0)
    {
        var clone = new DitaDocument(document.Root.CloneDeep())
        {
            FilePath = document.FilePath,
            DoctypeName = document.DoctypeName,
            DoctypePublicId = document.DoctypePublicId,
            DoctypeSystemId = document.DoctypeSystemId
        };

        ExpandIn(project, document, clone.Root, depth);
        return clone;
    }

    private static void ExpandIn(DitaProject project, DitaDocument sourceDoc, DitaNode node, int depth)
    {
        if (depth > 12)
        {
            return;
        }

        foreach (var child in node.Children.ToList())
        {
            if (child.Kind != NodeKind.Element)
            {
                continue;
            }

            if (child.HasAttribute("conref") || child.HasAttribute("conkeyref"))
            {
                var target = ResolveConref(project, sourceDoc, child);
                if (target is not null)
                {
                    var replacement = target.CloneDeep();
                    replacement.RemoveAttribute("id");

                    // Атрибуты локального элемента имеют приоритет над заимствованными.
                    foreach (var attr in child.Attributes)
                    {
                        if (attr.Name is "conref" or "conkeyref" or "conrefend" or "conaction")
                        {
                            continue;
                        }

                        replacement.SetAttribute(attr.Name, attr.Value);
                    }

                    child.ReplaceWith(replacement);
                    ExpandIn(project, sourceDoc, replacement, depth + 1);
                    continue;
                }
            }

            ExpandIn(project, sourceDoc, child, depth);
        }
    }

    // ---------------------------------------------------------- проверка ссылок

    public static IReadOnlyList<ValidationIssue> ValidateReferences(DitaProject project, DitaDocument document)
    {
        var issues = new List<ValidationIssue>();
        var baseFile = document.FilePath;
        if (baseFile is null)
        {
            return issues;
        }

        foreach (var node in document.Root.DescendantsAndSelf())
        {
            if (node.Kind != NodeKind.Element)
            {
                continue;
            }

            CheckHref(project, document, node, "href", baseFile, issues);
            CheckHref(project, document, node, "conref", baseFile, issues);

            var keyref = node.GetAttribute("keyref");
            if (!string.IsNullOrWhiteSpace(keyref))
            {
                var key = keyref!.Split('/')[0];
                if (!project.KeyExistsAnywhere(key))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        $"Ключ \"{key}\" не объявлен ни в одной карте проекта.",
                        node,
                        baseFile));
                }
            }

            var conkeyref = node.GetAttribute("conkeyref");
            if (!string.IsNullOrWhiteSpace(conkeyref))
            {
                if (ResolveConref(project, document, node) is null)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        $"Не удалось разрешить conkeyref=\"{conkeyref}\".",
                        node,
                        baseFile));
                }
            }
        }

        return issues;
    }

    private static void CheckHref(
        DitaProject project,
        DitaDocument document,
        DitaNode node,
        string attribute,
        string baseFile,
        List<ValidationIssue> issues)
    {
        var href = node.GetAttribute(attribute);
        if (string.IsNullOrWhiteSpace(href))
        {
            return;
        }

        if (IsExternal(href!) || node.GetAttribute("scope") is "external" or "peer")
        {
            return;
        }

        var reference = Parse(baseFile, href!);
        if (reference.Path is not null && !File.Exists(reference.Path))
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                $"Файл по ссылке @{attribute}=\"{href}\" не найден.",
                node,
                baseFile));
            return;
        }

        var format = node.GetAttribute("format");
        if (format is not null && format is not ("dita" or "ditamap"))
        {
            return; // изображения, PDF и прочие ресурсы по фрагментам не проверяем
        }

        if (reference.TopicId is null)
        {
            return;
        }

        if (FindTarget(project, document, reference) is null)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                $"Не найден целевой элемент ссылки @{attribute}=\"{href}\".",
                node,
                baseFile));
        }
    }
}
