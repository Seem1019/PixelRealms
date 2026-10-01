using System.Diagnostics;
using PixelRealms.Game.Core;

namespace PixelRealms.Server.Hosting;

/// <summary>
/// HU-004: bucle de simulación de paso fijo (20 Hz) en un hilo dedicado. Solo este hilo muta el <see cref="World"/> (regla 2).
/// Acumulador + Stopwatch para no derivar; recupera como mucho <see cref="GameConstants.MaxCatchUpTicks"/> ticks atrasados;
/// loguea `warn` en ticks &gt; 50 ms y `tick p50/p99` + entidades cada 30 s; se detiene limpio en &lt; 1 s.
/// </summary>
public sealed class GameLoopService(Simulation simulation, ILogger<GameLoopService> logger, NetMetrics? metrics = null) : IHostedService, IDisposable
{
    private readonly ManualResetEventSlim _stopped = new(false);
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    public Simulation Simulation { get; } = simulation;

    public TickStats Stats { get; } = new();

    public long TicksRun => Simulation.Tick;

    /// <summary>Hook opcional que se ejecuta en el hilo del tick al parar (guardar jugadores, HU-026).</summary>
    public Action? OnStopping { get; set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "GameLoop" };
        _thread.Start();
        logger.LogInformation("GameLoop iniciado: tick de {TickMs} ms", GameConstants.TickMs);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        var ok = _stopped.Wait(TimeSpan.FromSeconds(1), CancellationToken.None);
        if (!ok) logger.LogWarning("GameLoop no se detuvo en 1 s");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts.Dispose();
        _stopped.Dispose();
    }

    private void Run()
    {
        var scheduler = new TickScheduler();
        var sw = Stopwatch.StartNew();
        var lastMs = 0L;
        var lastReport = 0L;
        var lastSample = 0L;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var now = sw.ElapsedMilliseconds;
                var ticks = scheduler.Advance(now - lastMs);
                lastMs = now;
                for (var i = 0; i < ticks && !_cts.IsCancellationRequested; i++)
                {
                    var t0 = Stopwatch.GetTimestamp();
                    try
                    {
                        Simulation.RunTick();
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // Una excepción en el tick no debe tumbar el loop (server-authority-reviewer §2).
                        logger.LogError(ex, "Excepción en el tick {Tick}", Simulation.Tick);
                    }
                    var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    Stats.Record(ms);
                    if (ms > GameConstants.SlowTickWarnMs)
                        logger.LogWarning("Tick {Tick} lento: {Ms:F1} ms", Simulation.Tick, ms);
                }
                if (metrics is not null && now - lastSample >= 1000) { lastSample = now; metrics.Sample(now); } // HU-072
                if (now - lastReport >= 30_000)
                {
                    lastReport = now;
                    var (p50, p99) = Stats.Percentiles();
                    logger.LogInformation("tick p50 {P50:F2} ms · p99 {P99:F2} ms · entidades {Entities} · tick {Tick}", p50, p99, Simulation.EntityCount(), Simulation.Tick);
                }
                Sleep(scheduler.MsUntilNextTick, sw, lastMs);
            }
            OnStopping?.Invoke();
        }
        finally
        {
            _stopped.Set();
        }
    }

    private static void Sleep(long msUntilNext, Stopwatch sw, long lastMs)
    {
        // Duerme grueso y gira fino los últimos ~2 ms (skill dotnet-server §Reglas del game loop).
        var target = lastMs + msUntilNext;
        var remaining = target - sw.ElapsedMilliseconds;
        if (remaining > 2) Thread.Sleep((int)(remaining - 2));
        var spinner = new SpinWait();
        while (sw.ElapsedMilliseconds < target) spinner.SpinOnce(-1);
    }
}
