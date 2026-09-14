using DitaStudio.Core.Model;
using DitaStudio.Core.Templates;

namespace DitaStudio.Core.Project;

/// <summary>Итог операции рефакторинга: сколько ссылок обновлено и какие документы изменились
/// (только в памяти — на диск ничего не пишет, кроме физического переноса файла в MoveFile,
/// без которого сама операция не имеет смысла). Вызывающая сторона решает, что сохранить.</summary>
public sealed record RefactorResult(int UpdatedReferences, IReadOnlyList<DitaDocument> ChangedDocuments);

/// <summary>
/// Переименование id и перенос файла с автоматическим обновлением ссылок (href/conref) по
/// всему проекту. Не трогает keyref/conkeyref — они ссылаются на ключ, а не на путь+id напрямую,
/// и не ломаются при переименовании/переносе (если сам keydef не трогать).
/// </summary>
public static class RefactorService
{
    /// <summary>Переименовывает id элемента в документе и обновляет все href/conref в проекте,
    /// указывающие на этот id (как topicId, так и elementId — фрагмент может ссылаться на любой
    /// из них). Ничего не делает, если элемент с oldId не найден.</summary>
    public static RefactorResult RenameId(DitaProject project, string filePath, string oldId, string newId)
    {
        var fullPath = System.IO.Path.GetFullPath(filePath);
        var doc = project.TryGetDocument(fullPath);
        var target = doc is null ? null : RefResolver.FindById(doc.Root, oldId);
        if (doc is null || target is null)
        {
            return new RefactorResult(0, Array.Empty<DitaDocument>());
        }

        target.SetAttribute("id", newId);
        var changed = new List<DitaDocument> { doc };
        var updated = 0;

        ForEachLocalReference(project, (refDoc, node, attribute, reference) =>
        {
            if (!string.Equals(reference.Path, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var topicId = reference.TopicId;
            var elementId = reference.ElementId;
            var changedHere = false;

            if (topicId == oldId)
            {
                topicId = newId;
                changedHere = true;
            }

            if (elementId == oldId)
            {
                elementId = newId;
                changedHere = true;
            }

            if (!changedHere)
            {
                return;
            }

            node.SetAttribute(attribute, BuildHref(FilePart(node.GetAttribute(attribute)!), topicId, elementId));
            updated++;
            if (!changed.Contains(refDoc))
            {
                changed.Add(refDoc);
            }
        });

        return new RefactorResult(updated, changed);
    }

    /// <summary>Физически переносит/переименовывает файл (создаёт папку назначения при
    /// необходимости) и обновляет: (а) исходящие href/conref самого файла — они относительные,
    /// после переноса пересчитываются от нового расположения; (б) входящие ссылки из всех
    /// остальных файлов проекта. Сам перемещённый файл сохраняется по новому пути сразу — без
    /// этого на диске образовался бы висящий пустой путь. Вызывающая сторона должна вызвать
    /// project.Scan() после и сохранить документы из ChangedDocuments, которые сама не открывала.</summary>
    public static RefactorResult MoveFile(DitaProject project, string oldPath, string newPath)
    {
        var oldFull = System.IO.Path.GetFullPath(oldPath);
        var newFull = System.IO.Path.GetFullPath(newPath);
        var doc = project.TryGetDocument(oldFull);
        if (doc is null || !File.Exists(oldFull))
        {
            return new RefactorResult(0, Array.Empty<DitaDocument>());
        }

        if (!string.Equals(oldFull, newFull, StringComparison.OrdinalIgnoreCase) && File.Exists(newFull))
        {
            throw new IOException($"Файл уже существует: {newFull}");
        }

        var updated = 0;

        // Свои собственные (исходящие) ссылки — относительный путь до цели не изменился,
        // но "откуда считать" теперь другая папка.
        foreach (var node in doc.Root.DescendantsAndSelf())
        {
            if (node.Kind != NodeKind.Element)
            {
                continue;
            }

            foreach (var attribute in ReferenceAttributes)
            {
                var value = node.GetAttribute(attribute);
                if (string.IsNullOrWhiteSpace(value) || RefResolver.IsExternal(value!) || IsPeerOrExternalScope(node))
                {
                    continue;
                }

                var reference = RefResolver.Parse(oldFull, value!);
                if (reference.Path is null || string.Equals(reference.Path, oldFull, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // ссылка сама на себя (пустой filePart) — оставляем как есть
                }

                var relative = RefResolver.MakeRelative(newFull, reference.Path);
                node.SetAttribute(attribute, BuildHref(relative, reference.TopicId, reference.ElementId));
                updated++;
            }
        }

        var directory = System.IO.Path.GetDirectoryName(newFull);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        doc.FilePath = newFull;
        doc.Save(newFull);
        File.Delete(oldFull);

        var changed = new List<DitaDocument> { doc };

        // Входящие ссылки из остальных файлов проекта.
        ForEachLocalReference(project, (refDoc, node, attribute, reference) =>
        {
            if (ReferenceEquals(refDoc, doc) || !string.Equals(reference.Path, oldFull, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var relative = RefResolver.MakeRelative(refDoc.FilePath ?? project.RootPath, newFull);
            node.SetAttribute(attribute, BuildHref(relative, reference.TopicId, reference.ElementId));
            updated++;
            if (!changed.Contains(refDoc))
            {
                changed.Add(refDoc);
            }
        });

        return new RefactorResult(updated, changed);
    }

    /// <summary>Переносит содержимое элемента в другой топик (существующий или новый файл),
    /// а на исходном месте оставляет пустой элемент того же имени со ссылкой conref. Возвращает
    /// изменённые документы: исходный и целевой (если целевой — новый файл, он уже сохранён на
    /// диск, как и MoveFile). Не трогает conkeyref — вынесение делается по прямой ссылке.</summary>
    public static RefactorResult ExtractToConref(
        DitaProject project, string sourceFilePath, DitaNode element, string elementId,
        string? targetFilePath, string? newFileRelativePath)
    {
        var sourceFull = System.IO.Path.GetFullPath(sourceFilePath);
        var sourceDoc = project.TryGetDocument(sourceFull);
        if (sourceDoc is null || element.Parent is null)
        {
            return new RefactorResult(0, Array.Empty<DitaDocument>());
        }

        DitaDocument targetDoc;
        string targetFull;
        var changed = new List<DitaDocument> { sourceDoc };

        if (targetFilePath is not null)
        {
            targetFull = System.IO.Path.GetFullPath(targetFilePath);
            var existing = project.TryGetDocument(targetFull);
            if (existing is null || FindBody(existing.Root) is null)
            {
                return new RefactorResult(0, Array.Empty<DitaDocument>());
            }

            targetDoc = existing;
            changed.Add(targetDoc);
        }
        else
        {
            targetFull = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(project.RootPath, newFileRelativePath ?? "shared.dita"));
            if (File.Exists(targetFull))
            {
                throw new IOException($"Файл уже существует: {targetFull}");
            }

            var title = char.ToUpperInvariant(elementId[0]) + elementId[1..].Replace('_', ' ');
            targetDoc = DocumentTemplates.Create("topic", title);
            var placeholder = FindBody(targetDoc.Root)!;
            placeholder.Children.ToList().ForEach(c => placeholder.Remove(c));

            var directory = System.IO.Path.GetDirectoryName(targetFull);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            targetDoc.FilePath = targetFull;
        }

        element.SetAttribute("id", elementId);
        var clone = element.CloneDeep();
        FindBody(targetDoc.Root)!.Add(clone);

        var relative = RefResolver.MakeRelative(sourceFull, targetFull);
        var targetTopicId = targetDoc.Root.GetAttribute("id") ?? string.Empty;
        var stub = DitaNode.Element(element.Name);
        stub.SetAttribute("conref", BuildHref(relative, targetTopicId, elementId));
        element.ReplaceWith(stub);

        if (targetFilePath is null)
        {
            targetDoc.Save(targetFull);
        }

        return new RefactorResult(1, changed);
    }

    private static readonly string[] BodyElementNames =
        { "body", "conbody", "taskbody", "refbody", "troublebody", "glossdef" };

    private static DitaNode? FindBody(DitaNode root) =>
        root.ElementChildren().FirstOrDefault(c => BodyElementNames.Contains(c.Name));

    // ------------------------------------------------------------------ общее

    private static readonly string[] ReferenceAttributes = { "href", "conref" };

    private static bool IsPeerOrExternalScope(DitaNode node) => node.GetAttribute("scope") is "external" or "peer";

    /// <summary>Обходит href/conref во всех файлах проекта и вызывает visit для каждой локальной
    /// (не внешней) ссылки с уже разобранным DitaReference.</summary>
    private static void ForEachLocalReference(
        DitaProject project, Action<DitaDocument, DitaNode, string, DitaReference> visit)
    {
        foreach (var file in project.Files.ToList())
        {
            var doc = project.TryGetDocument(file.FullPath);
            if (doc is null)
            {
                continue;
            }

            foreach (var node in doc.Root.DescendantsAndSelf())
            {
                if (node.Kind != NodeKind.Element)
                {
                    continue;
                }

                foreach (var attribute in ReferenceAttributes)
                {
                    var value = node.GetAttribute(attribute);
                    if (string.IsNullOrWhiteSpace(value) || RefResolver.IsExternal(value!) || IsPeerOrExternalScope(node))
                    {
                        continue;
                    }

                    var reference = RefResolver.Parse(doc.FilePath ?? file.FullPath, value!);
                    if (reference.Path is null)
                    {
                        continue;
                    }

                    visit(doc, node, attribute, reference);
                }
            }
        }
    }

    private static string? FilePart(string href)
    {
        var hash = href.IndexOf('#');
        if (hash < 0)
        {
            return href;
        }

        return hash == 0 ? null : href[..hash];
    }

    private static string BuildHref(string? filePart, string? topicId, string? elementId)
    {
        var sb = new System.Text.StringBuilder(filePart ?? string.Empty);
        if (topicId is not null)
        {
            sb.Append('#').Append(topicId);
            if (elementId is not null)
            {
                sb.Append('/').Append(elementId);
            }
        }

        return sb.ToString();
    }
}
