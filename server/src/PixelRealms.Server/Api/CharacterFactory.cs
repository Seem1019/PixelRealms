using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Progression;
using PixelRealms.Persistence.Repositories;

namespace PixelRealms.Server.Api;

/// <summary>Construye el personaje nuevo (HU-012 CA2/CA5, HU-058): nivel 1, cementerio por defecto del mapa inicial, equipo inicial de la clase, vida y recurso llenos.</summary>
public sealed class CharacterFactory(ReloadableContent content, World world)
{
    public NewCharacter Create(Guid accountId, string name, ClassDef cls)
    {
        var db = content.Current;
        var rules = db.Rules;
        var mapId = rules.World.StartMapId;
        var spawn = world.Maps.TryGetValue(mapId, out var map) ? map.DefaultGraveyard.Position : new Vec2(1, 1);

        var items = new List<SavedItem>();
        var equipped = new List<ItemTemplate>();
        short bagSlot = 0;
        foreach (var si in cls.StartingItems)
        {
            var template = db.Item(si.ItemId);
            if (si.Equip && template.Slot is { } slot)
            {
                items.Add(new SavedItem(Guid.CreateVersion7(), template.Id, 1, 1, (short)slot));
                equipped.Add(template);
            }
            else
            {
                items.Add(new SavedItem(Guid.CreateVersion7(), template.Id, Math.Clamp(si.Qty, 1, Math.Max(1, template.MaxStack)), 0, bagSlot++));
            }
        }

        var derived = StatCalculator.Derive(cls, 1, rules, equipped);
        var maxResource = StatCalculator.MaxResource(cls, derived, rules);
        // Ira y energía empiezan en 0 (combat.md §Recursos); el maná, lleno.
        var resource = cls.Resource == Resource.Mana ? maxResource : 0;

        var hotbar = db.KnownSpells(cls.Id, 1).Take(rules.Loadout.SpellSlots)
            .Select((s, i) => new SavedHotbarSlot((short)i, 0, s.Id)).ToList();
        return new NewCharacter(accountId, name, cls.Id, mapId, spawn.X, spawn.Y, derived.MaxHp, resource, items, hotbar);
    }
}
