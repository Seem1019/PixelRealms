using PixelRealms.Game.Core;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Entities;

/// <summary>NPC estático del mapa (capa `npcs`): vendedor (`vendorId`) o de otro tipo (`kind`, p. ej. class_change). No combate.</summary>
public sealed class Npc(EntityId id, NpcDef def) : Actor(id, def.Name)
{
    public override ActorKind Kind => ActorKind.Npc;

    public NpcDef Def { get; } = def;

    public string? VendorId => Def.VendorId;

    public string NpcKind => Def.Kind ?? (Def.VendorId is not null ? "vendor" : "npc");
}
