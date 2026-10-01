using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Editing;

/// <summary>Куда вставлять блоки: родитель и индекс среди всех его детей.</summary>
public sealed record BlockInsertion(DitaNode Parent, int Index);

/// <summary>
/// Копирование, вырезание и вставка выделенных блоков. В буфер обмена блоки идут обычным текстом XML (по блоку на элемент) — так их
/// можно вставить и в «Исходный код», и в другое окно; обратно разбирается любой фрагмент из элементов. Вставка проверяется по
/// контент-модели: блоки ставятся после выбранного места, а если там они недопустимы — выше по цепочке родителей.
/// </summary>
public static class BlockClipboard
{
    /// <summary>
    /// Текст, который редактор последним положил в буфер обмена как блоки: если в буфере то же самое, вставка в абзац — это вставка
    /// блоков, а не текста с разметкой. Свой XML, скопированный из другого места, как блоки сам не вставляется.
    /// </summary>
    public static string? LastCopiedText { get; set; }

    // Корень-обёртка, чтобы разобрать несколько блоков подряд как один документ.
    private const string Wrapper = "dita-studio-blocks";

    /// <summary>Блоки как текст XML для буфера обмена.</summary>
    public static string Serialize(IEnumerable<DitaNode> blocks) =>
        string.Join("\n", blocks.Where(n => n.Kind != NodeKind.Text || !string.IsNullOrWhiteSpace(n.Value)).Select(XmlSerializer.ToXml));

    /// <summary>
    /// Блоки из текста буфера обмена (копии, не связанные с исходным документом). Null — это не фрагмент блоков: текст не разбирается
    /// как XML, в нём нет элементов, есть неизвестные каталогу элементы или фразовые элементы и текст вперемешку (обычный текст
    /// вставляется как текст, а не как блок).
    /// </summary>
    public static IReadOnlyList<DitaNode>? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('<'))
        {
            return null;
        }

        DitaDocument parsed;
        try
        {
            parsed = DitaDocument.Parse($"<{Wrapper}>{text}</{Wrapper}>");
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return null;
        }

        var blocks = new List<DitaNode>();
        foreach (var child in parsed.Root.Children)
        {
            if (child.Kind == NodeKind.Text)
            {
                if (!string.IsNullOrWhiteSpace(child.Value))
                {
                    return null; // текст вперемешку с элементами — это не набор блоков
                }

                continue;
            }

            if (child.Kind == NodeKind.Element && (DitaCatalog.Default.Get(child.Name) is not { } def || def.Display == DisplayKind.Inline))
            {
                return null; // неизвестный каталогу элемент или фраза (b, ph…) — не блок
            }

            blocks.Add(child.CloneDeep());
        }

        return blocks.Count == 0 ? null : blocks;
    }

    /// <summary>
    /// Идентификаторы вставляемых блоков, которые уже есть в документе, получают новые («id», «id-копия», «id-копия-2»…): иначе документ
    /// содержал бы два одинаковых <c>id</c>. Возвращает число переименованных.
    /// </summary>
    public static int MakeIdsUnique(DitaDocument document, IEnumerable<DitaNode> blocks)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in document.Root.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element))
        {
            if (node.GetAttribute("id") is { Length: > 0 } id)
            {
                used.Add(id);
            }
        }

        var renamed = 0;
        foreach (var node in blocks.SelectMany(b => b.DescendantsAndSelf()).Where(n => n.Kind == NodeKind.Element))
        {
            if (node.GetAttribute("id") is not { Length: > 0 } id)
            {
                continue;
            }

            var unique = id;
            for (var n = 1; used.Contains(unique); n++)
            {
                unique = n == 1 ? id + "-копия" : $"{id}-копия-{n}";
            }

            used.Add(unique);
            if (unique != id)
            {
                node.SetAttribute("id", unique);
                renamed++;
            }
        }

        return renamed;
    }

    /// <summary>
    /// Место вставки блоков после узла <paramref name="after"/>: сначала в его родителя сразу за ним, а если там набор блоков
    /// недопустим по контент-модели — выше (после родителя, затем после деда…). Null — нигде не допустимо.
    /// </summary>
    public static BlockInsertion? FindInsertion(DitaNode after, IReadOnlyList<DitaNode> blocks)
    {
        var anchor = after;
        for (var level = 0; level < 8 && anchor.Parent is { } parent; level++)
        {
            var index = parent.IndexOf(anchor) + 1;
            if (Accepts(parent, index, blocks))
            {
                return new BlockInsertion(parent, index);
            }

            anchor = parent;
        }

        return null;
    }

    /// <summary>Допустимо ли вставить блоки в <paramref name="parent"/> на позицию среди всех детей: последовательность имён его детей остаётся
    /// допустимой по контент-модели родителя.</summary>
    public static bool Accepts(DitaNode parent, int childIndex, IReadOnlyList<DitaNode> blocks)
    {
        if (DitaCatalog.Default.Get(parent.Name) is not { } def)
        {
            return false;
        }

        var names = new List<string>();
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (i == childIndex)
            {
                names.AddRange(blocks.Where(b => b.Kind == NodeKind.Element).Select(b => b.Name));
            }

            if (parent.Children[i].Kind == NodeKind.Element)
            {
                names.Add(parent.Children[i].Name);
            }
        }

        if (childIndex >= parent.Children.Count)
        {
            names.AddRange(blocks.Where(b => b.Kind == NodeKind.Element).Select(b => b.Name));
        }

        return def.Automaton.Validate(names, out _, out _);
    }
}
