using System.Text.RegularExpressions;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;

namespace DitaStudio.Presentation.Authoring;

/// <summary>Поле всплывающей правки плашки: ключ, подпись, текущее значение и (для перечислений) допустимые значения.</summary>
public sealed record ChipField(string Key, string Label, string Value, IReadOnlyList<string>? Choices = null);

/// <summary>Итог <see cref="ChipEdit.Apply"/>: изменилась ли модель и перестала ли плашка быть плашкой.</summary>
public readonly record struct ChipEditResult(bool Changed, bool ChipDissolved);

/// <summary>
/// Правка атрибутов плашки «Автора» (изображение, пустая ссылка) без перехода в исходный код. Чистая логика над
/// моделью — окно и отмена остаются за интерфейсом: он получает <see cref="Fields"/>, показывает поля и возвращает
/// введённое в <see cref="Apply"/>. Поле остаётся как есть, если введённое значение недопустимо (ширина не число,
/// пустой адрес у изображения без <c>keyref</c>).
/// </summary>
public static class ChipEdit
{
    public const string Alt = "alt";
    public const string Width = "width";
    public const string Href = "href";
    public const string Scope = "scope";
    public const string Text = "text";

    private static readonly Regex WidthPattern = new(@"^\d+([.,]\d+)?(px|pt|pc|in|cm|mm|em|%)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Есть ли у плашки поля для правки.</summary>
    public static bool CanEdit(DitaNode node) => node.Kind == NodeKind.Element && node.Name is "image" or "xref" or "link";

    /// <summary>Заголовок окна правки.</summary>
    public static string Title(DitaNode node) =>
        node.Name == "image" ? Loc.T("Author_ImageProperties") : Loc.T("Author_LinkProperties");

    /// <summary>Поля правки плашки; пусто, если править нечего.</summary>
    public static IReadOnlyList<ChipField> Fields(DitaNode node)
    {
        if (!CanEdit(node))
        {
            return Array.Empty<ChipField>();
        }

        if (node.Name == "image")
        {
            return new[]
            {
                new ChipField(Alt, Loc.T("Author_ChipAltText"), AltOf(node)?.InnerText ?? node.GetAttribute("alt") ?? string.Empty),
                new ChipField(Width, Loc.T("Author_ChipWidth"), node.GetAttribute("width") ?? string.Empty),
                new ChipField(Href, Loc.T("Author_ChipImageFile"), node.GetAttribute("href") ?? string.Empty)
            };
        }

        return new[]
        {
            new ChipField(Text, Loc.T("Author_ChipLinkText"), node.InnerText.Trim()),
            new ChipField(Href, Loc.T("Author_ChipTarget"), node.GetAttribute("href") ?? string.Empty),
            new ChipField(Scope, Loc.T("Author_ChipScope"), node.GetAttribute("scope") ?? string.Empty, new[] { string.Empty, "local", "peer", "external" })
        };
    }

    /// <summary>Записывает введённые значения в узел. Ключи, которых нет в словаре, не трогаются.</summary>
    public static ChipEditResult Apply(DitaNode node, IReadOnlyDictionary<string, string> values)
    {
        var changed = false;
        var dissolved = false;
        string? Get(string key) => values.TryGetValue(key, out var v) ? v.Trim() : null;

        if (Get(Href) is { } href && href != (node.GetAttribute("href") ?? string.Empty))
        {
            // Адрес можно убрать только там, где остаётся другой способ указать цель.
            if (href.Length > 0)
            {
                node.SetAttribute("href", href);
                changed = true;
            }
            else if (!string.IsNullOrEmpty(node.GetAttribute("keyref")) && node.GetAttribute("href") is not null)
            {
                node.RemoveAttribute("href");
                changed = true;
            }
        }

        if (node.Name == "image")
        {
            changed |= ApplyAlt(node, Get(Alt));
            changed |= ApplyWidth(node, Get(Width));
        }
        else
        {
            if (Get(Scope) is { } scope && scope != (node.GetAttribute("scope") ?? string.Empty) &&
                scope is "" or "local" or "peer" or "external")
            {
                node.SetAttribute("scope", scope.Length == 0 ? null : scope);
                changed = true;
            }

            if (Get(Text) is { } text && text != node.InnerText.Trim() && node.Children.All(c => c.Kind == NodeKind.Text))
            {
                // Пустая ссылка была плашкой; со своим текстом она становится обычным фразовым элементом (и наоборот).
                var wasEmpty = node.Children.Count == 0;
                node.SetText(text);
                dissolved = wasEmpty != (node.Children.Count == 0);
                changed = true;
            }
        }

        return new ChipEditResult(changed, dissolved);
    }

    private static DitaNode? AltOf(DitaNode image) => image.ElementChildren().FirstOrDefault(c => c.Name == "alt");

    private static bool ApplyAlt(DitaNode image, string? alt)
    {
        if (alt is null)
        {
            return false;
        }

        var element = AltOf(image);
        var current = element?.InnerText ?? image.GetAttribute("alt") ?? string.Empty;
        if (alt == current)
        {
            return false;
        }

        // В DITA 1.3 подпись — элемент <alt>; устаревший атрибут alt, если он был, переезжает в элемент.
        image.RemoveAttribute("alt");
        if (alt.Length == 0)
        {
            element?.RemoveSelf();
            return true;
        }

        if (element is null)
        {
            element = DitaNode.Element("alt");
            image.Insert(0, element);
        }

        element.SetText(alt);
        return true;
    }

    private static bool ApplyWidth(DitaNode image, string? width)
    {
        if (width is null || width == (image.GetAttribute("width") ?? string.Empty))
        {
            return false;
        }

        if (width.Length == 0)
        {
            image.RemoveAttribute("width");
            return true;
        }

        if (!WidthPattern.IsMatch(width))
        {
            return false;
        }

        // Число без единиц — пиксели; запятая — десятичный разделитель, как в русском вводе.
        var normalized = width.Replace(',', '.');
        if (char.IsDigit(normalized[^1]))
        {
            normalized += "px";
        }

        image.SetAttribute("width", normalized);
        image.RemoveAttribute("height"); // высота подбирается по пропорциям, как при перетаскивании маркера
        return true;
    }
}
