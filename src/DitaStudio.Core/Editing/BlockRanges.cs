using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Editing;

/// <summary>Диапазон соседних блоков одного родителя: <see cref="First"/>…<see cref="Last"/> — индексы среди всех детей (с текстом).</summary>
public sealed record BlockSpan(DitaNode Parent, int First, int Last)
{
    /// <summary>Узлы диапазона по порядку.</summary>
    public IReadOnlyList<DitaNode> Nodes => Parent.Children.Skip(First).Take(Last - First + 1).ToList();

    /// <summary>Только элементы и комментарии (пробельный текст между блоками не считается).</summary>
    public IReadOnlyList<DitaNode> Blocks => Nodes.Where(n => n.Kind != NodeKind.Text || !string.IsNullOrWhiteSpace(n.Value)).ToList();
}

/// <summary>
/// Выделение нескольких блоков (протяжкой мыши от одного блока к другому) и «Обернуть в…»: по двум узлам находится диапазон
/// соседей общего родителя, по диапазону — список элементов, в которые его допустимо вложить по контент-модели.
/// </summary>
public static class BlockRanges
{
    // Внутренности таблиц: выделение через границу ячеек — это выделение ячеек, а не блоков.
    private static readonly HashSet<string> TableInternals = new()
    {
        "table", "tgroup", "thead", "tbody", "row", "colspec", "simpletable", "sthead", "strow",
        "properties", "prophead", "property", "choicetable", "chhead", "chrow", "dl", "dlhead", "dlentry"
    };

    /// <summary>
    /// Диапазон блоков от <paramref name="from"/> до <paramref name="to"/> (в любом порядке): соседи ближайшего общего предка, в которых
    /// лежат эти узлы, и всё между ними. Если один узел внутри другого — диапазон из одного внешнего блока. null — это один и тот же
    /// узел (диапазона нет) или узлы в разных ячейках таблицы (это выделение ячеек).
    /// </summary>
    public static BlockSpan? Between(DitaNode from, DitaNode to)
    {
        if (ReferenceEquals(from, to))
        {
            return null;
        }

        var chainFrom = Chain(from);
        var chainTo = Chain(to);
        var common = chainFrom.FirstOrDefault(n => chainTo.Contains(n));
        if (common is null)
        {
            return null;
        }

        // Один узел содержит другой: выделен внешний блок целиком.
        if (ReferenceEquals(common, from) || ReferenceEquals(common, to))
        {
            return common.Parent is { } outerParent ? new BlockSpan(outerParent, outerParent.IndexOf(common), outerParent.IndexOf(common)) : null;
        }

        if (TableInternals.Contains(common.Name))
        {
            return null;
        }

        var a = common.IndexOf(chainFrom[chainFrom.IndexOf(common) - 1]);
        var b = common.IndexOf(chainTo[chainTo.IndexOf(common) - 1]);
        return new BlockSpan(common, Math.Min(a, b), Math.Max(a, b));
    }

    // Узел и его предки до корня.
    private static List<DitaNode> Chain(DitaNode node)
    {
        var chain = new List<DitaNode>();
        for (var current = node; current is not null; current = current.Parent)
        {
            chain.Add(current);
        }

        return chain;
    }

    /// <summary>
    /// Элементы, в которые допустимо вложить диапазон: после вложения и родитель, и сам новый элемент остаются допустимыми по
    /// контент-модели. Блоки и контейнеры (не фразы, не метаданные, не корни топиков и карт), по алфавиту подписи.
    /// </summary>
    public static IReadOnlyList<ElementDef> WrapCandidates(DitaNode parent, int first, int last)
    {
        var catalog = DitaCatalog.Default;
        if (catalog.Get(parent.Name) is not { } parentDef || first < 0 || last >= parent.Children.Count || first > last)
        {
            return Array.Empty<ElementDef>();
        }

        var range = parent.Children.Skip(first).Take(last - first + 1).ToList();
        var rangeNames = range.Where(n => n.Kind == NodeKind.Element).Select(n => n.Name).ToList();
        var hasText = range.Any(n => n.Kind == NodeKind.Text && !string.IsNullOrWhiteSpace(n.Value));
        var before = parent.Children.Take(first).Where(n => n.Kind == NodeKind.Element).Select(n => n.Name).ToList();
        var after = parent.Children.Skip(last + 1).Where(n => n.Kind == NodeKind.Element).Select(n => n.Name).ToList();

        var result = new List<ElementDef>();
        foreach (var def in catalog.Elements.Values)
        {
            if (def.Display is not (DisplayKind.Block or DisplayKind.Container or DisplayKind.Table) || def.IsTopicType || def.IsMapType)
            {
                continue;
            }

            if (hasText && !def.Automaton.AllowsText)
            {
                continue;
            }

            // Родитель: до диапазона, новый элемент, после диапазона.
            var parentNames = before.Concat(new[] { def.Name }).Concat(after).ToList();
            if (!parentDef.Automaton.Validate(parentNames, out _, out _))
            {
                continue;
            }

            // Новый элемент: внутри ровно содержимое диапазона.
            if (!def.Automaton.Validate(rangeNames, out _, out _))
            {
                continue;
            }

            result.Add(def);
        }

        return result.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
    }
}
