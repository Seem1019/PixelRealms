using PixelRealms.Game.Core;
using PixelRealms.Game.Interest;

namespace PixelRealms.Server.Net;

/// <summary>Traduce los eventos del tick a mensajes para los observadores (skill dotnet-server: la emisión S→C sale de aquí, nunca de los sistemas).</summary>
public sealed class EventDispatcher(ConnectionManager connections)
{
    public void OnPostTick(TickContext ctx)
    {
        foreach (var e in ctx.Events)
        {
            switch (e)
            {
                case EntityEnteredView v when v.Observer.ConnectionId >= 0:
                    connections.Send(v.Observer.ConnectionId, SnapshotBuilder.ToSpawn(v.Entity));
                    break;
                case EntityLeftView l when l.Observer.ConnectionId >= 0:
                    connections.Send(l.Observer.ConnectionId, new Protocol.Messages.EntityDespawn(l.EntityId.Value, l.Reason));
                    break;
                default:
                    break;
            }
        }
    }
}
