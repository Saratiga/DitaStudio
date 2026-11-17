using System.Globalization;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Editing;

/// <summary>
/// Track changes через штатные атрибуты DITA: status="new"/"changed"/"deleted" (как в спецификации,
/// univ-atts) плюс свои tcauthor/tcdate. Узел, помеченный на удаление, физически остаётся в
/// дереве до Accept/Reject — контент-модель это не нарушает, элемент как был допустимым в этой
/// позиции, так и остался; удаляется/восстанавливается он только явным Accept/Reject.
/// Пометить можно и блок целиком, и слова внутри блока — тогда они оборачиваются в <c>ph</c> с теми же атрибутами.
/// </summary>
public static class TrackChanges
{
    public const string StatusNew = "new";
    public const string StatusChanged = "changed";
    public const string StatusDeleted = "deleted";

    /// <summary>Формат tcdate: дата и время правки (местное время), сортируется как строка.</summary>
    public const string DateFormat = "yyyy-MM-dd HH:mm";

    public static bool IsInserted(DitaNode node) => node.GetAttribute("status") == StatusNew;

    public static bool IsChanged(DitaNode node) => node.GetAttribute("status") == StatusChanged;

    public static bool IsDeleted(DitaNode node) => node.GetAttribute("status") == StatusDeleted;

    public static bool IsTracked(DitaNode node) => IsInserted(node) || IsChanged(node) || IsDeleted(node);

    public static void MarkInserted(DitaNode node, string author) => Mark(node, StatusNew, author);

    public static void MarkChanged(DitaNode node, string author) => Mark(node, StatusChanged, author);

    public static void MarkDeleted(DitaNode node, string author) => Mark(node, StatusDeleted, author);

    /// <summary>Ставит пометку правки: статус, автор и дата со временем.</summary>
    public static void Mark(DitaNode node, string status, string author, DateTime? when = null)
    {
        node.SetAttribute("status", status);
        node.SetAttribute("tcauthor", author);
        node.SetAttribute("tcdate", (when ?? DateTime.Now).ToString(DateFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Принимает правку: вставленный или изменённый узел остаётся, но теряет пометку; удалённый узел
    /// физически убирается из дерева. Ничего не делает, если узел не отслеживается.
    /// </summary>
    public static void Accept(DitaNode node)
    {
        if (IsDeleted(node))
        {
            node.RemoveSelf();
            return;
        }

        if (IsInserted(node) || IsChanged(node))
        {
            ClearMarks(node);
        }
    }

    /// <summary>
    /// Отклоняет правку: вставленный узел убирается из дерева; удалённый восстанавливается (теряет пометку). У изменённого прежнего
    /// текста нет — пометка просто снимается. Ничего не делает, если узел не отслеживается.
    /// </summary>
    public static void Reject(DitaNode node)
    {
        if (IsInserted(node))
        {
            node.RemoveSelf();
            return;
        }

        if (IsDeleted(node) || IsChanged(node))
        {
            ClearMarks(node);
        }
    }

    /// <summary>Все отслеживаемые узлы поддерева (включая сам узел) — для «Принять все»/
    /// «Отклонить все» и для подсчёта в статусной строке.</summary>
    public static IEnumerable<DitaNode> CollectTracked(DitaNode root) =>
        root.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element && IsTracked(n));

    /// <summary>Дата правки для показа: «17.11.2026 14:05»; нераспознанное значение — как есть; null — даты нет.</summary>
    public static string? DisplayDate(DitaNode node)
    {
        var raw = node.GetAttribute("tcdate");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string[] formats = { DateFormat, "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToString(raw.Trim().Length > 10 ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        return raw.Trim();
    }

    /// <summary>Название правки: «Вставлено», «Изменено», «Удалено»; null — узел не отслеживается.</summary>
    public static string? StatusLabel(DitaNode node) => node.GetAttribute("status") switch
    {
        StatusNew => Loc.T("Track_Inserted"),
        StatusChanged => Loc.T("Track_Changed"),
        StatusDeleted => Loc.T("Track_Deleted"),
        _ => null
    };

    /// <summary>Описание правки для пометки и подсказки: «Изменено · Иванов · 17.11.2026 14:05»; null — узел не отслеживается.</summary>
    public static string? Describe(DitaNode node)
    {
        if (StatusLabel(node) is not { } label)
        {
            return null;
        }

        var parts = new List<string> { label };
        if (node.GetAttribute("tcauthor") is { Length: > 0 } author)
        {
            parts.Add(author);
        }

        if (DisplayDate(node) is { } date)
        {
            parts.Add(date);
        }

        return string.Join(" · ", parts);
    }

    private static void ClearMarks(DitaNode node)
    {
        node.RemoveAttribute("status");
        node.RemoveAttribute("tcauthor");
        node.RemoveAttribute("tcdate");

        // Пометка слов внутри блока — обёртка ph без своих атрибутов: после снятия пометки она не нужна, текст остаётся на месте.
        if (node.Name == "ph" && node.Attributes.Count == 0 && node.Parent is { } parent)
        {
            var first = parent.IndexOf(node);
            var index = first;
            foreach (var child in node.Children.ToList())
            {
                parent.Insert(index++, child);
            }

            parent.Remove(node);

            // Пометка через границу оформления разрезала его (<b>жир</b><b>ный</b>) — после снятия обёртки половины склеиваются.
            MergeAt(parent, index - 1);
            MergeAt(parent, first - 1);
        }
    }

    /// <summary>Склеивает два соседних одинаковых фразовых элемента (то же имя и атрибуты) на позиции <paramref name="index"/> и следующей.</summary>
    private static void MergeAt(DitaNode parent, int index)
    {
        if (index < 0 || index + 1 >= parent.Children.Count)
        {
            return;
        }

        var left = parent.Children[index];
        var right = parent.Children[index + 1];
        if (left.Kind != NodeKind.Element || right.Kind != NodeKind.Element || left.Name != right.Name ||
            Schema.DitaCatalog.Default.Get(left.Name) is not { IsInline: true } || left.Name is "xref" or "fn" or "image" ||
            left.Attributes.Count != right.Attributes.Count || left.Attributes.Any(a => right.GetAttribute(a.Name) != a.Value))
        {
            return;
        }

        foreach (var child in right.Children.ToList())
        {
            left.Add(child);
        }

        parent.Remove(right);
    }
}
