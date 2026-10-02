using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Net;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-015: `CloseAfterFlush` cierra cuando lo encolado ya salió, y a los 2 s aunque el cliente no lea nada.</summary>
public sealed class CloseAfterFlushTests
{
    /// <summary>Socket de un cliente que no lee: cada envío se queda colgado hasta que se cancela. Recibe el Close cuando se lo mandan.</summary>
    private sealed class StuckClientSocket(bool stuckSends) : WebSocket
    {
        private readonly TaskCompletionSource _closeSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebSocketState _state = WebSocketState.Open;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public TimeSpan? ClosedAt { get; private set; }
        public string? CloseDescription { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => CloseDescription;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            ClosedAt ??= _clock.Elapsed;
            CloseDescription = statusDescription;
            _state = WebSocketState.CloseSent;
            _closeSent.TrySetResult();
            return Task.CompletedTask;
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await _closeSent.Task.WaitAsync(cancellationToken); // el cliente contesta al Close del servidor
            _state = WebSocketState.Closed;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            stuckSends ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;

        public override void Dispose() { }
    }

    private static (WebSocketSession Session, Channel<InboundMessage> Inbound) Session(WebSocket socket)
    {
        var inbound = Channel.CreateUnbounded<InboundMessage>();
        return (new WebSocketSession(1, socket, inbound.Writer, TimeSpan.FromMinutes(1), NullLogger.Instance), inbound);
    }

    [Fact]
    public async Task ClientThatStopsReading_IsClosedAnyway_AfterTheFlushTimeout()
    {
        var socket = new StuckClientSocket(stuckSends: true);
        var (session, inbound) = Session(socket);
        using (session)
        {
            var run = session.RunAsync(TestContext.Current.CancellationToken);
            session.Send(new LoggedOut()); // el envío se queda colgado: la marca de fin nunca llega al bucle de escritura
            session.CloseAfterFlush("logout");

            await run.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            socket.ClosedAt.ShouldNotBeNull();
            socket.ClosedAt.Value.ShouldBeGreaterThanOrEqualTo(WebSocketSession.FlushCloseTimeout - TimeSpan.FromMilliseconds(100));
            socket.ClosedAt.Value.ShouldBeLessThan(WebSocketSession.FlushCloseTimeout + TimeSpan.FromSeconds(1.5));
            socket.CloseDescription.ShouldBe("logout");
            var disconnected = await DisconnectedOf(inbound);
            disconnected.Attachment.ShouldBe("logout");
        }
    }

    [Fact]
    public async Task ClientThatReads_IsClosedAsSoonAsTheQueueIsFlushed()
    {
        var socket = new StuckClientSocket(stuckSends: false);
        var (session, _) = Session(socket);
        using (session)
        {
            var run = session.RunAsync(TestContext.Current.CancellationToken);
            session.Send(new LoggedOut());
            session.CloseAfterFlush("logout");

            await run.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            socket.ClosedAt.ShouldNotBeNull();
            socket.ClosedAt.Value.ShouldBeLessThan(TimeSpan.FromSeconds(1), "no espera el plazo si la cola ya salió");
        }
    }

    private static async Task<InboundMessage> DisconnectedOf(Channel<InboundMessage> inbound)
    {
        await foreach (var m in inbound.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
            if (m.Kind == InboundKind.Disconnected) return m;
        throw new InvalidOperationException("sin Disconnected");
    }
}
