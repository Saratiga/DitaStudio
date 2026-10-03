using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// Предметный указатель: indexterm превращается в скрытое поле XE, а сам указатель — поле INDEX в конце документа.
public sealed partial class DocxRenderer
{
    private readonly List<IndexTermEntry> _indexTerms = new();

    /// <summary>Термины, отмеченные в документе полями XE, — по ним <see cref="DocxPublisher"/> строит поле INDEX.</summary>
    public IReadOnlyList<IndexTermEntry> IndexTerms => _indexTerms;

    /// <summary>Поля XE для <c>indexterm</c> (и вложенных подтерминов) внутри абзаца: скрытый текст, в документе не виден.</summary>
    private IEnumerable<OpenXmlElement> IndexFieldRuns(DitaNode indexterm)
    {
        foreach (var entry in IndexTermReader.Read(indexterm))
        {
            _indexTerms.Add(entry);
            var path = string.Join(":", entry.Path.Select(EscapeIndexText));
            if (entry.See.Count == 0 && entry.SeeAlso.Count == 0)
            {
                foreach (var run in Field($" XE \"{path}\" "))
                {
                    yield return run;
                }

                continue;
            }

            // Отсылки Word хранит текстом: у каждой своё поле XE с переключателем \t.
            foreach (var see in entry.See)
            {
                foreach (var run in Field($" XE \"{path}\" \\t \"{EscapeIndexText(L.IndexSee + " " + see)}\" "))
                {
                    yield return run;
                }
            }

            foreach (var seeAlso in entry.SeeAlso)
            {
                foreach (var run in Field($" XE \"{path}\" \\t \"{EscapeIndexText(L.IndexSeeAlso + " " + seeAlso)}\" "))
                {
                    yield return run;
                }
            }
        }
    }

    /// <summary>Термин вне абзаца (блочный indexterm, пролог): абзац нулевой высоты с полями XE.</summary>
    private W.Paragraph? HiddenIndexParagraph(IEnumerable<DitaNode> terms)
    {
        var runs = terms.SelectMany(IndexFieldRuns).ToList();
        if (runs.Count == 0)
        {
            return null;
        }

        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.SpacingBetweenLines { Before = "0", After = "0", Line = "20", LineRule = W.LineSpacingRuleValues.Exact },
            new W.ParagraphMarkRunProperties(new W.Vanish())));
        paragraph.Append(runs);
        return paragraph;
    }

    private static IEnumerable<W.Run> Field(string instruction)
    {
        W.Run Hidden(OpenXmlElement content) => new(new W.RunProperties(new W.Vanish()), content);

        yield return Hidden(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin });
        yield return Hidden(new W.FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve });
        yield return Hidden(new W.FieldChar { FieldCharType = W.FieldCharValues.End });
    }

    // В записи поля XE кавычка, обратная косая черта и двоеточие (разделитель подтермина) экранируются обратной косой чертой.
    private static string EscapeIndexText(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace(":", "\\:");
}
