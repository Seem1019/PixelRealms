using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Social;

public sealed class PartyMember(Guid characterId, string name, string classId)
{
    public Guid CharacterId { get; } = characterId;
    public string Name { get; set; } = name;
    public string ClassId { get; set; } = classId;
    public bool Online { get; set; } = true;
    /// <summary>Desde cuándo está desconectado (ms de juego); long.MinValue si está en línea.</summary>
    public long OfflineSinceMs { get; set; } = long.MinValue;
}

public sealed class Party(int id, Guid leader)
{
    public int Id { get; } = id;
    public Guid Leader { get; set; } = leader;
    public List<PartyMember> Members { get; } = new(5);

    public PartyMember? Find(Guid characterId) => Members.FirstOrDefault(m => m.CharacterId == characterId);
    public bool Contains(Guid characterId) => Find(characterId) is not null;
}

public sealed record PartyInviteState(Guid From, string FromName, Guid To, long ExpiresAtMs);

/// <summary>Un grupo cambió (miembros, líder, conexión): el servidor envía PartyUpdate a todos sus miembros.</summary>
public sealed record PartyChangedEvent(int MapInstanceId, Party Party, string Reason) : IGameEvent;

/// <summary>Alguien recibió una invitación (el servidor se la muestra con un PartyUpdate{requested}-like o chat de sistema).</summary>
public sealed record PartyInvitedEvent(int MapInstanceId, Player Target, PartyInviteState Invite) : IGameEvent;

/// <summary>Un grupo se disolvió (quedó 1): avisar al que queda.</summary>
public sealed record PartyDisbandedEvent(int MapInstanceId, Guid LastMember) : IGameEvent;

/// <summary>
/// HU-061: invitaciones con caducidad (`inviteExpireSec`), grupos de hasta `maxMembers`, líder hereda al siguiente, se disuelve
/// con 1, desconectados `offlineGraceSec` como "desconectado" y luego fuera. Por personaje (Guid), no por conexión. Puro.
/// </summary>
public sealed class PartyService
{
    private readonly Dictionary<Guid, Party> _byMember = new();
    private readonly Dictionary<Guid, PartyInviteState> _invitesByTarget = new();
    private int _nextId = 1;

    public Party? PartyOf(Guid characterId) => _byMember.GetValueOrDefault(characterId);

    public PartyInviteState? PendingInvite(Guid characterId) => _invitesByTarget.GetValueOrDefault(characterId);

    public IEnumerable<Party> All => _byMember.Values.Distinct();

    /// <summary>Invita; errores: "duel_busy"-like no aplica; devuelve código o null.</summary>
    public string? Invite(Player from, Player to, long nowMs, GroupRules rules)
    {
        if (ReferenceEquals(from, to)) return "invalid_target";
        var party = PartyOf(from.CharacterId);
        if (party is not null && party.Leader != from.CharacterId) return "forbidden";
        if (party is not null && party.Members.Count >= rules.MaxMembers) return "forbidden";
        if (PartyOf(to.CharacterId) is not null) return "invalid_target";
        _invitesByTarget[to.CharacterId] = new PartyInviteState(from.CharacterId, from.Name, to.CharacterId, nowMs + (long)(rules.InviteExpireSec * 1000));
        return null;
    }

    /// <summary>Responde a la invitación pendiente; al aceptar crea el grupo (el que invitó es líder) o se une.</summary>
    public (Party? Party, string? Error) Respond(Player target, bool accept, Func<Guid, Player?> findPlayer, long nowMs, GroupRules rules)
    {
        if (!_invitesByTarget.Remove(target.CharacterId, out var invite)) return (null, "not_found");
        if (!accept) return (null, null);
        if (nowMs > invite.ExpiresAtMs) return (null, "not_found");
        var inviter = findPlayer(invite.From);
        if (inviter is null) return (null, "not_found");
        // Todo se valida antes de crear nada: si no, un rechazo dejaba al que invitó en un grupo de 1 que no podía recibir
        // invitaciones, y si el que invitó se había unido a otro grupo, el invitado entraba sin permiso de ese líder.
        if (PartyOf(target.CharacterId) is not null) return (null, "invalid_target");
        var party = PartyOf(invite.From);
        if (party is not null && party.Leader != invite.From) return (null, "forbidden");
        if (party is not null && party.Members.Count >= rules.MaxMembers) return (null, "forbidden");
        if (party is null)
        {
            party = new Party(_nextId++, invite.From);
            party.Members.Add(new PartyMember(inviter.CharacterId, inviter.Name, inviter.ClassId));
            _byMember[inviter.CharacterId] = party;
        }
        party.Members.Add(new PartyMember(target.CharacterId, target.Name, target.ClassId));
        _byMember[target.CharacterId] = party;
        return (party, null);
    }

    /// <summary>Sale del grupo; devuelve el grupo si sigue existiendo (y hubo cambio) o null si se disolvió / no estaba.</summary>
    public (Party? Party, bool Disbanded, Guid? LastMember) Leave(Guid characterId)
    {
        var party = PartyOf(characterId);
        if (party is null) return (null, false, null);
        party.Members.RemoveAll(m => m.CharacterId == characterId);
        _byMember.Remove(characterId);
        if (party.Members.Count <= 1)
        {
            Guid? last = party.Members.Count == 1 ? party.Members[0].CharacterId : null;
            foreach (var m in party.Members) _byMember.Remove(m.CharacterId);
            party.Members.Clear();
            return (null, true, last);
        }
        if (party.Leader == characterId) party.Leader = party.Members[0].CharacterId;
        return (party, false, null);
    }

    public string? Kick(Guid leader, string targetName, out Party? party, out Guid? kicked)
    {
        kicked = null;
        party = PartyOf(leader);
        if (party is null) return "not_found";
        if (party.Leader != leader) return "forbidden";
        var member = party.Members.FirstOrDefault(m => string.Equals(m.Name, targetName, StringComparison.OrdinalIgnoreCase));
        if (member is null || member.CharacterId == leader) return "not_found";
        kicked = member.CharacterId;
        var (after, _, _) = Leave(member.CharacterId);
        party = after;
        return null;
    }

    public Party? SetOnline(Guid characterId, bool online, long nowMs)
    {
        var party = PartyOf(characterId);
        if (party?.Find(characterId) is not { } m) return null;
        m.Online = online;
        m.OfflineSinceMs = online ? long.MinValue : nowMs;
        return party;
    }

    /// <summary>Caducidad de invitaciones y expulsión de desconectados tras `offlineGraceSec` (HU-061 CA5).</summary>
    public List<(Party? Party, Guid Removed, bool Disbanded, Guid? Last)> Tick(long nowMs, GroupRules rules)
    {
        foreach (var key in _invitesByTarget.Where(kv => kv.Value.ExpiresAtMs < nowMs).Select(kv => kv.Key).ToList()) _invitesByTarget.Remove(key);
        var changes = new List<(Party?, Guid, bool, Guid?)>();
        var graceMs = (long)(rules.OfflineGraceSec * 1000);
        foreach (var party in All.ToList())
            foreach (var m in party.Members.Where(m => !m.Online && nowMs - m.OfflineSinceMs >= graceMs).ToList())
            {
                var (after, disbanded, last) = Leave(m.CharacterId);
                changes.Add((after, m.CharacterId, disbanded, last));
            }
        return changes;
    }
}

/// <summary>Reparto de XP en grupo (gdd.md §Progresión, HU-062 CA2): activos, referencia = nivel máximo, pesos por brecha, bono por tamaño.</summary>
public static class GroupXp
{
    public static double Weight(GroupRules g, int maxLevel, int level) =>
        Math.Max(g.LevelGapMinWeight, Math.Pow(g.LevelGapDecay, Math.Max(0, (maxLevel - level) - g.LevelGapFreeLevels)));

    /// <summary>XP por miembro activo (sin redondear). `activeLevels` ya filtrados (vivos, en rango, activos).</summary>
    public static List<double> Split(ProgressionRules p, GroupRules g, MonsterTemplate monster, IReadOnlyList<int> activeLevels)
    {
        if (activeLevels.Count == 0) return [];
        var maxLevel = activeLevels.Max();
        var baseXp = Progression.XpCurve.MonsterXp(p, monster.Level, monster.Type);
        var mod = Progression.XpCurve.LevelDiffModifier(p, monster.Level, maxLevel);
        var n = Math.Min(activeLevels.Count, g.BonusBySize.Count);
        var pool = baseXp * mod * g.BonusBySize[n - 1] * p.XpRate;
        var weights = activeLevels.Select(l => Weight(g, maxLevel, l)).ToList();
        var sum = weights.Sum();
        return weights.Select(w => pool * w / sum).ToList();
    }
}
