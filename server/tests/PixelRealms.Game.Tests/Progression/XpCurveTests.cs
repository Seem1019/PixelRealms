using PixelRealms.Content.Defs;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Progression;

public sealed class XpCurveTests
{
    private readonly ProgressionRules _p = TestContent.Load().Rules.Progression;

    [Fact]
    public void Table_MatchesGdd_1To14() // HU-041 CA4: valores exactos del GDD
    {
        var expected = new[] { 100, 367, 933, 1750, 2817, 4392, 6000, 7858, 9967, 12750, 15400, 18300, 21450, 24850 };
        for (var level = 1; level <= 14; level++) XpCurve.XpToNextLevel(_p, level).ShouldBe(expected[level - 1], $"nivel {level}");
        XpCurve.XpToNextLevel(_p, 15).ShouldBe(0);
        Enumerable.Range(1, 14).Sum(l => XpCurve.XpToNextLevel(_p, l)).ShouldBe(126_934);
    }

    [Fact]
    public void MonsterXp_ByType()
    {
        XpCurve.MonsterXp(_p, 1, MonsterType.Normal).ShouldBe(6);
        XpCurve.MonsterXp(_p, 5, MonsterType.Hard).ShouldBe(31);
        XpCurve.MonsterXp(_p, 6, MonsterType.Elite).ShouldBe(93);
        XpCurve.MonsterXp(_p, 6, MonsterType.Boss).ShouldBe(310);
    }

    [Fact]
    public void SoloKillXp_LevelDiff() // HU-040 CA1b
    {
        var db = TestContent.Load();
        XpCurve.SoloKillXp(_p, db.Monster("slime"), 5).ShouldBe(4);
        XpCurve.SoloKillXp(_p, db.Monster("goblin_archer"), 5).ShouldBe(31);
        XpCurve.SoloKillXp(_p, db.Monster("rubble_golem"), 5).ShouldBe(102);
        XpCurve.SoloKillXp(_p, db.Monster("foreman_grask"), 5).ShouldBe(341);
        XpCurve.SoloKillXp(_p, db.Monster("slime"), 6).ShouldBe(0);
    }
}
