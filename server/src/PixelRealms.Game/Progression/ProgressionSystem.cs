using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Progression;

public sealed record XpGainedEvent(int MapInstanceId, Player Player, int Amount, EntityId? SourceId) : IGameEvent;

public sealed record RankUp(string SpellId, int Rank);

/// <param name="UpgradesUnlocked">HU-104: hechizos cuya mejora se puede elegir desde este nivel.</param>
public sealed record LevelUpEvent(int MapInstanceId, Player Player, int Level, IReadOnlyList<string> NewSpells, IReadOnlyList<RankUp> RankUps,
    IReadOnlyList<string>? UpgradesUnlocked = null) : IGameEvent;

/// <summary>Stats del jugador cambiaron (nivel, equipo, auras con stats): el servidor envía StatsUpdate.</summary>
public sealed record StatsChangedEvent(int MapInstanceId, Player Player) : IGameEvent;

/// <summary>`/level` hacia abajo: el jugador olvidó hechizos y la barra perdió casillas; el cliente necesita su lista y su barra
/// otra vez (el servidor reenvía Welcome, como al cambiar de clase).</summary>
public sealed record KnownSpellsResetEvent(int MapInstanceId, Player Player) : IGameEvent;

/// <summary>
/// HU-040/HU-041: XP al matar (al jugador que taggeó al monstruo; grupos en HU-062), tope de nivel de la fase, subida de nivel
/// con sobrante (varios niveles de golpe), stats +statsPerLevel vía StatCalculator, vida y recurso llenos, hechizos nuevos de
/// `spellUnlockLevels` y rangos de `spellRankLevels` (ADR-024). Corre después de la muerte en el tick y lee los ActorDiedEvent.
/// </summary>
public sealed class ProgressionSystem(CombatServices services) : IMapSystem
{
    public string Name => "progression";

    /// <summary>XP por receptor (HU-062 reparte en grupo). Por defecto: solo quien taggeó, con la XP en solitario.</summary>
    public Func<Player, Monster, MapInstance, IRules, List<(Player Player, int Xp)>> XpRecipients { get; set; } =
        static (tagger, monster, _, rules) => [(tagger, XpCurve.SoloKillXp(rules.Progression, monster.Template, tagger.Level))];

    public void Tick(MapInstance map, TickContext ctx)
    {
        var count = ctx.Events.Count;
        for (var i = 0; i < count; i++)
        {
            if (ctx.Events[i] is not ActorDiedEvent { Victim: Monster monster } died || died.MapInstanceId != map.Id) continue;
            var taggerId = monster.TaggedBy ?? died.Killer?.Id;
            if (taggerId is null || map.Find(taggerId.Value) is not Player tagger) continue;
            foreach (var (player, xp) in XpRecipients(tagger, monster, map, ctx.Rules))
                GrantXp(player, xp, monster.Id, map, ctx);
        }
    }

    /// <summary>Suma XP (0 si está en el tope de la fase) y sube los niveles que correspondan.</summary>
    public void GrantXp(Player player, int amount, EntityId? sourceId, MapInstance map, TickContext ctx)
    {
        var p = ctx.Rules.Progression;
        var cap = ctx.Rules.CurrentLevelCap;
        if (player.Level >= cap || amount <= 0) return;
        player.Xp += amount;
        player.Dirty = true;
        ctx.Emit(new XpGainedEvent(map.Id, player, amount, sourceId));
        var leveled = false;
        while (player.Level < cap)
        {
            var need = XpCurve.XpToNextLevel(p, player.Level);
            if (need <= 0 || player.Xp < need) break;
            player.Xp -= need;
            LevelUp(player, map, ctx);
            leveled = true;
        }
        if (player.Level >= cap) player.Xp = 0; // CA4: en el tope no se acumula
        if (leveled) ctx.Emit(new StatsChangedEvent(map.Id, player));
    }

    /// <summary>HU-070 `/level n`: fija el nivel (acotado a [1, tope de fase]); subir pasa por LevelUp (hechizos/rangos), bajar quita los hechizos que ya no se cumplen.</summary>
    public int SetLevel(Player player, int level, MapInstance map, TickContext ctx)
    {
        var target = Math.Clamp(level, 1, ctx.Rules.CurrentLevelCap);
        while (player.Level < target) LevelUp(player, map, ctx);
        if (player.Level > target)
        {
            player.Level = target;
            var db = services.Content;
            player.KnownSpells.RemoveAll(id => db.TryGetSpell(id, out var s) && s is not null && s.LevelReq > target);
            SpellUpgradeRules.Prune(player, db, ctx.Rules.Progression); // HU-104 CA6: por debajo del nivel 8 no queda ninguna
            for (var i = 0; i < player.Hotbar.Length; i++)
                if (player.Hotbar[i] is { Kind: "spell" } slot && !player.KnownSpells.Contains(slot.Ref)) player.Hotbar[i] = null;
            if (player.Combat.Cast is { } cast)
            {
                // Puede ser un hechizo que acaba de olvidar; el Welcome renovado borra además la barra de casteo del cliente.
                player.Combat.Cast = null;
                ctx.Emit(new CastEndedEvent(map.Id, player, cast.Spell, CastResults.Cancelled, null));
            }
            services.Recalculate(player);
            player.Hp = player.MaxHp;
            player.Resource = player.MaxResource;
            // Sin esto el cliente seguía mostrando en la barra hechizos que el servidor ya había quitado y, al volver a subir, no
            // los colocaba otra vez: "Ese hechizo no está en tu barra".
            ctx.Emit(new KnownSpellsResetEvent(map.Id, player));
        }
        player.Xp = 0;
        player.Dirty = true;
        ctx.Emit(new StatsChangedEvent(map.Id, player));
        return player.Level;
    }

    private void LevelUp(Player player, MapInstance map, TickContext ctx)
    {
        var p = ctx.Rules.Progression;
        player.Level++;
        var db = services.Content;
        var newSpells = new List<string>();
        foreach (var spell in db.ClassSpells(player.ClassId))
        {
            if (spell.LevelReq != player.Level || !db.IsSpellAvailable(spell.Id) || player.KnownSpells.Contains(spell.Id)) continue;
            player.KnownSpells.Add(spell.Id);
            newSpells.Add(spell.Id);
        }
        var rankUps = new List<RankUp>();
        if (SpellRanks.IsRankLevel(p, player.Level))
        {
            var rank = SpellRanks.RankAt(p, player.Level);
            foreach (var id in player.KnownSpells)
                if (!newSpells.Contains(id)) rankUps.Add(new RankUp(id, rank));
        }
        services.Recalculate(player);
        player.Hp = player.MaxHp;
        player.Resource = player.MaxResource;
        player.Dirty = true;
        ctx.Emit(new LevelUpEvent(map.Id, player, player.Level, newSpells, rankUps, SpellUpgradeRules.UnlockedAt(player, newSpells, db, p)));
    }
}
