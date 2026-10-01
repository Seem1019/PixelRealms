using PixelRealms.Content.Defs;

namespace PixelRealms.Content.Validation;

/// <summary>
/// ADR-023: funciones del motor ya implementadas. Un hechizo que use algo que falte se carga como <b>no disponible</b>
/// (no se aprende ni se equipa) y el validador avisa sin fallar. Cada HU que implementa una función la añade aquí
/// (HU-086 el cono y la línea cuando un hechizo los use).
/// </summary>
public static class EngineCapabilities
{
    public static readonly HashSet<string> Shapes = new(StringComparer.Ordinal) { "circle" };

    public static readonly HashSet<string> Targetings = new(StringComparer.Ordinal)
        { "self", "enemy", "ally", "self_aoe_enemies", "self_aoe_allies", "ground_aoe_enemies", "ground_aoe_allies", "ground_aoe_all" };

    public static readonly HashSet<string> Effects = new(StringComparer.Ordinal)
        { "damage", "heal", "restore_resource", "apply_aura", "taunt", "dash", "interrupt", "leap" };

    /// <summary>Motivo por el que el hechizo no está disponible, o null si lo está.</summary>
    public static string? UnavailableReason(SpellDef spell)
    {
        if (spell.Targeting.IsArea() && !Shapes.Contains(ContentJson.EnumName(spell.Shape)))
            return $"forma '{ContentJson.EnumName(spell.Shape)}' no implementada";
        if (!Targetings.Contains(ContentJson.EnumName(spell.Targeting)))
            return $"targeting '{ContentJson.EnumName(spell.Targeting)}' no implementado";
        foreach (var e in spell.Effects)
            if (!Effects.Contains(ContentJson.EnumName(e.Type)))
                return $"efecto '{ContentJson.EnumName(e.Type)}' no implementado";
        return null;
    }
}
