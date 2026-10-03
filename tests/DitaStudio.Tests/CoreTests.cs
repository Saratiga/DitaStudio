using Xunit;
using Xunit.Abstractions;

// Разделы делят статическое состояние CoreChecks (счётчики проверок) и общие объекты ядра
// (DitaCatalog.Default) — выполняем их последовательно, как прежний консольный прогон.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DitaStudio.Tests;

/// <summary>
/// Тесты xUnit поверх разделов <see cref="CoreChecks"/>: один тест — один раздел. Раздел
/// выполняет все свои проверки до конца, и упавший тест перечисляет все непрошедшие проверки,
/// а не только первую.
/// </summary>
public sealed class CoreTests
{
    private readonly ITestOutputHelper _output;

    public CoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact] public void CatalogTests() => Run(CoreChecks.CatalogTests);
    [Fact] public void DitaCatalogMiscTests() => Run(CoreChecks.DitaCatalogMiscTests);
    [Fact] public void ContentModelTests() => Run(CoreChecks.ContentModelTests);
    [Fact] public void ContentModelMiscTests() => Run(CoreChecks.ContentModelMiscTests);
    [Fact] public void ModelAutomatonPropertyTests() => Run(CoreChecks.ModelAutomatonPropertyTests);
    [Fact] public void RoundTripTests() => Run(CoreChecks.RoundTripTests);
    [Fact] public void XmlSerializerMiscTests() => Run(CoreChecks.XmlSerializerMiscTests);
    [Fact] public void ValidationTests() => Run(CoreChecks.ValidationTests);
    [Fact] public void DitaValidatorMiscTests() => Run(CoreChecks.DitaValidatorMiscTests);
    [Fact] public void ValidationIssueTests() => Run(CoreChecks.ValidationIssueTests);
    [Fact] public void TemplateTests() => Run(CoreChecks.TemplateTests);
    [Fact] public void EditingTests() => Run(CoreChecks.EditingTests);
    [Fact] public void EditCommandsExtraTests() => Run(CoreChecks.EditCommandsExtraTests);
    [Fact] public void UndoStackExtraTests() => Run(CoreChecks.UndoStackExtraTests);
    [Fact] public void ProjectTests() => Run(CoreChecks.ProjectTests);
    [Fact] public void ProjectSettingsTests() => Run(CoreChecks.ProjectSettingsTests);
    [Fact] public void LocalizationTests() => Run(CoreChecks.LocalizationTests);
    [Fact] public void ConditionalMapTests() => Run(CoreChecks.ConditionalMapTests);
    [Fact] public void DitavalActionsTests() => Run(CoreChecks.DitavalActionsTests);
    [Fact] public void PublicationLanguageTests() => Run(CoreChecks.PublicationLanguageTests);
    [Fact] public void XliffBatchTests() => Run(CoreChecks.XliffBatchTests);
    [Fact] public void SubjectSchemeTests() => Run(CoreChecks.SubjectSchemeTests);
    [Fact] public void IndexTermTests() => Run(CoreChecks.IndexTermTests);
    [Fact] public void HtmlPublisherMiscTests() => Run(CoreChecks.HtmlPublisherMiscTests);
    [Fact] public void KeyScopeTests() => Run(CoreChecks.KeyScopeTests);
    [Fact] public void KeyDefinitionMiscTests() => Run(CoreChecks.KeyDefinitionMiscTests);
    [Fact] public void MultiProjectWorkspaceTests() => Run(CoreChecks.MultiProjectWorkspaceTests);
    [Fact] public void ValidationAndAnchorFixTests() => Run(CoreChecks.ValidationAndAnchorFixTests);
    [Fact] public void RelTableTests() => Run(CoreChecks.RelTableTests);
    [Fact] public void MapTreeMiscTests() => Run(CoreChecks.MapTreeMiscTests);
    [Fact] public void RevChangeTests() => Run(CoreChecks.RevChangeTests);
    [Fact] public void TrackChangesTests() => Run(CoreChecks.TrackChangesTests);
    [Fact] public void XliffTests() => Run(CoreChecks.XliffTests);
    [Fact] public void XliffConverterPropertyTests() => Run(CoreChecks.XliffConverterPropertyTests);
    [Fact] public void DtdAttributeListParserTests() => Run(CoreChecks.DtdAttributeListParserTests);
    [Fact] public void DtdCatalogLoaderTests() => Run(CoreChecks.DtdCatalogLoaderTests);
    [Fact] public void DtdReaderMiscTests() => Run(CoreChecks.DtdReaderMiscTests);
    [Fact] public void DtdCatalogLoaderPropertyTests() => Run(CoreChecks.DtdCatalogLoaderPropertyTests);
    [Fact] public void ProjectCatalogTests() => Run(CoreChecks.ProjectCatalogTests);
    [Fact] public void SampleProjectsTests() => Run(CoreChecks.SampleProjectsTests);
    [Fact] public void PluginLoaderTests() => Run(CoreChecks.PluginLoaderTests);
    [Fact] public void DitaProjectValidateAllWithPluginsTests() => Run(CoreChecks.DitaProjectValidateAllWithPluginsTests);
    [Fact] public void DitavalFlagTests() => Run(CoreChecks.DitavalFlagTests);
    [Fact] public void DitavalPropertyTests() => Run(CoreChecks.DitavalPropertyTests);
    [Fact] public void RefResolverTests() => Run(CoreChecks.RefResolverTests);
    [Fact] public void RefactorTests() => Run(CoreChecks.RefactorTests);
    [Fact] public void ExtractToConrefTests() => Run(CoreChecks.ExtractToConrefTests);
    [Fact] public void PdfExporterTests() => Run(CoreChecks.PdfExporterTests);
    [Fact] public void DiffTests() => Run(CoreChecks.DiffTests);
    [Fact] public void GitHistoryTests() => Run(CoreChecks.GitHistoryTests);
    [Fact] public void SvnHistoryTests() => Run(CoreChecks.SvnHistoryTests);
    [Fact] public void DocxTests() => Run(CoreChecks.DocxTests);
    [Fact] public void DocxRendererMiscTests() => Run(CoreChecks.DocxRendererMiscTests);
    [Fact] public void AdvancedRenderingTests() => Run(CoreChecks.AdvancedRenderingTests);
    [Fact] public void ImageSizeFormatsTests() => Run(CoreChecks.ImageSizeFormatsTests);
    [Fact] public void HtmlRendererMiscTests() => Run(CoreChecks.HtmlRendererMiscTests);
    [Fact] public void ListAndStepsDispatchTests() => Run(CoreChecks.ListAndStepsDispatchTests);
    [Fact] public void FileSafetyTests() => Run(CoreChecks.FileSafetyTests);
    [Fact] public void XmlSafetyTests() => Run(CoreChecks.XmlSafetyTests);
    [Fact] public void DocxStylingTests() => Run(CoreChecks.DocxStylingTests);
    [Fact] public void DivContentTests() => Run(CoreChecks.DivContentTests);
    [Fact] public void CaptionTests() => Run(CoreChecks.CaptionTests);
    [Fact] public void FigureCaptionFormatTests() => Run(CoreChecks.FigureCaptionFormatTests);
    [Fact]
    public void EmptyTitleCaptionTests() => Run(CoreChecks.EmptyTitleCaptionTests);
    [Fact] public void PageMarginBoxTests() => Run(CoreChecks.PageMarginBoxTests);
    [Fact] public void CssSelectorTests() => Run(CoreChecks.CssSelectorTests);
    [Fact] public void TopicPageBreakTests() => Run(CoreChecks.TopicPageBreakTests);
    [Fact] public void BrokenTopicTests() => Run(CoreChecks.BrokenTopicTests);
    [Fact] public void CustomFontSizeTests() => Run(CoreChecks.CustomFontSizeTests);
    [Fact] public void TableBordersTests() => Run(CoreChecks.TableBordersTests);
    [Fact] public void TitleImageTests() => Run(CoreChecks.TitleImageTests);
    [Fact] public void ProductListTests() => Run(CoreChecks.ProductListTests);
    [Fact] public void PagedPreviewTests() => Run(CoreChecks.PagedPreviewTests);
    [Fact] public void MarkerTests() => Run(CoreChecks.MarkerTests);
    [Fact] public void BlockRangeTests() => Run(CoreChecks.BlockRangeTests);
    [Fact] public void BlockClipboardTests() => Run(CoreChecks.BlockClipboardTests);
    [Fact] public void TableRangeTests() => Run(CoreChecks.TableRangeTests);
    [Fact] public void TableEdgesTests() => Run(CoreChecks.TableEdgesTests);
    [Fact]
    public void CellParagraphsDocxTests() => Run(CoreChecks.CellParagraphsDocxTests);
    [Fact] public void CssCascadeDocxTests() => Run(CoreChecks.CssCascadeDocxTests);
    [Fact] public void HeaderFooterCssDocxTests() => Run(CoreChecks.HeaderFooterCssDocxTests);
    [Fact] public void MapMoveTests() => Run(CoreChecks.MapMoveTests);
    [Fact]
    public void FormatClassOnAnyElementTests() => Run(CoreChecks.FormatClassOnAnyElementTests);
    [Fact] public void UntitledTopicTests() => Run(CoreChecks.UntitledTopicTests);
    [Fact] public void FileReferencesTests() => Run(CoreChecks.FileReferencesTests);
    [Fact] public void TableEditingTests() => Run(CoreChecks.TableEditingTests);
    [Fact] public void PageSetupTests() => Run(CoreChecks.PageSetupTests);
    [Fact] public void TocTests() => Run(CoreChecks.TocTests);
    [Fact] public void UnnumberedTitleTests() => Run(CoreChecks.UnnumberedTitleTests);
    [Fact] public void ExcludeFromPublicationTests() => Run(CoreChecks.ExcludeFromPublicationTests);
    [Fact] public void HeaderImageTests() => Run(CoreChecks.HeaderImageTests);
    [Fact] public void TextAlignmentTests() => Run(CoreChecks.TextAlignmentTests);
    [Fact] public void TextSizeTests() => Run(CoreChecks.TextSizeTests);
    [Fact] public void TextColorTests() => Run(CoreChecks.TextColorTests);
    [Fact] public void TableResizeTests() => Run(CoreChecks.TableResizeTests);
    [Fact] public void NumberedParagraphTests() => Run(CoreChecks.NumberedParagraphTests);
    [Fact] public void PagePlacementTests() => Run(CoreChecks.PagePlacementTests);

    private void Run(Action section)
    {
        var result = CoreChecks.Run(section);
        _output.WriteLine($"{result.Title}: пройдено проверок {result.Passed}");
        foreach (var note in result.Notes)
        {
            _output.WriteLine("  " + note);
        }
        if (result.Failures.Count > 0)
        {
            Assert.Fail(
                $"{result.Title}: не пройдено {result.Failures.Count} из {result.Passed + result.Failures.Count}\n" +
                string.Join("\n", result.Failures.Select(f => "  ✗ " + f)));
        }
    }
}
