using Avalonia.Media;
using AvaloniaEdit.Rendering;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Desktop.Authoring;

/// <summary>Оформление фразовых элементов DITA в режиме «Автор» — те же правила, что в WPF-версии.</summary>
public static class InlineStyles
{
    /// <summary>Классы оформления из «Автора» у фразового элемента: размер (пт) и цвет текста.</summary>
    public static void ApplyFormatting(VisualLineElementTextRunProperties run, DitaNode node, Func<string, IBrush?>? brush = null)
    {
        if (brush is not null)
        {
            ApplyMarks(run, node, brush);
        }

        if (TextFormatting.SizeOf(node) is { } points)
        {
            run.SetFontRenderingEmSize(points * 4.0 / 3);
        }

        if (TextFormatting.ColorOf(node) is { } hex)
        {
            run.SetForegroundBrush(new SolidColorBrush(Color.Parse(hex)));
        }

        if (TextFormatting.MarkOf(node) is { } mark)
        {
            // Маркер: цвет фона, как «выделение текста» в Word; текст на нём чёрный — тёмный фон на светлой теме не должен «съедать» буквы.
            run.SetBackgroundBrush(new SolidColorBrush(Color.Parse(mark)));
            if (TextFormatting.ColorOf(node) is null)
            {
                run.SetForegroundBrush(TextFormatting.IsDarkColor(mark) ? Brushes.White : Brushes.Black);
            }
        }
    }

    private static readonly FontFamily MathFont = new("Cambria Math, Times New Roman, serif");

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
            case "equation-inline":
            case "equation-block":
                // Формула — шрифтом формул, как уравнения Word.
                Face(family: MathFont);
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
            case "fn":
                // Текст сноски выделен фоном плашки — видно, где сноска начинается и кончается.
                run.SetBackgroundBrush(brush("EditorChipBackground"));
                run.SetForegroundBrush(brush("EditorChipText"));
                run.SetFontRenderingEmSize(run.FontRenderingEmSize * 0.9);
                break;
        }
    }

    /// <summary>Условные атрибуты фразы (продукт, аудитория…): их слова подчёркиваются серым пунктиром.</summary>
    public static readonly string[] ConditionAttributes = { "product", "audience", "platform", "props", "otherprops", "deliveryTarget" };

    public static bool HasConditions(DitaNode node) =>
        ConditionAttributes.Any(a => !string.IsNullOrWhiteSpace(node.GetAttribute(a)));

    /// <summary>
    /// Пометки слов внутри блока: правки track changes (вставлено — зелёным с подчёркиванием, удалено — красным зачёркнутым, изменено —
    /// оранжевым с пунктиром, как правки в Word) и условные атрибуты (серый пунктир под словами).
    /// </summary>
    private static void ApplyMarks(VisualLineElementTextRunProperties run, DitaNode node, Func<string, IBrush?> brush)
    {
        void Decorate(TextDecorationLocation location, IBrush? stroke, bool dashed, double thickness = 1)
        {
            var decorations = new TextDecorationCollection();
            if (run.TextDecorations is { } existing)
            {
                decorations.AddRange(existing);
            }

            decorations.Add(new TextDecoration
            {
                Location = location,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeThicknessUnit = TextDecorationUnit.Pixel,
                StrokeDashArray = dashed ? new Avalonia.Collections.AvaloniaList<double> { 2, 2 } : null,
                StrokeOffset = location == TextDecorationLocation.Underline ? 1 : 0,
                StrokeOffsetUnit = TextDecorationUnit.Pixel
            });
            run.SetTextDecorations(decorations);
        }

        switch (node.GetAttribute("status"))
        {
            case "new":
                run.SetForegroundBrush(brush("Success"));
                Decorate(TextDecorationLocation.Underline, brush("Success"), dashed: false);
                break;
            case "deleted":
                run.SetForegroundBrush(brush("Danger"));
                Decorate(TextDecorationLocation.Strikethrough, brush("Danger"), dashed: false);
                break;
            case "changed":
                run.SetForegroundBrush(brush("Warning"));
                Decorate(TextDecorationLocation.Underline, brush("Warning"), dashed: true, 1.5);
                break;
        }

        if (HasConditions(node))
        {
            Decorate(TextDecorationLocation.Underline, brush("EditorTag"), dashed: true);
        }
    }
}
