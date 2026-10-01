using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Map;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

public sealed class LineOfSightTests
{
    [Fact]
    public void WallBetween_BlocksSight_OpenGround_DoesNot()
    {
        var grid = new CollisionGrid(10, 10);
        LineOfSight.Has(grid, new Vec2(1.5f, 1.5f), new Vec2(8.5f, 1.5f)).ShouldBeTrue();
        grid.SetBlocksSight(5, 1);
        LineOfSight.Has(grid, new Vec2(1.5f, 1.5f), new Vec2(8.5f, 1.5f)).ShouldBeFalse();
        LineOfSight.Has(grid, new Vec2(1.5f, 1.5f), new Vec2(8.5f, 5.5f)).ShouldBeTrue(); // diagonal que no pasa por (5,1)
    }

    [Fact]
    public void OriginAndDestinationTiles_NeverBlock()
    {
        var grid = new CollisionGrid(10, 10);
        grid.SetBlocksSight(1, 1);
        grid.SetBlocksSight(3, 1);
        LineOfSight.Has(grid, new Vec2(1.5f, 1.5f), new Vec2(3.5f, 1.5f)).ShouldBeTrue();
    }
}
