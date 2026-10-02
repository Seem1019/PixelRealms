using System.Threading.Channels;
using PixelRealms.Persistence.Repositories;

namespace PixelRealms.Server.Hosting;

/// <summary>
/// Guarda personajes fuera del tick (regla 2): el tick encola DTOs inmutables; esta tarea los escribe con 3 reintentos y
/// backoff y nunca bloquea el loop (HU-026 CA4). Al apagar, vacía lo pendiente (máx. 10 s, HU-026 CA2).
/// </summary>
public sealed class SaveService(ICharacterRepository characters, ILogger<SaveService> logger) : BackgroundService
{
    private readonly Channel<Queued> _queue = Channel.CreateUnbounded<Queued>(new UnboundedChannelOptions { SingleReader = true });
    private int _saved;
    private int _failed;
    private int _pending;
    /// <summary>Guardados pendientes por personaje: HelloGate espera a que se escriban antes de leerlo (HU-015 CA5).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _pendingByCharacter = new();
    /// <summary>Generación del último guardado encolado por personaje (crece en 1 con cada Enqueue).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, long> _enqueuedGen = new();
    /// <summary>Generación más alta ya escrita en la BD por personaje.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, long> _writtenGen = new();

    /// <summary>
    /// Auditoría de guardados que fallaron del todo, por personaje: se escribe con el siguiente guardado de ese personaje. Los
    /// items van en el estado completo de cada guardado y no se pierden, pero la auditoría solo viaja una vez (HU-015, HU-057).
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, IReadOnlyList<AuditEntry>> _unwrittenAudit = new();

    private readonly record struct Queued(CharacterSaveDto Dto, long Gen);

    /// <summary>Plazo para vaciar la cola al apagar (HU-026 CA2).</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int Saved => _saved;

    public int Failed => _failed;

    /// <summary>Guardados encolados y aún no terminados.</summary>
    public int Pending => _pending;

    /// <summary>Encola el guardado y devuelve su generación (monótona por personaje).</summary>
    public long Enqueue(CharacterSaveDto dto)
    {
        Interlocked.Increment(ref _pending);
        _pendingByCharacter.AddOrUpdate(dto.Id, 1, (_, n) => n + 1);
        var gen = _enqueuedGen.AddOrUpdate(dto.Id, 1, (_, g) => g + 1);
        var item = new Queued(dto, gen);
        if (_queue.Writer.TryWrite(item)) return gen;
        // La cola ya se cerró al apagar: nadie lo va a escribir.
        Done(item, written: false);
        LogLost(dto, "encolado después del apagado");
        return gen;
    }

    /// <summary>
    /// Generación más alta de este personaje que ya está en la BD (0 = ninguna en esta ejecución). HelloGate la lee antes de
    /// cargar: si el tick tiene en memoria un guardado más nuevo, la BD que leyó está atrasada (HU-015 CA5).
    /// </summary>
    public long WrittenGeneration(Guid characterId) => _writtenGen.GetValueOrDefault(characterId);

    /// <summary>¿Queda algún guardado de este personaje sin escribir?</summary>
    public bool HasPending(Guid characterId) => _pendingByCharacter.TryGetValue(characterId, out var n) && n > 0;

    /// <summary>Entradas de auditoría que esperan al siguiente guardado de este personaje.</summary>
    public int UnwrittenAuditCount(Guid characterId) => _unwrittenAudit.TryGetValue(characterId, out var a) ? a.Count : 0;

    /// <summary>
    /// Espera (fuera del tick) a que se escriban los guardados encolados de un personaje, como mucho `timeout`: así quien sale
    /// a la selección y vuelve a entrar enseguida lee de la BD el estado con el que salió (HU-015 CA5).
    /// </summary>
    public async Task WaitForCharacterAsync(Guid characterId, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (HasPending(characterId) && Environment.TickCount64 < deadline) await Task.Delay(20, ct);
    }

    private void Done(Queued item, bool written)
    {
        if (written) _writtenGen.AddOrUpdate(item.Dto.Id, item.Gen, (_, g) => Math.Max(g, item.Gen));
        Interlocked.Decrement(ref _pending);
        _pendingByCharacter.AddOrUpdate(item.Dto.Id, 0, (_, n) => Math.Max(0, n - 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                Done(item, await WriteAsync(item.Dto, CancellationToken.None));
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
        Queued? current = null;
        try
        {
            // El plazo se mira antes de sacar el siguiente: un DTO leído nunca se descarta sin guardarlo o loguearlo.
            while (!cts.IsCancellationRequested && _queue.Reader.TryRead(out var next))
            {
                current = next;
                Done(next, await WriteAsync(next.Dto, cts.Token));
                current = null;
                drained++;
            }
        }
        catch (Exception) when (cts.IsCancellationRequested) { } // con el token cortado el driver puede lanzar algo distinto de OCE
        finally
        {
            if (drained > 0) logger.LogInformation("Guardados {Count} personajes al apagar", drained);
            if (current is { } cur)
            {
                // Puede que la BD sí lo confirmara antes de reaccionar al token: Failed sobra como mucho, y el DTO queda en el log.
                Done(cur, written: false);
                LogLost(cur.Dto, "plazo de apagado agotado durante el guardado");
            }
            LogRemaining("plazo de apagado agotado");
        }
    }

    /// <summary>Saca y loguea lo que quede en la cola, y la auditoría que esperaba otro guardado; solo cuando ya no hay otro lector.</summary>
    private void LogRemaining(string reason)
    {
        while (_queue.Reader.TryRead(out var left))
        {
            Done(left, written: false);
            LogLost(left.Dto, reason);
        }
        foreach (var characterId in _unwrittenAudit.Keys)
            if (_unwrittenAudit.TryRemove(characterId, out var audit))
                logger.LogError("Auditoría del personaje {CharacterId} no escrita ({Reason}): {Audit}", characterId, reason, System.Text.Json.JsonSerializer.Serialize(audit));
    }

    private void LogLost(CharacterSaveDto dto, string reason)
    {
        Interlocked.Increment(ref _failed);
        logger.LogError("Guardado de {Name} no escrito ({Reason}). DTO: {Dto}", dto.Name, reason, System.Text.Json.JsonSerializer.Serialize(dto));
    }

    /// <summary>Guarda un DTO de la cola sumándole la auditoría de guardados fallidos anteriores; si falla, la conserva para el siguiente.</summary>
    private async Task<bool> WriteAsync(CharacterSaveDto dto, CancellationToken ct)
    {
        if (_unwrittenAudit.TryRemove(dto.Id, out var carried)) dto = dto with { Audit = [.. carried, .. dto.Audit] };
        var written = false;
        try { written = await SaveWithRetryAsync(dto, ct); }
        finally
        {
            if (!written && dto.Audit.Count > 0) _unwrittenAudit[dto.Id] = dto.Audit;
        }
        return written;
    }

    /// <summary>Guarda de inmediato (uso en tests y en el apagado). Devuelve si quedó escrito.</summary>
    public async Task<bool> SaveWithRetryAsync(CharacterSaveDto dto, CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(200);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await characters.SaveAsync(dto, ct);
                Interlocked.Increment(ref _saved);
                return true;
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
                return false;
            }
        }
        return false;
    }
}
