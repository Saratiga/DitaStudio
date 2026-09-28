using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace DitaStudio.Desktop.Authoring;

/// <summary>Оформление фразовых элементов DITA в режиме «Автор» — те же правила, что в WPF-версии.</summary>
public static class InlineStyles
{
    /// <summary>Применяет оформление элемента к участку текста редактора блока.</summary>
    /// <param name="brush">Кисть из текущей темы по ключу палитры.</param>
    /// <param name="mono">Моноширинный шрифт темы.</param>
    public static void Apply(VisualLineElementTextRunProperties run, string elementName, Func<string, IBrush?> brush, FontFamily mono)
    {
        var typeface = run.Typeface;

        void Face(FontFamily? family = null, FontStyle? style = null, FontWeight? weight = null) =>
            run.SetTypeface(typeface = new Typeface(family ?? typeface.FontFamily, style ?? typeface.Style, weight ?? typeface.Weight, typeface.Stretch));

        void Code()
        {
            Face(family: mono);
            run.SetBackgroundBrush(brush("CodeBackground"));
        }

        switch (elementName)
        {
            case "b":
                Face(weight: FontWeight.Bold);
                break;
            case "i":
            case "varname":
            case "var":
            case "q":
            case "cite":
                Face(style: FontStyle.Italic);
                break;
            case "term":
                Face(style: FontStyle.Italic);
                run.SetForegroundBrush(brush("AttributeNameBrush"));
                break;
            case "u":
                run.SetTextDecorations(TextDecorations.Underline);
                break;
            case "line-through":
                run.SetTextDecorations(TextDecorations.Strikethrough);
                break;
            case "sup":
                run.SetBaselineAlignment(BaselineAlignment.Superscript);
                run.SetFontRenderingEmSize(run.FontRenderingEmSize * 0.72);
                break;
            case "sub":
                run.SetBaselineAlignment(BaselineAlignment.Subscript);
                run.SetFontRenderingEmSize(run.FontRenderingEmSize * 0.72);
                break;
            case "tt":
            case "codeph":
            case "synph":
            case "filepath":
            case "systemoutput":
            case "msgph":
            case "msgnum":
            case "parmname":
            case "apiname":
            case "option":
            case "cmdname":
            case "markupname":
            case "xmlatt":
            case "xmlelement":
                Code();
                break;
            case "userinput":
                Code();
                Face(weight: FontWeight.SemiBold);
                break;
            case "uicontrol":
            case "wintitle":
            case "shortcut":
                Face(weight: FontWeight.SemiBold);
                break;
            case "keyword":
                run.SetForegroundBrush(brush("Success"));
                break;
            case "xref":
            case "link":
                run.SetForegroundBrush(brush("Accent"));
                run.SetTextDecorations(TextDecorations.Underline);
                break;
        }
    }
}
