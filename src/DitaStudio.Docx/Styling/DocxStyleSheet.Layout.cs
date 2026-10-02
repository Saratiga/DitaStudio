using System.Text.RegularExpressions;
using DitaStudio.Core.Model;
using DitaStudio.Core.Localization;

namespace DitaStudio.Docx.Styling;

// Вид таблиц, пометка изменений, размер страницы из @page.
public sealed partial class DocxStyleSheet
{
    private void BuildTableLook(Dictionary<string, List<(int, int, IReadOnlyList<CssDeclaration>)>> targets)
    {
        CssBox? Resolve(string target)
        {
            if (!targets.TryGetValue(target, out var matched))
            {
                return null;
            }

            var box = new CssBox(new DocxStyleProps());
            CssApplier.Apply(box, Ordered(matched), FontSizeOf(DocxStyleCatalog.TableText), this, silent: true);
            return box;
        }

        static DocxBorder? AnySide(DocxStyleProps p) => p.BorderTop ?? p.BorderLeft ?? p.BorderBottom ?? p.BorderRight;

        if (targets.TryGetValue(DocxStyleCatalog.TableTarget, out var tableRules) &&
            Ordered(tableRules).LastOrDefault(d => d.Property == "width") is { } widthRule)
        {
            var width = Substitute(widthRule.Value).Trim();
            if (width.EndsWith('%') && double.TryParse(width[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            {
                Table.WidthPercent = Math.Clamp(percent, 5, 100);
            }
            else if (CssApplier.Length(width, DocxDefaults.FontSizePt) is { } widthPt && widthPt > 0)
            {
                Table.WidthPt = widthPt;
            }
        }

        var table = Resolve(DocxStyleCatalog.TableTarget);
        var th = Resolve(DocxStyleCatalog.HeaderCellTarget);
        var td = Resolve(DocxStyleCatalog.BodyCellTarget);

        // Рамки ячеек (th, td) задают и внутренние линии, и внешние — так таблица выглядит в
        // HTML со схлопнутыми рамками; явная рамка у самой таблицы перекрывает внешнюю.
        var cellBorder = (td is null ? null : AnySide(td.Props)) ?? (th is null ? null : AnySide(th.Props));
        if (cellBorder is not null)
        {
            Table.Inner = cellBorder;
            Table.Outer = cellBorder;
        }

        if (table is not null && AnySide(table.Props) is { } tableBorder)
        {
            Table.Outer = tableBorder;
        }

        if (th?.Props.Background is { } headerFill)
        {
            Table.HeaderFill = headerFill;
        }

        if (td?.Props.Background is { } bodyFill)
        {
            Table.BodyFill = bodyFill;
        }
        else if (table?.Props.Background is { } tableFill)
        {
            Table.BodyFill = tableFill;
        }

        var padding = td?.PaddingLeft ?? td?.PaddingTop ?? th?.PaddingLeft ?? th?.PaddingTop;
        if (padding is not null)
        {
            Table.CellPaddingPt = Math.Clamp(padding.Value, 0, 72);
        }
    }

    private void BuildRevBorder(Dictionary<string, List<(int, int, IReadOnlyList<CssDeclaration>)>> targets)
    {
        if (!targets.TryGetValue(DocxStyleCatalog.RevTarget, out var matched))
        {
            return;
        }

        var box = new CssBox(new DocxStyleProps { BorderLeft = RevBorder });
        CssApplier.Apply(box, Ordered(matched), DocxDefaults.FontSizePt, this, silent: true);
        box.ResolvePadding();
        RevBorder = box.Props.BorderLeft ?? RevBorder;
    }

    private void BuildPage(CssStyleSheet parsed)
    {
        if (parsed.PageRules.Any(r => r.Pseudo is not null))
        {
            _diag.Add(Loc.T("Core_ThePageRulesFirstLeftRight") +
                      Loc.T("Core_AndMirroredMarginsAreSetIn"));
        }

        var width = Page.WidthPt;
        var height = Page.HeightPt;
        double top = Page.TopPt, right = Page.RightPt, bottom = Page.BottomPt, left = Page.LeftPt;

        foreach (var rule in parsed.PageRules.Where(r => r.Pseudo is null).OrderBy(r => r.Order))
        {
            foreach (var d in rule.Declarations)
            {
                var value = Substitute(d.Value);
                switch (d.Property)
                {
                    case "size":
                        if (!TryParsePageSize(value, ref width, ref height))
                        {
                            _diag.UnsupportedValue(d.Property, value);
                        }

                        break;
                    case "margin":
                        var sides = CssApplier.Sides(value, DocxDefaults.FontSizePt);
                        if (sides is null)
                        {
                            _diag.UnsupportedValue(d.Property, value);
                            break;
                        }

                        (top, right, bottom, left) = sides.Value;
                        break;
                    case "margin-top" when CssApplier.Length(value, DocxDefaults.FontSizePt) is { } t:
                        top = t;
                        break;
                    case "margin-right" when CssApplier.Length(value, DocxDefaults.FontSizePt) is { } r:
                        right = r;
                        break;
                    case "margin-bottom" when CssApplier.Length(value, DocxDefaults.FontSizePt) is { } b:
                        bottom = b;
                        break;
                    case "margin-left" when CssApplier.Length(value, DocxDefaults.FontSizePt) is { } l:
                        left = l;
                        break;
                    default:
                        _diag.UnsupportedProperty("@page " + d.Property);
                        break;
                }
            }
        }

        static double Clamp(double v) => Math.Clamp(v, 0, 300);
        Page = new DocxPageSetup(width, height, Clamp(top), Clamp(right), Clamp(bottom), Clamp(left));
    }

    private static readonly Dictionary<string, (double W, double H)> PaperSizes = new(StringComparer.Ordinal)
    {
        ["a3"] = (297, 420), ["a4"] = (210, 297), ["a5"] = (148, 210), ["a6"] = (105, 148),
        ["b4"] = (250, 353), ["b5"] = (176, 250), ["letter"] = (215.9, 279.4), ["legal"] = (215.9, 355.6),
        ["ledger"] = (279.4, 431.8)
    };

    private static bool TryParsePageSize(string value, ref double width, ref double height)
    {
        var tokens = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double? w = null, h = null;
        var landscape = false;
        var lengths = new List<double>();
        foreach (var token in tokens)
        {
            if (PaperSizes.TryGetValue(token, out var paper))
            {
                (w, h) = (paper.W * DocxPageSetup.MmToPt, paper.H * DocxPageSetup.MmToPt);
            }
            else if (token == "landscape")
            {
                landscape = true;
            }
            else if (token is "portrait" or "auto")
            {
            }
            else if (CssValues.TryParseLength(token, out var length) && length.Unit is not ("em" or "%" or "ex" or "ch"))
            {
                lengths.Add(length.ToPoints(DocxDefaults.FontSizePt));
            }
            else
            {
                return false;
            }
        }

        if (lengths.Count == 1)
        {
            (w, h) = (lengths[0], lengths[0]);
        }
        else if (lengths.Count >= 2)
        {
            (w, h) = (lengths[0], lengths[1]);
        }

        w ??= width;
        h ??= height;
        if (landscape && w < h || !landscape && tokens.Contains("portrait") && w > h)
        {
            (w, h) = (h, w);
        }

        if (w < 72 || h < 72)
        {
            return false;
        }

        (width, height) = (w.Value, h.Value);
        return true;
    }
}
