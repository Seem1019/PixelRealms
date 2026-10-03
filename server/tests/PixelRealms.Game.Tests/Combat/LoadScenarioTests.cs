using PixelRealms.Tools.LoadBot;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-089: el escenario "Mina llena" corre en proceso; aquí solo 10 s simulados y las métricas deterministas (asignación, Gen2).
/// Los tiempos (p99/máx) se evalúan con `dotnet run -c Release --project server/tools/LoadBot`, no en el test (máquina compartida).</summary>
[Collection(nameof(LoadScenarioIsolation))]
public sealed class LoadScenarioTests
{
    /// <summary>HU-088 CA1 (enmendado): los eventos del tick siguen siendo records; el resto no asigna. Medido: ~9 KB por tick con
    /// 30 bots y 300 monstruos peleando; el presupuesto deja margen sin tapar una regresión (una lista o un LINQ por actor y tick
    /// lo superan).</summary>
    private const double AllocPerTickBudgetBytes = 16 * 1024;

    [Fact]
    public void MinaLlena_10s_RunsWithoutExceptions_AndAllocatesUnderBudget()
    {
        var result = CombatScenario.Run(new LoadOptions(DurationSec: 10, Bots: 30, Monsters: 300, Size: 60, Seed: 7, ContentDir: null, Bench: false), TextWriter.Null);
        result.Ticks.ShouldBe(200);
        result.Casts.ShouldBeGreaterThan(50);
        result.Kills.ShouldBeGreaterThan(0);
        result.AurasMax.ShouldBeGreaterThanOrEqualTo(150);
        result.AllocPerSec.ShouldBeLessThanOrEqualTo(CombatScenario.AllocLimitBytesPerSec);
        // HU-088 CA1: en pleno combate solo asignan los eventos del tick (records), dentro de un presupuesto por tick.
        (result.AllocPerSec / CombatScenario.TicksPerSecond).ShouldBeLessThanOrEqualTo(AllocPerTickBudgetBytes);
        result.Gen2.ShouldBe(0);
    }
}

/// <summary>La asignación y el conteo de Gen2 son de todo el proceso: el escenario no puede correr en paralelo con otros tests.</summary>
[CollectionDefinition(nameof(LoadScenarioIsolation), DisableParallelization = true)]
public sealed class LoadScenarioIsolation;
