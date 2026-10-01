using PixelRealms.Game.Core;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Ai;

public enum AiState { Idle, Chase, Attack, Evade }

/// <summary>Estado de IA de un monstruo (combat.md §Monstruos). Lo muta solo <see cref="MonsterAiSystem"/>.</summary>
public sealed class MonsterBrain
{
    public AiState State { get; set; } = AiState.Idle;

    /// <summary>Spawn del que salió (para el respawn y el radio de patrulla).</summary>
    public SpawnDef? Spawn { get; set; }

    /// <summary>Patrulla: destino actual o null si está en pausa.</summary>
    public Vec2? WanderTarget { get; set; }

    /// <summary>Fin de la pausa entre paseos (2–6 s).</summary>
    public long WanderPauseUntilMs { get; set; }

    /// <summary>Próxima percepción (cada 250 ms, no cada tick).</summary>
    public long NextPerceptionAtMs { get; set; }

    public List<Vec2> Path { get; } = new(32);

    public int PathIndex { get; set; }

    public long PathComputedAtMs { get; set; } = long.MinValue;

    /// <summary>Posición del objetivo cuando se calculó el camino (recalcular si se movió > 2 casillas).</summary>
    public Vec2 PathTargetPos { get; set; }

    public void ClearPath()
    {
        Path.Clear();
        PathIndex = 0;
        PathComputedAtMs = long.MinValue;
    }
}
