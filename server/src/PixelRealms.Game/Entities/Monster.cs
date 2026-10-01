using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;

namespace PixelRealms.Game.Entities;

/// <summary>Monstruo vivo: su plantilla, su spawn y su estado de IA (HU-031/HU-036).</summary>
public sealed class Monster(EntityId id, MonsterTemplate template, Vec2 spawnPosition, float wanderRadius) : Actor(id, template.Name)
{
    public override ActorKind Kind => ActorKind.Monster;

    public MonsterTemplate Template { get; } = template;

    public Vec2 SpawnPosition { get; } = spawnPosition;

    public float WanderRadius { get; } = wanderRadius;

    public string TemplateId => Template.Id;

    public bool IsBoss => Template.Boss;
}
