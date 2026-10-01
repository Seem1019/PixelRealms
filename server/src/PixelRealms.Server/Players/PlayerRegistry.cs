using PixelRealms.Game.Entities;

namespace PixelRealms.Server.Players;

/// <summary>Jugadores en el mundo por conexión y por personaje. Solo lo toca el tick.</summary>
public sealed class PlayerRegistry
{
    private readonly Dictionary<int, Player> _byConnection = new();
    private readonly Dictionary<Guid, Player> _byCharacter = new();
    private readonly Dictionary<Guid, Player> _byAccount = new();

    public IReadOnlyCollection<Player> All => _byCharacter.Values;

    public int Count => _byCharacter.Count;

    public Player? ByConnection(int connectionId) => _byConnection.GetValueOrDefault(connectionId);

    public Player? ByCharacter(Guid characterId) => _byCharacter.GetValueOrDefault(characterId);

    public Player? ByAccount(Guid accountId) => _byAccount.GetValueOrDefault(accountId);

    public Player? ByName(string name) => _byCharacter.Values.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Add(Player player, int connectionId)
    {
        player.ConnectionId = connectionId;
        _byConnection[connectionId] = player;
        _byCharacter[player.CharacterId] = player;
        _byAccount[player.AccountId] = player;
    }

    /// <summary>La conexión se fue pero el jugador sigue en el mundo (linkdead, HU-025).</summary>
    public void Detach(int connectionId)
    {
        if (_byConnection.Remove(connectionId, out var p) && p.ConnectionId == connectionId) p.ConnectionId = -1;
    }

    /// <summary>Reconexión: una conexión nueva toma el control del mismo personaje.</summary>
    public void Attach(Player player, int connectionId)
    {
        if (player.ConnectionId >= 0) _byConnection.Remove(player.ConnectionId);
        player.ConnectionId = connectionId;
        _byConnection[connectionId] = player;
    }

    public void Remove(Player player)
    {
        if (player.ConnectionId >= 0) _byConnection.Remove(player.ConnectionId);
        _byCharacter.Remove(player.CharacterId);
        _byAccount.Remove(player.AccountId);
        player.ConnectionId = -1;
    }
}
