using DitaStudio.Core.Editing;
using DitaStudio.Desktop.Views;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Зоны сброса строки карты и прокрутка списка у краёв при перетаскивании.</summary>
public sealed class MapDropZonesTests
{
    [Theory]
    [InlineData(0, DropPosition.Before)]
    [InlineData(5, DropPosition.Before)]
    [InlineData(6, DropPosition.Child)]
    [InlineData(12, DropPosition.Child)]
    [InlineData(18, DropPosition.Child)]
    [InlineData(18.5, DropPosition.After)]
    [InlineData(24, DropPosition.After)]
    public void PositionAt_TopQuarterBefore_MiddleInto_BottomQuarterAfter(double y, DropPosition expected) =>
        Assert.Equal(expected, MapDropZones.PositionAt(y, 24));

    [Fact]
    public void PositionAt_ZeroHeightRow_IsAfter() => Assert.Equal(DropPosition.After, MapDropZones.PositionAt(3, 0));

    [Fact]
    public void AutoScrollStep_OnlyNearEdges_ProportionalToProximity()
    {
        const double height = 400;
        Assert.Equal(0, MapDropZones.AutoScrollStep(200, height));      // середина списка — стоит на месте
        Assert.Equal(0, MapDropZones.AutoScrollStep(40, height));       // ещё не у края
        Assert.Equal(0, MapDropZones.AutoScrollStep(height - 40, height));

        var nearTop = MapDropZones.AutoScrollStep(16, height);
        var atTop = MapDropZones.AutoScrollStep(0, height);
        Assert.True(nearTop < 0 && atTop < nearTop, "вверх — отрицательный шаг, у самого края быстрее");

        var nearBottom = MapDropZones.AutoScrollStep(height - 16, height);
        var atBottom = MapDropZones.AutoScrollStep(height, height);
        Assert.True(nearBottom > 0 && atBottom > nearBottom);
        Assert.InRange(atBottom, 1, 22);
    }

    [Fact]
    public void AutoScrollStep_PointerOutsideOrTinyList_DoesNotScroll()
    {
        Assert.Equal(0, MapDropZones.AutoScrollStep(-5, 400));
        Assert.Equal(0, MapDropZones.AutoScrollStep(405, 400));
        Assert.Equal(0, MapDropZones.AutoScrollStep(10, 50)); // список ниже двух краёв — прокручивать нечего
    }
}
