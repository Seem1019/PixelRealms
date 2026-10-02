using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Players;

public sealed class PlayerMapperTests
{
    private static PlayerMapper Mapper() => new(new ReloadableContent(TestContent.Load(), TestContent.ContentDir));

    private static CharacterSaveDto Dto() => new(Guid.NewGuid(), Guid.NewGuid(), "Ana", "warrior", 3, 0, 50, "meadow", 23, 60, 100, 0, [], [], []);

    [Fact]
    public void ToStatsUpdate_DoesNotTouchTheLivePlayer()
    {
        var mapper = Mapper();
        var p = mapper.ToPlayer(Dto(), new EntityId(1));
        // Máximos vivos subidos por un aura (CombatServices.Recalculate cuenta auras; el mapper no las conoce).
        p.MaxHp += 40;
        p.Hp = p.MaxHp;
        var (maxHp, hp, maxRes, res) = (p.MaxHp, p.Hp, p.MaxResource, p.Resource);

        var update = mapper.ToStatsUpdate(p); // se envía tras cada Welcome, también al reconectar

        (p.MaxHp, p.Hp, p.MaxResource, p.Resource).ShouldBe((maxHp, hp, maxRes, res));
        update.Derived.MaxHp.ShouldBe(maxHp); // el mismo máximo que ve el cliente en los Snapshot
        update.Gold.ShouldBe(50);
    }
}
