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

    public int Saved => _saved;

    public int Failed => _failed;

    /// <summary>Guardados encolados y aún no terminados.</summary>
    public int Pending => _pending;

    public void Enqueue(CharacterSaveDto dto)
    {
        Interlocked.Increment(ref _pending);
        _queue.Writer.TryWrite(dto);
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
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Primero se detiene ExecuteAsync: la cola es de un solo lector y dos lectores a la vez pueden entregar un DTO nulo.
        await base.StopAsync(cancellationToken);
        if (ExecuteTask is { IsCompleted: false })
        {
            // El host agotó su plazo con un guardado aún en curso: vaciar ahora volvería a tener dos lectores.
            logger.LogError("El guardado en segundo plano no terminó al apagar: {Pending} guardados pendientes sin escribir", _pending);
            return;
        }
        // Vacía la cola antes de salir: lo que el tick encoló al apagar debe llegar a la BD.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var drained = 0;
        while (_queue.Reader.TryRead(out var dto) && !cts.IsCancellationRequested)
        {
            await SaveWithRetryAsync(dto, cts.Token);
            Interlocked.Decrement(ref _pending);
            drained++;
        }
        if (drained > 0) logger.LogInformation("Guardados {Count} personajes al apagar", drained);
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
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 3)
            {
                logger.LogWarning(ex, "Guardado de {Name} falló (intento {Attempt})", dto.Name, attempt);
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref _failed);
                logger.LogError(ex, "Guardado de {Name} falló definitivamente. DTO: {Dto}", dto.Name, System.Text.Json.JsonSerializer.Serialize(dto));
                return;
            }
        }
    }
}
