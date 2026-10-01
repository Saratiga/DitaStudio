using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Tests;

// Д11: границы выделенных ячеек как в меню Word — стороны диапазона, внутренние линии, рамка по сторонам.
internal static partial class CoreChecks
{
    internal static void TableEdgesTests()
    {
        Section("Границы выделенных ячеек (как в Word)");

        DitaNode Table(DitaDocument d) => d.Root.DescendantsAndSelf().First(n => n.Name == "table");
        CellBorders Of(DitaNode table, int row, int column) => CalsBorders.Compute(table)[CellGrid.Of(table)!.At(row, column)!.Entry];
        bool AnyOverrides(DitaNode table) => table.DescendantsAndSelf().Any(n => n.Kind == NodeKind.Element && (n.GetAttribute("rowsep") is not null || n.GetAttribute("colsep") is not null));

        // Центральная ячейка 3×3: все четыре её стороны (внутренние линии таблицы) убираются и возвращаются.
        var d1 = GridTopic(bodyRows: 2, columns: 3);
        var t1 = Table(d1);
        var center = new CellRange(1, 1, 1, 1);
        Check(CalsBorders.AreVisible(t1, center, BorderEdges.Outer), "по умолчанию вокруг ячейки есть все линии");
        Check(CalsBorders.SetEdges(t1, center, BorderEdges.Outer, visible: false), "стороны внутренней ячейки убираются без пропусков");
        Check(Of(t1, 1, 1) == new CellBorders(false, false, false, false), "у ячейки нет ни одной линии");
        Check(!Of(t1, 0, 1).Bottom && !Of(t1, 2, 1).Top && !Of(t1, 1, 0).Right && !Of(t1, 1, 2).Left && Of(t1, 0, 0).Bottom && Of(t1, 2, 2).Top,
            "соседи согласованы (нижняя линия верхней, верхняя нижней…), чужие линии не тронуты");
        Check(!CalsBorders.AreVisible(t1, center, BorderEdges.Outer), "AreVisible видит, что линий нет");
        Check(CalsBorders.SetEdges(t1, center, BorderEdges.Outer, visible: true) && Of(t1, 1, 1) == CellBorders.All && !AnyOverrides(t1), "возвращаем линии — лишних атрибутов rowsep/colsep не остаётся");

        // Внутренние линии всей таблицы: вертикальные убраны, горизонтальные остались.
        var d2 = GridTopic(bodyRows: 2, columns: 3);
        var t2 = Table(d2);
        var whole = CellGrid.Of(t2)!.WholeTable;
        CalsBorders.SetEdges(t2, whole, BorderEdges.InnerVertical, false);
        Check(!Of(t2, 1, 0).Right && !Of(t2, 1, 2).Left && Of(t2, 1, 0).Bottom && Of(t2, 0, 0).Left && Of(t2, 2, 2).Right, "внутренние вертикальные убраны, горизонтальные и внешняя рамка остались");
        Check(!CalsBorders.AreVisible(t2, whole, BorderEdges.InnerVertical) && CalsBorders.AreVisible(t2, whole, BorderEdges.InnerHorizontal), "AreVisible различает внутренние горизонтальные и вертикальные");
        CalsBorders.SetEdges(t2, whole, BorderEdges.All, false);
        Check(Of(t2, 1, 1) == new CellBorders(false, false, false, false) && Of(t2, 0, 0) == new CellBorders(false, false, false, false) && CalsBorders.FrameFlags(t2) == (false, false, false, false) && t2.GetAttribute("frame") == "none",
            "«нет границы» для всей таблицы — ни линий, ни рамки (frame=none)");

        // Верхний край: если выделена вся ширина — рамка сверху снимается, остальная рамка остаётся; для части — пропуск.
        var d3 = GridTopic(bodyRows: 2, columns: 3);
        var t3 = Table(d3);
        var partial = CalsBorders.SetEdges(t3, new CellRange(0, 0, 0, 1), BorderEdges.Top, false);
        Check(!partial && CalsBorders.FrameFlags(t3) == (true, true, true, true) && t3.GetAttribute("outputclass") is null, "верх только у части ячеек: у CALS так нельзя — рамка не тронута, вернулось false");
        Check(CalsBorders.SetEdges(t3, new CellRange(0, 0, 0, 2), BorderEdges.Top, false) && CalsBorders.FrameFlags(t3) == (false, true, true, true), "верх всей ширины снят: рамка слева, справа и снизу осталась");
        Check(t3.GetAttribute("outputclass") == "frame-bottom frame-left frame-right" && t3.GetAttribute("frame") == "sides" && CalsBorders.IsCustom(t3),
            "это не стандартное значение frame — рамка записана классами frame-…, frame — ближайшее стандартное");
        Check(!Of(t3, 0, 0).Top && !Of(t3, 0, 2).Top && Of(t3, 0, 0).Left && Of(t3, 2, 2).Bottom && Of(t3, 1, 1).Top, "ячейки верхней строки без верхней линии, остальное на месте");
        CalsBorders.SetEdges(t3, new CellRange(0, 0, 0, 2), BorderEdges.Top, true);
        Check(CalsBorders.FrameFlags(t3) == (true, true, true, true) && t3.GetAttribute("frame") is null && t3.GetAttribute("outputclass") is null, "вернули верх — рамка снова обычная, лишних атрибутов нет");

        // Только левая линия таблицы (столбец целиком).
        var d4 = GridTopic(bodyRows: 2, columns: 3);
        var t4 = Table(d4);
        CalsBorders.SetEdges(t4, CellGrid.Of(t4)!.WholeColumn(0), BorderEdges.Left, false);
        Check(CalsBorders.FrameFlags(t4) == (true, true, false, true) && !Of(t4, 1, 0).Left && Of(t4, 1, 2).Right, "левый край снят отдельно от правого");
        // Режим «Все границы» таблицы снимает и эти классы.
        CalsBorders.SetMode(t4, TableBorderMode.All);
        Check(CalsBorders.FrameFlags(t4) == (true, true, true, true) && t4.GetAttribute("outputclass") is null, "режим «Все границы» таблицы сбрасывает и классы рамки");

        // Объединённая ячейка: линии внутри неё не считаются.
        var d5 = GridTopic(bodyRows: 3, columns: 4);
        var t5 = Table(d5);
        var g5 = CellGrid.Of(t5)!;
        TableRanges.Merge(g5.Tgroup, new CellRange(1, 2, 1, 2));
        var mergedRange = CellGrid.Of(t5)!.RangeBetween(CellGrid.Of(t5)!.At(1, 1)!.Entry, CellGrid.Of(t5)!.At(2, 2)!.Entry)!.Value;
        Check(CalsBorders.AreVisible(t5, mergedRange, BorderEdges.Outer) && CalsBorders.SetEdges(t5, mergedRange, BorderEdges.All, false) && !CalsBorders.AreVisible(t5, mergedRange, BorderEdges.Outer),
            "объединённая ячейка 2×2: внешние стороны убираются, внутренние участки (внутри ячейки) в расчёт не входят");
        Check(Of(t5, 1, 1) == new CellBorders(false, false, false, false) && Of(t5, 1, 0).Right == false && Of(t5, 3, 2).Top == false && Of(t5, 0, 1).Bottom == false, "у объединённой ячейки нет линий, соседи согласованы");
    }
}
