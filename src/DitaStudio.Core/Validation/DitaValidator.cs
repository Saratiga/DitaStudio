using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Localization;

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
                Loc.T("Core_UnknownRootElement0CheckThat", document.Root.Name),
                document.Root,
                document.FilePath));
        }
        else if (!rootDef.IsTopicType && !rootDef.IsMapType)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                Loc.T("Core_TheElement0IsNotNormally", document.Root.Name),
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
                Loc.T("Msg_TheElement0IsNotIn", node.Name),
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
                    Loc.T("Core_TheIdentifier0IsAlreadyUsed", id),
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
                    Loc.T("Core_TheAttribute0IsNotDeclared", attr.Name, node.Name),
                    node,
                    file));
                continue;
            }

            if (attrDef.Type == AttrType.Enumeration && attrDef.Values.Count > 0 &&
                !attrDef.Values.Contains(attr.Value, StringComparer.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    Loc.T("Core_InvalidValue01On2", attr.Name, attr.Value, node.Name, string.Join(", ", attrDef.Values)),
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
                    Loc.T("Core_TheElement0LacksTheRequired", node.Name, attrDef.Name),
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
                        Loc.T("Core_TheElement0CannotContainText", node.Name),
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
                Loc.T("Core_TheElement0MustBeEmpty", node.Name),
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
                ? Loc.T("Core_AllowedHere01", string.Join(", ", expected.Take(12)), (expected.Count > 12 ? "…" : string.Empty))
                : string.Empty;
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                Loc.T("Core_TheElement0IsNotAllowed", names[errorIndex], node.Name, hint),
                errorIndex < node.ElementChildren().Count() ? node.ElementChildren().ElementAt(errorIndex) : node,
                file));
        }
        else
        {
            var missing = expected.Count > 0
                ? Loc.T("Core_MissingOneOf01", string.Join(", ", expected.Take(12)), (expected.Count > 12 ? "…" : string.Empty))
                : string.Empty;
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                Loc.T("Core_TheContentOf0IsIncomplete", node.Name, missing),
                node,
                file));
        }
    }

    private void ValidateStyle(DitaDocument document, List<ValidationIssue> issues)
    {
        var root = document.Root;
        var def = _catalog.Get(root.Name);
        if (def is null)
        {
            return;
        }

        if (def.IsTopicType)
        {
            ValidateTopicStyle(root, document, issues);
        }

        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.Kind != NodeKind.Element)
            {
                continue;
            }

            ValidateNodeStyle(node, document, issues);
        }
    }

    private static void ValidateTopicStyle(DitaNode root, DitaDocument document, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(root.GetAttribute("id")))
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                Loc.T("Core_TheTopicHasNoIdAttribute"),
                root,
                document.FilePath));
        }

        var title = root.FirstElement("title") ?? root.FirstElement("glossterm");
        if (title is { Name: "title" } && IsEmptyTitle(title))
        {
            // Пустой <title/> допустим по DTD — так делают топик из одной таблицы: при публикации
            // заголовок не печатается и в оглавление не попадает.
            issues.Add(new ValidationIssue(
                IssueSeverity.Info,
                Loc.T("Core_EmptyTitleTheTopicIsPublished", document.Title),
                title,
                document.FilePath));
        }
        else if (title is null || string.IsNullOrWhiteSpace(title.InnerText))
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                Loc.T("Core_EmptyTopicTitle"),
                title ?? root,
                document.FilePath));
        }

        if (root.FirstElement("shortdesc") is null && root.FirstElement("abstract") is null)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Info,
                Loc.T("Core_NoShortDescriptionShortdescItIs"),
                root,
                document.FilePath));
        }
    }

    /// <summary>Заголовок топика без текста и без подстановки (conref/keyref) — топик без заголовка.</summary>
    public static bool IsEmptyTitle(DitaNode title) =>
        string.IsNullOrWhiteSpace(title.InnerText) &&
        !title.Descendants().Any(n => n.Kind == NodeKind.Element) &&
        string.IsNullOrWhiteSpace(title.GetAttribute("conref")) &&
        string.IsNullOrWhiteSpace(title.GetAttribute("conkeyref"));

    private static void ValidateNodeStyle(DitaNode node, DitaDocument document, List<ValidationIssue> issues)
    {
        // Элемент с conref/conkeyref не пуст: содержимое подставляется из другого топика.
        // Пустой заголовок самого топика — отдельный случай (см. ValidateTopicStyle).
        if (node.Name is "p" or "li" or "cmd" or "title" or "entry" or "stentry" &&
            !(node.Name == "title" && node.Parent is { } owner && DitaCatalog.Default.Get(owner.Name)?.IsTopicType == true) &&
            node.Children.Count == 0 &&
            string.IsNullOrWhiteSpace(node.GetAttribute("conref")) &&
            string.IsNullOrWhiteSpace(node.GetAttribute("conkeyref")))
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                Loc.T("Core_EmptyElement0", node.Name),
                node,
                document.FilePath));
        }

        if (node.Name == "image" && string.IsNullOrWhiteSpace(node.GetAttribute("href")) &&
            string.IsNullOrWhiteSpace(node.GetAttribute("keyref")))
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error,
                Loc.T("Core_TheImageHasNeitherHrefNor"),
                node,
                document.FilePath));
        }

        if (node.Name == "image" && string.IsNullOrWhiteSpace(node.GetAttribute("alt")) &&
            node.FirstElement("alt") is null)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Info,
                Loc.T("Core_TheImageHasNoAlternativeText"),
                node,
                document.FilePath));
        }
    }
}
