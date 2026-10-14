using DitaStudio.Core.Model;

namespace DitaStudio.Docx.Styling;

// Оформление таблиц из CSS по классам и селекторам: у каждой таблицы и ячейки свой вид, а не один на весь документ.
// Общий вид (`table`, `td`, `th`) собирается при сборке стилей (BuildTableLook); правила для своих классов и селекторов
// (`table.synchro`, `.synchro td`, `thead entry`…) сопоставляются с деревом DITA и накладываются сверху.
public sealed partial class DocxStyleSheet
{
    /// <summary>Правила для таблиц и ячеек есть, и вид надо определять у каждой таблицы отдельно.</summary>
    public bool HasTableRules => _classRules.Count > 0 || _selectorRules.Count > 0;

    /// <summary>Вид таблицы <paramref name="table"/>: общий вид документа, поверх него — правила CSS, подошедшие именно этой таблице.</summary>
    public DocxTableLook LookFor(DitaNode table)
    {
        if (!HasTableRules)
        {
            return Table;
        }

        var declarations = Cascade(table);
        if (declarations.Count == 0)
        {
            return Table;
        }

        var box = new CssBox(new DocxStyleProps());
        CssApplier.Apply(box, declarations, FontSizeOf(DocxStyleCatalog.TableText), this, silent: true);
        box.ResolvePadding();
        var props = box.Props;

        var look = Table.Clone();
        var changed = false;
        if (SideBorders(props) is { } sides)
        {
            look.Top = sides.Top;
            look.Left = sides.Left;
            look.Bottom = sides.Bottom;
            look.Right = sides.Right;
            changed = true;
        }

        if (props.Background is { } fill)
        {
            look.BodyFill = fill;
            changed = true;
        }

        if (declarations.LastOrDefault(d => d.Property == "width") is { } widthRule)
        {
            var width = Substitute(widthRule.Value).Trim();
            if (width.EndsWith('%') && double.TryParse(width[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            {
                look.WidthPercent = Math.Clamp(percent, 5, 100);
                look.WidthPt = null;
                changed = true;
            }
            else if (CssApplier.Length(width, DocxDefaults.FontSizePt) is { } widthPt && widthPt > 0)
            {
                look.WidthPt = widthPt;
                changed = true;
            }
        }

        return changed ? look : Table;
    }

    /// <summary>Оформление ячейки <paramref name="entry"/> из правил CSS для классов и селекторов: рамки сторон, заливка и поля.</summary>
    public DocxCellLook? CellLookFor(DitaNode entry)
    {
        if (!HasTableRules)
        {
            return null;
        }

        var declarations = Cascade(entry);
        if (declarations.Count == 0)
        {
            return null;
        }

        var box = new CssBox(new DocxStyleProps());
        CssApplier.Apply(box, declarations, FontSizeOf(DocxStyleCatalog.TableText), this, silent: true);
        var props = box.Props; // padding ячейки — её поля (tcMar), а не зазор у рамки или отступ абзаца: ResolvePadding здесь не нужен
        var sides = SideBorders(props);
        var padding = box.PaddingLeft ?? box.PaddingTop;
        if (sides is null && props.Background is null && padding is null)
        {
            return null;
        }

        return new DocxCellLook(sides?.Top, sides?.Left, sides?.Bottom, sides?.Right, props.Background, padding);
    }

    private static (DocxBorder? Top, DocxBorder? Left, DocxBorder? Bottom, DocxBorder? Right)? SideBorders(DocxStyleProps p) =>
        p.BorderTop is null && p.BorderLeft is null && p.BorderBottom is null && p.BorderRight is null
            ? null
            : (p.BorderTop, p.BorderLeft, p.BorderBottom, p.BorderRight);
}

/// <summary>Оформление одной ячейки из CSS. null у стороны или заливки — «как у таблицы».</summary>
public sealed record DocxCellLook(DocxBorder? Top, DocxBorder? Left, DocxBorder? Bottom, DocxBorder? Right, string? Fill, double? PaddingPt);
