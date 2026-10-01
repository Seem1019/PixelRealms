namespace PixelRealms.Content.Defs;

// Enums de content/schemas/common.schema.json y de cada schema. Se serializan en snake_case minúscula
// (ContentJson.Options): GroundAoeAll ⇄ "ground_aoe_all", MeleeDps ⇄ "melee_dps", MainHand ⇄ "main_hand".

public enum Resource { Mana, Rage, Energy }

public enum School { Physical, Magic }

public enum Rarity { Junk, Common, Uncommon, Rare, Epic }

public enum EquipSlot { Head, Neck, Chest, Hands, Legs, Feet, Ring, MainHand, OffHand }

public enum WeaponType { Sword, Axe, Mace, Dagger, Staff, Wand }

public enum ArmorType { Cloth, Leather, Mail, Plate, Shield, Jewelry }

public enum Affinity { Alta, Media, Baja }

public enum MonsterType { Normal, Hard, Elite, Boss }

public enum ClassRole { Tank, MeleeDps, RangedDps, Healer }

public enum SpellSource { Class, Item, Monster }

public enum Targeting { Self, Enemy, Ally, SelfAoeEnemies, SelfAoeAllies, GroundAoeEnemies, GroundAoeAllies, GroundAoeAll }

public enum Shape { Circle, Cone, Line }

public enum EffectType { Damage, Heal, RestoreResource, ApplyAura, Taunt, Dash, Interrupt, Leap }

public enum ApplyTo { Targets, Self }

public enum AuraKind { Dot, Hot, StatMod, Stun, Root, Silence, Shield, Slow }

public enum ItemType { Weapon, Armor, Consumable, Material, Junk }

public enum Stat { Str, Agi, Int }

public enum MonsterSpellTarget { Current, RandomNotTopThreat, Self }

public enum LootOwnerMode { RandomPerItem, FreeForAll }

public enum GoldSplit { Equal, ToLooter }

public static class TargetingExtensions
{
    public static bool IsArea(this Targeting t) => t is not (Targeting.Self or Targeting.Enemy or Targeting.Ally);

    public static bool IsGround(this Targeting t) => t is Targeting.GroundAoeEnemies or Targeting.GroundAoeAllies or Targeting.GroundAoeAll;

    public static bool HitsEnemies(this Targeting t) => t is Targeting.Enemy or Targeting.SelfAoeEnemies or Targeting.GroundAoeEnemies or Targeting.GroundAoeAll;

    public static bool HitsAllies(this Targeting t) => t is Targeting.Ally or Targeting.SelfAoeAllies or Targeting.GroundAoeAllies or Targeting.GroundAoeAll;
}
