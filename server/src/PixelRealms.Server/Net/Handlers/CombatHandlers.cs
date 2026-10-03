using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>Contexto común de los handlers de combate: mundo y módulo de combate (hilo del tick).</summary>
public sealed class CombatHandlerDeps(World world, CombatModule combat)
{
    public World World { get; } = world;

    public CombatModule Combat { get; } = combat;

    public Game.Map.MapInstance? MapOf(Game.Entities.Player p) => World.GetInstance(p.MapInstanceId);
}

/// <summary>HU-030 CA4: el servidor guarda el objetivo seleccionado (lo usan otros jugadores como "objetivo de mi objetivo").
/// Solo entidades de su mapa: un id inventado no se guarda ni se reenvía en `EntState.tgt`.</summary>
public sealed class SelectTargetHandler(CombatHandlerDeps deps) : IMessageHandler<SelectTarget>
{
    public void Handle(SelectTarget msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null) return;
        var exists = msg.TargetId is { } id && id > 0 && deps.MapOf(player)?.Find(new EntityId(id)) is not null;
        player.Combat.TargetId = exists ? new EntityId(msg.TargetId!.Value) : null;
        if (player.Combat.TargetId is null) player.Combat.AutoAttackOn = false;
    }
}

/// <summary>HU-033: `CastSpell{spellId, targetId?, targetPos?, reqId?}` → CastSystem; errores con los códigos del protocolo.</summary>
public sealed class CastSpellHandler(CombatHandlerDeps deps) : IMessageHandler<CastSpell>
{
    public void Handle(CastSpell msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null) return;
        if (string.IsNullOrEmpty(msg.SpellId) || !deps.Combat.Services.Content.TryGetSpell(msg.SpellId, out var spell) || spell is null)
        {
            ctx.SendError(ErrorCodes.NotFound, msg.ReqId);
            return;
        }
        // ADR-014: se lanzan los hechizos equipados en las casillas de hechizo de la barra, no cualquiera de los conocidos (los de
        // objeto y monstruo los rechaza TryBeginCast).
        if (spell.Source == Content.Defs.SpellSource.Class && !IsEquipped(player, spell.Id, deps.Combat.Services.Content.Rules.Loadout.SpellSlots))
        {
            ctx.SendError(ErrorCodes.NotEquipped, msg.ReqId, "Ese hechizo no está en tu barra");
            return;
        }
        var map = deps.MapOf(player);
        if (map is null) return;
        var targetId = msg.TargetId is { } t ? new EntityId(t) : player.Combat.TargetId;
        Vec2? pos = msg.TargetPos is { } p ? new Vec2(p.X / GameConstants.PixelsPerTile, p.Y / GameConstants.PixelsPerTile) : null;
        var error = deps.Combat.Casts.TryBeginCast(player, spell, targetId, pos, map, ctx.Tick);
        if (error is not null) ctx.SendError(error, msg.ReqId, MessageFor(error, spell));
    }

    private static bool IsEquipped(Game.Entities.Player p, string spellId, int spellSlots)
    {
        for (var i = 0; i < Math.Min(spellSlots, p.Hotbar.Length); i++)
            if (p.Hotbar[i] is { Kind: "spell" } slot && slot.Ref == spellId) return true;
        return false;
    }

    private static string? MessageFor(string code, Content.Defs.SpellDef spell) => code switch
    {
        CastErrors.NotEnoughResource when spell.Cost is { } c => c.Resource switch
        {
            Content.Defs.Resource.Mana => "No tienes suficiente maná",
            Content.Defs.Resource.Rage => "No tienes suficiente ira",
            _ => "No tienes suficiente energía",
        },
        CastErrors.OutOfRange => "Fuera de alcance",
        CastErrors.NoLos => "Sin línea de visión",
        CastErrors.InvalidTarget => "Objetivo no válido",
        _ => null,
    };
}

public sealed class CancelCastHandler(CombatHandlerDeps deps) : IMessageHandler<CancelCast>
{
    public void Handle(CancelCast msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null || deps.MapOf(player) is not { } map) return;
        deps.Combat.Casts.Cancel(player, map, ctx.Tick);
    }
}

/// <summary>HU-032 CA1: `AutoAttack{on}` sobre el objetivo seleccionado; el alcance lo comprueba el sistema cada tick.</summary>
public sealed class AutoAttackHandler(CombatHandlerDeps deps) : IMessageHandler<AutoAttack>
{
    public void Handle(AutoAttack msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null || deps.MapOf(player) is not { } map) return;
        if (!msg.On) { player.Combat.AutoAttackOn = false; return; }
        if (player.IsDead) { ctx.SendError(ErrorCodes.IsDead); return; }
        var target = player.Combat.TargetId is { } id ? map.Find(id) : null;
        // HU-064 CA6: a otro jugador solo en duelo.
        if (target is Game.Entities.Player && !ReferenceEquals(target, player) && !target.IsDead && !deps.Combat.Services.IsEnemy(player, target)) { ctx.SendError(ErrorCodes.PvpNotAllowed); return; }
        if (target is null || target.IsDead || !deps.Combat.Services.IsEnemy(player, target)) { ctx.SendError(ErrorCodes.InvalidTarget); return; }
        if (deps.Combat.AutoAttack.SwingMs(player) is null) { ctx.SendError(ErrorCodes.InvalidTarget, message: "Necesitas un arma"); return; }
        player.Combat.AutoAttackOn = true;
    }
}

/// <summary>HU-037 CA2: `Respawn` → cementerio más cercano con respawnHpPct / respawnResourcePct.</summary>
public sealed class RespawnHandler(CombatHandlerDeps deps) : IMessageHandler<Respawn>
{
    public void Handle(Respawn msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null || deps.MapOf(player) is not { } map) return;
        if (!deps.Combat.Death.Respawn(player, map, ctx.Tick)) ctx.SendError(ErrorCodes.InvalidPayload, message: "No estás muerto");
    }
}
