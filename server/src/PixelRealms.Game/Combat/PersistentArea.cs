using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Área duradera en el suelo (HU-100, ADR-018): aplica los efectos de su hechizo cada `rules.limits.persistentAreaTickMs` con la
/// forma fijada al crearla (`Pos` = centro o punto canónico, `Origin` = posición del lanzador al empezar). Vive en
/// <see cref="Map.MapInstance.PersistentAreas"/>, una lista de capacidad fija; las áreas nunca interactúan entre sí.
/// </summary>
public readonly record struct PersistentArea(int Id, Actor Caster, SpellDef Spell, Vec2 Pos, Vec2 Origin, long ExpiresAtMs, long NextPulseAtMs);

public sealed record PersistentAreaSpawnedEvent(int MapInstanceId, PersistentArea Area) : IGameEvent;

public sealed record PersistentAreaDespawnedEvent(int MapInstanceId, int AreaId) : IGameEvent;
