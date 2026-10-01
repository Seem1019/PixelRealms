using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Net;

/// <summary>Resultado de procesar un Hello fuera del tick: o un adjunto para encolar PlayerJoin, o un código de error (y se cierra).</summary>
public readonly record struct HelloResult(object? JoinAttachment, string? ErrorCode)
{
    public static HelloResult Ok(object attachment) => new(attachment, null);

    public static HelloResult Fail(string code) => new(null, code);
}

/// <summary>Valida versión y ticket y carga el personaje de BD en la tarea de la conexión, antes de encolar al tick.</summary>
public interface IHelloGate
{
    Task<HelloResult> ProcessAsync(Hello hello, WebSocketSession session, CancellationToken ct);
}
