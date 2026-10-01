using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Progression;

namespace PixelRealms.Game.Combat;

/// <summary>Arma equipada resuelta para el ataque básico (ADR-019): tipo, alcance, velocidad, tirada y escuela.</summary>
public readonly record struct WeaponInfo(ItemTemplate Template, WeaponTypeRules TypeRules, Stat Scaling, double Affinity)
{
    public School School => Scaling == Stat.Int ? School.Magic : School.Physical;

    public double RangeTiles => TypeRules.RangeTiles;

    public int SpeedMs => Template.SpeedMs;

    public double AverageRoll => (Template.DamageMin + Template.DamageMax) / 2.0;
}

/// <summary>
/// Servicios compartidos por los sistemas de combate: contenido vigente, stats derivados cacheados, relación entre actores
/// (enemigo/aliado) y arma equipada. Sin IO; el contenido llega por delegado para soportar recarga en caliente.
/// </summary>
public sealed class CombatServices(Func<ContentDb> content)
{
    public ContentDb Content => content();

    /// <summary>¿Puede `a` atacar al jugador `b`? Lo rellena PvpService (duelos, HU-064). Por defecto, nunca.</summary>
    public Func<Player, Player, bool> PvpCanAttack { get; set; } = static (_, _) => false;

    /// <summary>¿Está en un duelo activo? Los duelistas no atacan monstruos ni terceros y los monstruos los ignoran (HU-064 CA5).</summary>
    public Func<Player, bool> InDuel { get; set; } = static _ => false;

    public DerivedStats StatsOf(Actor actor)
    {
        if (actor.Combat.Stats is { } cached) return cached;
        var db = Content;
        DerivedStats stats;
        switch (actor)
        {
            case Player p:
            {
                var cls = db.Class(p.ClassId);
                var equipped = new List<ItemTemplate>(Items.Equipment.SlotCount);
                foreach (var slot in p.Equipment.Slots) if (slot is not null) equipped.Add(db.Item(slot.TemplateId));
                stats = StatCalculator.Derive(cls, p.Level, db.Rules, equipped, PrimaryStats.From(p.Auras.StatMods()));
                break;
            }
            case Monster m:
            {
                // combat.md §Daño físico: los monstruos usan la misma fórmula con attackPower = 0; crítico fijo (critBase); sin esquiva.
                var c = db.Rules.Combat;
                stats = new DerivedStats(default, m.Template.Hp, 0, 0, 0, c.CritBase, c.CritBase, 0, m.Template.Armor, 1.0, 0, 0, 1.0);
                break;
            }
            default:
                stats = new DerivedStats(default, actor.MaxHp, 0, 0, 0, 0, 0, 0, 0, 1.0, 0, 0, 1.0);
                break;
        }
        actor.Combat.Stats = stats;
        return stats;
    }

    /// <summary>Aplica los máximos derivados al actor (vida/recurso) tras un cambio de equipo, nivel o aura con stats.</summary>
    public DerivedStats Recalculate(Player p)
    {
        p.MarkStatsDirty();
        var d = StatsOf(p);
        var cls = Content.Class(p.ClassId);
        p.MaxHp = d.MaxHp;
        p.MaxResource = StatCalculator.MaxResource(cls, d, Content.Rules);
        p.Hp = Math.Min(p.Hp, p.MaxHp);
        p.Resource = Math.Min(p.Resource, p.MaxResource);
        return d;
    }

    public Resource ResourceOf(Player p) => Content.Class(p.ClassId).Resource;

    /// <summary>Arma del jugador o null si no lleva (sin arma no hay ataque básico: decisión provisional, ver fase-1.md).</summary>
    public WeaponInfo? WeaponOf(Player p)
    {
        var main = p.Equipment.MainHand;
        if (main is null) return null;
        var tpl = Content.Item(main.TemplateId);
        if (tpl.WeaponType is not { } wt) return null;
        var typeName = ContentJson.EnumName(wt);
        var rules = Content.Rules;
        if (!rules.Weapons.Types.TryGetValue(typeName, out var typeRules)) return null;
        var scaling = tpl.Scaling ?? (rules.Affinity.WeaponScaling.TryGetValue(typeName, out var s) ? s : Stat.Str);
        return new WeaponInfo(tpl, typeRules, scaling, rules.Affinity.MultiplierFor(p.ClassId, tpl.AffinityType));
    }

    /// <summary>Enemigos: jugador ↔ monstruo siempre; jugador ↔ jugador solo si el PvP lo permite (duelo). Nunca uno mismo.</summary>
    public bool IsEnemy(Actor a, Actor b)
    {
        if (ReferenceEquals(a, b)) return false;
        if (a is Player pa && b is Player pb) return PvpCanAttack(pa, pb);
        if (a is Player da && InDuel(da)) return false;
        if (b is Player db2 && InDuel(db2)) return false;
        return a.Kind != b.Kind && (a is Player || b is Player);
    }

    /// <summary>Aliados: mismo bando (incluye a uno mismo). Los NPC no son aliados de nadie.</summary>
    public bool IsAlly(Actor a, Actor b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is Player pa && b is Player pb) return !PvpCanAttack(pa, pb);
        return a is Monster && b is Monster;
    }
}
