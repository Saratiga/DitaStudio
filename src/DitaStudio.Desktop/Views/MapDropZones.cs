using DitaStudio.Core.Editing;

namespace DitaStudio.Desktop.Views;

/// <summary>
/// Геометрия перетаскивания в дереве карты: в какую зону строки попал указатель и как быстро
/// прокручивать список у его краёв. Отдельно от окна — чтобы проверяться без мыши.
/// </summary>
public static class MapDropZones
{
    /// <summary>Доля высоты строки сверху и снизу, отдаваемая под «перед» и «после».</summary>
    public const double EdgeShare = 0.25;

    /// <summary>Зона строки: верхняя четверть — «перед», нижняя — «после», середина — «внутрь».</summary>
    public static DropPosition PositionAt(double y, double rowHeight)
    {
        if (rowHeight <= 0)
        {
            return DropPosition.After;
        }

        var share = y / rowHeight;
        return share < EdgeShare ? DropPosition.Before : share > 1 - EdgeShare ? DropPosition.After : DropPosition.Child;
    }

    /// <summary>
    /// Шаг прокрутки, пикселей за такт, при указателе на высоте <paramref name="y"/> в области списка
    /// высотой <paramref name="viewportHeight"/>: у верхнего и нижнего края — плавно, тем быстрее, чем
    /// ближе к краю; в остальной области списка — 0. Так строку можно донести до верха длинной карты, а
    /// список сам «убегать» вместе с ней не будет.
    /// </summary>
    public static double AutoScrollStep(double y, double viewportHeight, double edge = 32, double maxStep = 22)
    {
        if (viewportHeight <= edge * 2 || y < 0 || y > viewportHeight)
        {
            return 0;
        }

        if (y < edge)
        {
            return -maxStep * (edge - y) / edge;
        }

        return y > viewportHeight - edge ? maxStep * (y - (viewportHeight - edge)) / edge : 0;
    }
}
