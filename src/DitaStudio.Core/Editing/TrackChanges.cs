using DitaStudio.Core.Model;

namespace DitaStudio.Core.Editing;

/// <summary>
/// Track changes через штатные атрибуты DITA: status="new"/"deleted" (как в спецификации,
/// univ-atts) плюс свои tcauthor/tcdate. Узел, помеченный на удаление, физически остаётся в
/// дереве до Accept/Reject — контент-модель это не нарушает, элемент как был допустимым в этой
/// позиции, так и остался; удаляется/восстанавливается он только явным Accept/Reject.
/// </summary>
public static class TrackChanges
{
    public const string StatusNew = "new";
    public const string StatusDeleted = "deleted";

    public static bool IsInserted(DitaNode node) => node.GetAttribute("status") == StatusNew;

    public static bool IsDeleted(DitaNode node) => node.GetAttribute("status") == StatusDeleted;

    public static bool IsTracked(DitaNode node) => IsInserted(node) || IsDeleted(node);

    public static void MarkInserted(DitaNode node, string author)
    {
        node.SetAttribute("status", StatusNew);
        node.SetAttribute("tcauthor", author);
        node.SetAttribute("tcdate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }

    public static void MarkDeleted(DitaNode node, string author)
    {
        node.SetAttribute("status", StatusDeleted);
        node.SetAttribute("tcauthor", author);
        node.SetAttribute("tcdate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }

    /// <summary>Принимает правку: вставленный узел остаётся, но теряет пометку; удалённый узел
    /// физически убирается из дерева. Ничего не делает, если узел не отслеживается.</summary>
    public static void Accept(DitaNode node)
    {
        if (IsDeleted(node))
        {
            node.RemoveSelf();
            return;
        }

        if (IsInserted(node))
        {
            ClearMarks(node);
        }
    }

    /// <summary>Отклоняет правку: вставленный узел убирается из дерева; удалённый узел
    /// восстанавливается (теряет пометку). Ничего не делает, если узел не отслеживается.</summary>
    public static void Reject(DitaNode node)
    {
        if (IsInserted(node))
        {
            node.RemoveSelf();
            return;
        }

        if (IsDeleted(node))
        {
            ClearMarks(node);
        }
    }

    /// <summary>Все отслеживаемые узлы поддерева (включая сам узел) — для «Принять все»/
    /// «Отклонить все» и для подсчёта в статусной строке.</summary>
    public static IEnumerable<DitaNode> CollectTracked(DitaNode root) =>
        root.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element && IsTracked(n));

    private static void ClearMarks(DitaNode node)
    {
        node.RemoveAttribute("status");
        node.RemoveAttribute("tcauthor");
        node.RemoveAttribute("tcdate");
    }
}
