using PixelRealms.Tools.LoadBot;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-089: el escenario "Mina llena" corre en proceso; aquí solo 10 s simulados y las métricas deterministas (asignación, Gen2).
/// Los tiempos (p99/máx) se evalúan con `dotnet run -c Release --project server/tools/LoadBot`, no en el test (máquina compartida).</summary>
public sealed class LoadScenarioTests
{
    [Fact]
    public void MinaLlena_10s_RunsWithoutExceptions_AndAllocatesUnderBudget()
    {
        var result = CombatScenario.Run(new LoadOptions(DurationSec: 10, Bots: 30, Monsters: 300, Size: 60, Seed: 7, ContentDir: null, Bench: false), TextWriter.Null);
        result.Ticks.ShouldBe(200);
        result.Casts.ShouldBeGreaterThan(50);
        result.Kills.ShouldBeGreaterThan(0);
        result.AurasMax.ShouldBeGreaterThanOrEqualTo(150);
        result.AllocPerSec.ShouldBeLessThanOrEqualTo(CombatScenario.AllocLimitBytesPerSec);
        result.Gen2.ShouldBe(0);
    }
}
