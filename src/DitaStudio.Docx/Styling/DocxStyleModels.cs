using System.Text.RegularExpressions;
using DitaStudio.Core.Model;

namespace DitaStudio.Docx.Styling;

/// <summary>Вид таблиц: рамки, заливка шапки и строк, внутренние поля ячеек.</summary>
public sealed class DocxTableLook
{
    public DocxBorder Outer { get; set; } = new("single", 0.5, "999999");
    public DocxBorder Inner { get; set; } = new("single", 0.5, "CCCCCC");

    /// <summary>Заливка ячеек шапки; "" — без заливки.</summary>
    public string HeaderFill { get; set; } = "E8E8E8";

    /// <summary>Заливка остальных ячеек; "" — без заливки.</summary>
    public string BodyFill { get; set; } = string.Empty;

    /// <summary>Внутренние поля ячеек, пт; null — как в Word по умолчанию.</summary>
    public double? CellPaddingPt { get; set; }

    /// <summary>Ширина таблицы (CSS <c>table { width }</c>) в процентах ширины текста; null — 100 %.</summary>
    public double? WidthPercent { get; set; }

    /// <summary>Ширина таблицы в пунктах (если задана длиной); null — не задана.</summary>
    public double? WidthPt { get; set; }
}

/// <summary>Размер страницы и поля, пт. По умолчанию — A4 с полями 20 мм.</summary>
public sealed record DocxPageSetup(double WidthPt, double HeightPt, double TopPt, double RightPt, double BottomPt, double LeftPt)
{
    public const double MmToPt = 72 / 25.4;

    public static DocxPageSetup A4 => new(210 * MmToPt, 297 * MmToPt, 20 * MmToPt, 20 * MmToPt, 20 * MmToPt, 20 * MmToPt);
}

/// <summary>Правило CSS для произвольного класса — значения outputclass или имени элемента DITA.</summary>
public sealed record DocxClassRule(IReadOnlyList<string> Classes, IReadOnlyList<CssDeclaration> Declarations, int Specificity, int Order);

/// <summary>
/// Правило с настоящим селектором (потомок, ребёнок, атрибут, <c>:first-child</c>, <c>:nth-child()</c>, <c>:not()</c>,
/// <c>::before</c>…): сопоставляется с элементом DITA во время сборки. <see cref="Hits"/> — на сколько элементов
/// оно подошло (для предупреждения о правиле, которое ничего не оформило).
/// </summary>
public sealed class DocxSelectorRule
{
    public DocxSelectorRule(CssSelector selector, IReadOnlyList<CssDeclaration> declarations, int order)
    {
        Selector = selector;
        Declarations = declarations;
        Order = order;
    }

    public CssSelector Selector { get; }

    public IReadOnlyList<CssDeclaration> Declarations { get; }

    public int Order { get; }

    public int Specificity => Selector.Specificity;

    public int Hits { get; set; }
}

/// <summary>Значения по умолчанию документа (docDefaults).</summary>
public static class DocxDefaults
{
    public const string FontFamily = "Calibri";
    public const double FontSizePt = 11;
}
