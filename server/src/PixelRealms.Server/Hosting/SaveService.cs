using System.Threading.Channels;
using PixelRealms.Persistence.Repositories;

namespace PixelRealms.Server.Hosting;

/// <summary>
/// Guarda personajes fuera del tick (regla 2): el tick encola DTOs inmutables; esta tarea los escribe con 3 reintentos y
/// backoff y nunca bloquea el loop (HU-026 CA4). Al apagar, vacía lo pendiente (máx. 10 s, HU-026 CA2).
/// </summary>
public sealed class SaveService(ICharacterRepository characters, ILogger<SaveService> logger) : BackgroundService
{
    private readonly Channel<CharacterSaveDto> _queue = Channel.CreateUnbounded<CharacterSaveDto>(new UnboundedChannelOptions { SingleReader = true });
    private int _saved;
    private int _failed;
    private int _pending;

    /// <summary>Plazo para vaciar la cola al apagar (HU-026 CA2).</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int Saved => _saved;

    public int Failed => _failed;

    /// <summary>Guardados encolados y aún no terminados.</summary>
    public int Pending => _pending;

    public void Enqueue(CharacterSaveDto dto)
    {
        Interlocked.Increment(ref _pending);
        if (_queue.Writer.TryWrite(dto)) return;
        // La cola ya se cerró al apagar: nadie lo va a escribir.
        Interlocked.Decrement(ref _pending);
        LogLost(dto, "encolado después del apagado");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var dto in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SaveWithRetryAsync(dto, CancellationToken.None);
                Interlocked.Decrement(ref _pending);
                // ReadAllAsync no mira el token entre elementos ya encolados: al apagar, el resto lo vacía StopAsync.
                if (stoppingToken.IsCancellationRequested) break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // El GameLoop ya paró y encoló los guardados de apagado: lo que llegue después se rechaza y se loguea en Enqueue.
        _queue.Writer.TryComplete();
        // Primero se detiene ExecuteAsync: la cola es de un solo lector y dos lectores a la vez pueden entregar un DTO nulo.
        await base.StopAsync(cancellationToken);
        // Con el token del host ya cancelado base.StopAsync vuelve sin esperar, aunque ExecuteAsync solo tenga que despertar y salir.
        if (ExecuteTask is { IsCompleted: false } running) await Task.WhenAny(running, Task.Delay(DrainTimeout, CancellationToken.None));
        if (ExecuteTask is { IsCompleted: false } stillRunning)
        {
            // Un guardado sigue en curso: vaciar ahora volvería a tener dos lectores. Lo pendiente se loguea cuando termine.
            logger.LogError("El guardado en segundo plano no terminó al apagar: {Pending} guardados pendientes sin escribir", _pending);
            _ = stillRunning.ContinueWith(_ => LogRemaining("el guardado en segundo plano no terminó a tiempo"), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return;
        }
        // Vacía la cola antes de salir: lo que el tick encoló al apagar debe llegar a la BD.
        using var cts = new CancellationTokenSource(DrainTimeout);
        var drained = 0;
        CharacterSaveDto? current = null;
        try
        {
            // El plazo se mira antes de sacar el siguiente: un DTO leído nunca se descarta sin guardarlo o loguearlo.
            while (!cts.IsCancellationRequested && _queue.Reader.TryRead(out current))
            {
                await SaveWithRetryAsync(current, cts.Token);
                current = null;
                Interlocked.Decrement(ref _pending);
                drained++;
            }
        }
        catch (Exception) when (cts.IsCancellationRequested) { } // con el token cortado el driver puede lanzar algo distinto de OCE
        finally
        {
            if (drained > 0) logger.LogInformation("Guardados {Count} personajes al apagar", drained);
            if (current is not null)
            {
                // Puede que la BD sí lo confirmara antes de reaccionar al token: Failed sobra como mucho, y el DTO queda en el log.
                Interlocked.Decrement(ref _pending);
                LogLost(current, "plazo de apagado agotado durante el guardado");
            }
            LogRemaining("plazo de apagado agotado");
        }
    }

    /// <summary>Saca y loguea lo que quede en la cola; solo cuando ya no hay otro lector.</summary>
    private void LogRemaining(string reason)
    {
        while (_queue.Reader.TryRead(out var left))
        {
            Interlocked.Decrement(ref _pending);
            LogLost(left, reason);
        }
    }

    private void LogLost(CharacterSaveDto dto, string reason)
    {
        Interlocked.Increment(ref _failed);
        logger.LogError("Guardado de {Name} no escrito ({Reason}). DTO: {Dto}", dto.Name, reason, System.Text.Json.JsonSerializer.Serialize(dto));
    }

    /// <summary>Guarda de inmediato (uso en tests y en el apagado).</summary>
    public async Task SaveWithRetryAsync(CharacterSaveDto dto, CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(200);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await characters.SaveAsync(dto, ct);
                Interlocked.Increment(ref _saved);
                return;
            }
            // Solo se propaga la cancelación pedida por `ct`; otra (p. ej. un timeout interno del driver) es un fallo más y se reintenta.
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 3)
            {
                logger.LogWarning(ex, "Guardado de {Name} falló (intento {Attempt})", dto.Name, attempt);
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _failed);
                logger.LogError(ex, "Guardado de {Name} falló definitivamente. DTO: {Dto}", dto.Name, System.Text.Json.JsonSerializer.Serialize(dto));
                return;
            }
        }
    }
}
