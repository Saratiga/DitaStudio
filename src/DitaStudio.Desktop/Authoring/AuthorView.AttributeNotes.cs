using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Authoring;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Authoring;

// Серые пометки у блоков: атрибуты (продукт, @class, любые другие) и метка размещения на странице.
public sealed partial class AuthorView
{
    /// <summary>
    /// Блок «на отдельном листе» (outputclass place-…): над ним — подпись с положением. В самом
    /// «Авторе» блок остаётся в тексте: лист есть только у печатного издания (PDF, DOCX).
    /// </summary>
    private static Control WithPlacementMark(string place, Control view)
    {
        var mark = new TextBlock
        {
            Text = Loc.T("Author_SeparateSheet") + PagePlacement.LabelOf(place)?.ToLowerInvariant(),
            FontSize = 11,
            Margin = new Thickness(4, 4, 0, 0),
            Tag = "placement-mark"
        };
        Themed(mark, TextBlock.ForegroundProperty, "Accent");
        ToolTip.SetTip(mark, Loc.T("Author_InPDFAndDOCXTheBlock") +
                             Loc.T("Author_ToChangeContextMenuFormattingPosition"));
        return new StackPanel { Tag = ViewWrapperTag, Children = { mark, view } };
    }

    /// <summary>Показывать у блоков серые пометки условных атрибутов (<c>product</c>, <c>audience</c>…) и <c>outputclass</c>.</summary>
    public static bool AttributeNotesEnabled { get; set; } = true;

    private static readonly string[] ConditionAttributes = { "product", "audience", "platform", "props", "otherprops", "deliveryTarget" };

    // Классы, которые «Автор» показывает иначе (оформление текста, положение на листе, высота строки) — в пометку не попадают.
    private static readonly string[] HiddenClassPrefixes = { "align-", "size-", "color-", "mark-", "frame-", "row-height-", "place-", "page-break-" };

    // Служебные атрибуты: идентификатор, пространства имён и «технический» class каталога — пользователю не интересны.
    private static readonly string[] ServiceAttributes = { "id", "class", "domains", "xtrf", "xtrc" };

    // Раскладка таблицы (CALS) и простой таблицы: ею управляют команды и рамка таблицы в «Авторе», в пометки она не попадает.
    private static readonly string[] TableLayoutAttributes =
    {
        "cols", "colname", "colnum", "colwidth", "colsep", "rowsep", "frame", "namest", "nameend", "spanname", "morerows",
        "align", "valign", "char", "charoff", "scale", "pgwide", "rowheader", "keycol", "relcolwidth", "orient", "tabstyle", "tocentry", "shortdesc"
    };

    private const int NoteValueLimit = 60;

    /// <summary>
    /// Текст серой пометки у блока с атрибутами: «product: Альфа, Бета · rev: 2 · class: warning-box». Условные атрибуты — первыми,
    /// остальные по алфавиту, классы оформления (<c>outputclass</c>) — в конце. Не показываются служебные атрибуты, раскладка
    /// таблицы и классы, которые «Автор» рисует иначе. null — у элемента нечего показывать.
    /// </summary>
    public static string? AttributeNote(DitaNode node)
    {
        var parts = new List<string>();
        void Add(string name, string value)
        {
            var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return;
            }

            var text = ConditionAttributes.Contains(name) ? string.Join(", ", tokens) : string.Join(" ", tokens);
            parts.Add($"{name}: {(text.Length > NoteValueLimit ? text[..(NoteValueLimit - 1)] + "…" : text)}");
        }

        foreach (var name in ConditionAttributes)
        {
            if (node.GetAttribute(name) is { } value)
            {
                Add(name, value);
            }
        }

        var others = node.Attributes
            .Where(a => !ConditionAttributes.Contains(a.Name) && a.Name != "outputclass" && !ServiceAttributes.Contains(a.Name) &&
                        !TableLayoutAttributes.Contains(a.Name) && !a.Name.StartsWith("xml:", StringComparison.Ordinal) &&
                        !a.Name.StartsWith("xmlns", StringComparison.Ordinal) && !a.Name.StartsWith("ditaarch:", StringComparison.Ordinal))
            .OrderBy(a => a.Name, StringComparer.Ordinal);
        foreach (var attribute in others)
        {
            Add(attribute.Name, attribute.Value);
        }

        var classes = (node.GetAttribute("outputclass") ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !HiddenClassPrefixes.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        if (classes.Count > 0)
        {
            Add("class", string.Join(" ", classes));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// Пересобирает только обёртки блока (метка размещения, серая пометка атрибутов) после того, как атрибуты узла изменились
    /// без перестройки блока (панель «Атрибуты»): блок и его редакторы остаются на месте, курсор не сбивается.
    /// </summary>
    public void RefreshMarks(DitaNode node)
    {
        if (!_views.TryGetValue(node, out var outer) || outer.Parent is null)
        {
            return;
        }

        var note = AttributeNotesEnabled ? AttributeNote(node) : null;
        var place = PagePlacement.Of(node);
        if (ReferenceEquals(outer, ViewFor(node)) && note is null && place is null)
        {
            return;
        }

        if (!Detach(outer))
        {
            return;
        }

        var core = outer;
        while (core is StackPanel { Tag: ViewWrapperTag } wrapper && wrapper.Children.Count > 0)
        {
            var inner = wrapper.Children[^1];
            wrapper.Children.Remove(inner);
            core = inner;
        }

        var view = core;
        if (place is not null)
        {
            view = WithPlacementMark(place, view);
        }

        if (note is not null)
        {
            view = WithAttributeNote(note, view, node.GetAttribute("status"));
        }

        _viewNodes.Remove(outer);
        _views[node] = view;
        _viewNodes[view] = node;
        _replaceSlot!(view);
        _replaceSlot = null;
    }

    /// <summary>Цвет пометки: правка по <c>status</c> — new зелёная, changed оранжевая, deleted красная; остальные пометки серые.</summary>
    public static string NoteBrushKey(string? status) => status switch
    {
        "new" => "Success",
        "changed" => "Warning",
        "deleted" => "Danger",
        _ => "EditorTag"
    };

    private static Control WithAttributeNote(string text, Control view, string? status = null)
    {
        var note = new TextBlock { Text = text, FontSize = 10.5, Margin = new Thickness(4, 2, 0, 0), Tag = "attribute-note", TextWrapping = TextWrapping.Wrap };
        Themed(note, TextBlock.ForegroundProperty, NoteBrushKey(status));
        if (status is "new" or "changed" or "deleted")
        {
            note.FontWeight = FontWeight.SemiBold;
        }
        ToolTip.SetTip(note, Loc.T("Author_ElementAttributesInAConditionalBuild") +
                             Loc.T("Author_EditOnTheAttributesPanelTo"));
        return new StackPanel { Tag = ViewWrapperTag, Children = { note, view } };
    }
}
