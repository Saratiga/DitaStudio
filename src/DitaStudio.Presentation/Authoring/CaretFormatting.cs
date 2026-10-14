using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Presentation.Authoring;

/// <summary>
/// Оформление текста у курсора (или у всего выделения) — для кнопок панели: какие фразовые элементы применены (<c>b</c>, <c>i</c>, <c>u</c>,
/// <c>codeph</c>…), размер, цвет текста и маркер (классы ph или блока), выравнивание блока.
/// </summary>
public sealed record CaretFormatting(IReadOnlySet<string> Elements, double? SizePt, string? ColorHex, string? MarkHex, string? Align)
{
    public static readonly CaretFormatting None = new(new HashSet<string>(), null, null, null, null);

    public bool Has(string element) => Elements.Contains(element);

    /// <summary>Оформление по цепочке фразовых элементов <paramref name="chain"/> (от внешнего к внутреннему) внутри блока <paramref name="block"/>.</summary>
    public static CaretFormatting Of(IReadOnlyList<DitaNode> chain, DitaNode block)
    {
        // Ближайший к тексту класс главнее: внутренний ph, затем внешний, затем сам блок и его предки.
        var holders = chain.Reverse().Concat(Ancestors(block)).ToList();
        string? First(string prefix) => holders.Select(n => TextFormatting.Token(n, prefix)).FirstOrDefault(t => t is not null);

        var size = First(TextFormatting.SizePrefix) is { } sizeToken ? TextFormatting.ParseSizeToken(sizeToken) : null;
        var color = First(TextFormatting.ColorPrefix) is { } colorToken
            ? TextFormatting.Colors.FirstOrDefault(c => c.Token == colorToken).Hex ?? TextFormatting.ParseMarkToken("mark-" + colorToken[TextFormatting.ColorPrefix.Length..])
            : null;
        var mark = First(TextFormatting.MarkPrefix) is { } markToken
            ? TextFormatting.Marks.FirstOrDefault(m => m.Token == markToken).Hex ?? TextFormatting.ParseMarkToken(markToken)
            : null;
        var align = Ancestors(block).Select(TextFormatting.AlignmentOf).FirstOrDefault(a => a is not null);
        return new CaretFormatting(chain.Select(n => n.Name).ToHashSet(StringComparer.Ordinal), size, color, mark, align);
    }

    private static IEnumerable<DitaNode> Ancestors(DitaNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }
}
