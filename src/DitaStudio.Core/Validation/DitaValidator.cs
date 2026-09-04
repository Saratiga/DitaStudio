using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Validation;

/// <summary>
/// Проверка документа по каталогу DITA: структура, атрибуты, уникальность идентификаторов
/// и типовые редакторские ошибки (пустой заголовок, отсутствующий shortdesc и т.п.).
/// </summary>
public sealed class DitaValidator
{
    private readonly DitaCatalog _catalog;

    public DitaValidator(DitaCatalog? catalog = null)
    {
        _catalog = catalog ?? DitaCatalog.Default;
    }

    /// <summary>Проверять «мягкие» рекомендации по стилю (shortdesc, id и т.п.).</summary>
    public bool CheckStyleRules { get; set; } = true;

    public IReadOnlyList<ValidationIssue> Validate(DitaDocument document)
    {
        var issues = new List<ValidationIssue>();
        var seenIds = new Dictionary<string, DitaNode>(StringComparer.Ordinal);

        var rootDef = _catalog.Get(document.Root.Name);
        if (rootDef is null)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                $"Неизвестный корневой элемент <{document.Root.Name}>. Проверьте, что документ соответствует DITA 1.3.",
                document.Root,
                document.FilePath));
        }
        else if (!rootDef.IsTopicType && !rootDef.IsMapType)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                $"Элемент <{document.Root.Name}> обычно не используется как корень документа.",
                document.Root,
                document.FilePath));
        }

        ValidateNode(document.Root, issues, seenIds, document.FilePath);

        if (CheckStyleRules)
        {
            ValidateStyle(document, issues);
        }

        return issues;
    }

    private void ValidateNode(DitaNode node, List<ValidationIssue> issues, Dictionary<string, DitaNode> seenIds, string? file)
    {
        if (node.Kind != NodeKind.Element)
        {
            return;
        }

        var def = _catalog.Get(node.Name);
        if (def is null)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                $"Элемент <{node.Name}> отсутствует в словаре DITA 1.3.",
                node,
                file));
            return;
        }

        // Уникальность @id в пределах документа.
        var id = node.GetAttribute("id");
        if (!string.IsNullOrEmpty(id))
        {
            if (seenIds.TryGetValue(id!, out _))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    $"Идентификатор \"{id}\" уже используется в этом документе.",
                    node,
                    file));
            }
            else
            {
                seenIds[id!] = node;
            }
        }

        ValidateAttributes(node, def, issues, file);
        ValidateContent(node, def, issues, file);

        foreach (var child in node.Children)
        {
            ValidateNode(child, issues, seenIds, file);
        }
    }

    private void ValidateAttributes(DitaNode node, ElementDef def, List<ValidationIssue> issues, string? file)
    {
        foreach (var attr in node.Attributes)
        {
            if (attr.Name.StartsWith("xmlns", StringComparison.Ordinal) ||
                attr.Name.StartsWith("ditaarch:", StringComparison.Ordinal))
            {
                continue;
            }

            if (!def.Attributes.TryGetValue(attr.Name, out var attrDef))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    $"Атрибут @{attr.Name} не объявлен для элемента <{node.Name}>.",
                    node,
                    file));
                continue;
            }

            if (attrDef.Type == AttrType.Enumeration && attrDef.Values.Count > 0 &&
                !attrDef.Values.Contains(attr.Value, StringComparer.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    $"Недопустимое значение @{attr.Name}=\"{attr.Value}\" у <{node.Name}>. Допустимо: {string.Join(", ", attrDef.Values)}.",
                    node,
                    file));
            }
        }

        foreach (var attrDef in def.Attributes.Values)
        {
            if (attrDef.Required && !node.HasAttribute(attrDef.Name))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    $"У элемента <{node.Name}> отсутствует обязательный атрибут @{attrDef.Name}.",
                    node,
                    file));
            }
        }
    }

    private void ValidateContent(DitaNode node, ElementDef def, List<ValidationIssue> issues, string? file)
    {
        if (def.Model is ContentModel.Any)
        {
            return;
        }

        // Элемент с conref получает содержимое извне — модель не проверяем.
        if (node.HasAttribute("conref") || node.HasAttribute("conkeyref"))
        {
            return;
        }

        if (!def.IsMixed)
        {
            foreach (var child in node.Children)
            {
                if (child.Kind == NodeKind.Text && !string.IsNullOrWhiteSpace(child.Value))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        $"Элемент <{node.Name}> не может содержать текст напрямую — перенесите его в дочерний элемент.",
                        node,
                        file));
                    break;
                }
            }
        }

        var names = DitaCatalog.ChildNames(node);
        if (def.IsEmpty && names.Count > 0)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                $"Элемент <{node.Name}> должен быть пустым.",
                node,
                file));
            return;
        }

        if (def.Automaton.Validate(names, out var errorIndex, out var expected))
        {
            return;
        }

        if (errorIndex >= 0 && errorIndex < names.Count)
        {
            var hint = expected.Count > 0
                ? $" Здесь допустимы: {string.Join(", ", expected.Take(12))}{(expected.Count > 12 ? "…" : string.Empty)}."
                : string.Empty;
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                $"Элемент <{names[errorIndex]}> недопустим внутри <{node.Name}> в этой позиции.{hint}",
                errorIndex < node.ElementChildren().Count() ? node.ElementChildren().ElementAt(errorIndex) : node,
                file));
        }
        else
        {
            var missing = expected.Count > 0
                ? $" Не хватает одного из: {string.Join(", ", expected.Take(12))}{(expected.Count > 12 ? "…" : string.Empty)}."
                : string.Empty;
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                $"Содержимое <{node.Name}> неполное.{missing}",
                node,
                file));
        }
    }

    private static void ValidateStyle(DitaDocument document, List<ValidationIssue> issues)
    {
        var root = document.Root;
        var def = DitaCatalog.Default.Get(root.Name);
        if (def is null)
        {
            return;
        }

        if (def.IsTopicType)
        {
            if (string.IsNullOrWhiteSpace(root.GetAttribute("id")))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    "У топика нет атрибута @id — на него нельзя сослаться из карты и из других топиков.",
                    root,
                    document.FilePath));
            }

            var title = root.FirstElement("title") ?? root.FirstElement("glossterm");
            if (title is null || string.IsNullOrWhiteSpace(title.InnerText))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "Пустой заголовок топика.",
                    title ?? root,
                    document.FilePath));
            }

            if (root.FirstElement("shortdesc") is null && root.FirstElement("abstract") is null)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Info,
                    "Нет краткого описания (shortdesc) — оно используется в подсказках ссылок и в поиске.",
                    root,
                    document.FilePath));
            }
        }

        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.Kind != NodeKind.Element)
            {
                continue;
            }

            if (node.Name is "p" or "li" or "cmd" or "title" or "entry" or "stentry" &&
                node.Children.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    $"Пустой элемент <{node.Name}>.",
                    node,
                    document.FilePath));
            }

            if (node.Name == "image" && string.IsNullOrWhiteSpace(node.GetAttribute("href")) &&
                string.IsNullOrWhiteSpace(node.GetAttribute("keyref")))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "У изображения не задан ни @href, ни @keyref.",
                    node,
                    document.FilePath));
            }

            if (node.Name == "image" && string.IsNullOrWhiteSpace(node.GetAttribute("alt")) &&
                node.FirstElement("alt") is null)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Info,
                    "У изображения нет альтернативного текста.",
                    node,
                    document.FilePath));
            }
        }
    }
}
