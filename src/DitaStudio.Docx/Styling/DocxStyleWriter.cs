using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx.Styling;

/// <summary>Создаёт определение стиля Word (w:style) из DocxStyleProps.</summary>
public static class DocxStyleWriter
{
    public static W.Style Create(string id, string name, DocxStyleKind kind, string? basedOn, DocxStyleProps props,
        bool primary = false, string? next = null, W.NumberingProperties? numbering = null, int? outlineLevel = null)
    {
        var style = new W.Style
        {
            Type = kind == DocxStyleKind.Paragraph ? W.StyleValues.Paragraph : W.StyleValues.Character,
            StyleId = id,
            StyleName = new W.StyleName { Val = name }
        };

        if (!string.IsNullOrEmpty(basedOn))
        {
            style.BasedOn = new W.BasedOn { Val = basedOn };
        }

        if (next is not null)
        {
            style.NextParagraphStyle = new W.NextParagraphStyle { Val = next };
        }

        if (primary)
        {
            style.PrimaryStyle = new W.PrimaryStyle();
        }

        if (kind == DocxStyleKind.Paragraph)
        {
            var paragraph = DocxPropsWriter.ParagraphElements(props, numbering, outlineLevel).ToList();
            if (paragraph.Count > 0)
            {
                style.StyleParagraphProperties = DocxPropsWriter.Fill(new W.StyleParagraphProperties(), paragraph);
            }
        }

        // У стиля абзаца заливка — это заливка абзаца (она уже в pPr), а не фон каждого знака.
        var runSource = props;
        if (kind == DocxStyleKind.Paragraph && props.Background is not null)
        {
            runSource = props.Clone();
            runSource.Background = null;
        }

        var run = DocxPropsWriter.RunElements(runSource).ToList();
        if (run.Count > 0)
        {
            style.StyleRunProperties = DocxPropsWriter.Fill(new W.StyleRunProperties(), run);
        }

        return style;
    }
}
