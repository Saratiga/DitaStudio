namespace DitaStudio.Core.Publishing;

/// <summary>
/// Общая классификация блочных элементов DITA, которые HtmlRenderer и DocxRenderer
/// обрабатывают одинаково (один и тот же вызов независимо от конкретного имени).
/// Единственный источник этих групп — раньше список имён дублировался в обоих
/// рендерерах и требовал синхронной правки при добавлении элемента.
/// </summary>
public enum BlockElementCategory
{
    None,
    ContainerDiv,
    ContainerSection,
    Preformatted,
    Figure,
    SimpleTable,
    Skip,
    ListUnordered,
    StepsGroup,
}

public static class BlockElementCategoryMap
{
    private static readonly Dictionary<string, BlockElementCategory> Map = new()
    {
        ["div"] = BlockElementCategory.ContainerDiv,
        ["bodydiv"] = BlockElementCategory.ContainerDiv,
        ["conbodydiv"] = BlockElementCategory.ContainerDiv,
        ["refbodydiv"] = BlockElementCategory.ContainerDiv,
        ["sectiondiv"] = BlockElementCategory.ContainerDiv,
        ["itemgroup"] = BlockElementCategory.ContainerDiv,
        ["equation-block"] = BlockElementCategory.ContainerDiv,

        ["section"] = BlockElementCategory.ContainerSection,
        ["example"] = BlockElementCategory.ContainerSection,
        ["refsyn"] = BlockElementCategory.ContainerSection,
        ["prereq"] = BlockElementCategory.ContainerSection,
        ["context"] = BlockElementCategory.ContainerSection,
        ["result"] = BlockElementCategory.ContainerSection,
        ["postreq"] = BlockElementCategory.ContainerSection,
        ["tasktroubleshooting"] = BlockElementCategory.ContainerSection,
        ["condition"] = BlockElementCategory.ContainerSection,
        ["cause"] = BlockElementCategory.ContainerSection,
        ["remedy"] = BlockElementCategory.ContainerSection,
        ["troubleSolution"] = BlockElementCategory.ContainerSection,
        ["steps-informal"] = BlockElementCategory.ContainerSection,
        ["lcIntro"] = BlockElementCategory.ContainerSection,
        ["lcObjectives"] = BlockElementCategory.ContainerSection,
        ["lcSummary"] = BlockElementCategory.ContainerSection,
        ["lcReview"] = BlockElementCategory.ContainerSection,
        ["lcNextSteps"] = BlockElementCategory.ContainerSection,
        ["lcPrereqs"] = BlockElementCategory.ContainerSection,
        ["lcResources"] = BlockElementCategory.ContainerSection,
        ["lcAudience"] = BlockElementCategory.ContainerSection,
        ["lcDuration"] = BlockElementCategory.ContainerSection,

        ["pre"] = BlockElementCategory.Preformatted,
        ["codeblock"] = BlockElementCategory.Preformatted,
        ["screen"] = BlockElementCategory.Preformatted,
        ["msgblock"] = BlockElementCategory.Preformatted,
        ["lines"] = BlockElementCategory.Preformatted,

        ["fig"] = BlockElementCategory.Figure,
        ["equation-figure"] = BlockElementCategory.Figure,
        ["imagemap"] = BlockElementCategory.Figure,

        ["simpletable"] = BlockElementCategory.SimpleTable,
        ["properties"] = BlockElementCategory.SimpleTable,
        ["choicetable"] = BlockElementCategory.SimpleTable,

        ["required-cleanup"] = BlockElementCategory.Skip,
        ["data"] = BlockElementCategory.Skip,
        ["data-about"] = BlockElementCategory.Skip,
        ["resourceid"] = BlockElementCategory.Skip,
        ["titlealts"] = BlockElementCategory.Skip,
        ["prolog"] = BlockElementCategory.Skip,

        ["ul"] = BlockElementCategory.ListUnordered,
        ["sl"] = BlockElementCategory.ListUnordered,
        ["choices"] = BlockElementCategory.ListUnordered,

        ["steps"] = BlockElementCategory.StepsGroup,
        ["steps-unordered"] = BlockElementCategory.StepsGroup,
    };

    public static BlockElementCategory Of(string name) =>
        Map.TryGetValue(name, out var category) ? category : BlockElementCategory.None;
}
