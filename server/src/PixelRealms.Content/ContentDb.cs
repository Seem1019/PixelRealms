using System.Collections.Frozen;
using PixelRealms.Content.Defs;
using PixelRealms.Content.Validation;

namespace PixelRealms.Content;

/// <summary>
/// Contenido cargado e inmutable. Las búsquedas por id lanzan <see cref="KeyNotFoundException"/> con un mensaje claro
/// (HU-003 CA6). Los hechizos que usan funciones del motor no implementadas quedan en <see cref="UnavailableSpells"/> (ADR-023).
/// </summary>
public sealed class ContentDb
{
    private readonly FrozenDictionary<string, ClassDef> _classes;
    private readonly FrozenDictionary<string, SpellDef> _spells;
    private readonly FrozenDictionary<string, AuraDef> _auras;
    private readonly FrozenDictionary<string, ItemTemplate> _items;
    private readonly FrozenDictionary<string, MonsterTemplate> _monsters;
    private readonly FrozenDictionary<string, LootTable> _lootTables;
    private readonly FrozenDictionary<string, VendorDef> _vendors;
    private readonly FrozenDictionary<string, string> _unavailable;
    private readonly FrozenDictionary<string, IReadOnlyList<SpellDef>> _spellsByClass;

    public ContentDb(
        IReadOnlyList<ClassDef> classes, IReadOnlyList<SpellDef> spells, IReadOnlyList<AuraDef> auras,
        IReadOnlyList<ItemTemplate> items, IReadOnlyList<MonsterTemplate> monsters, IReadOnlyList<LootTable> lootTables,
        IReadOnlyList<VendorDef> vendors, RulesDb rules)
    {
        Classes = classes; Spells = spells; Auras = auras; Items = items; Monsters = monsters; LootTables = lootTables; Vendors = vendors;
        Rules = rules;
        _classes = classes.ToFrozenDictionary(c => c.Id, StringComparer.Ordinal);
        _spells = spells.ToFrozenDictionary(s => s.Id, StringComparer.Ordinal);
        _auras = auras.ToFrozenDictionary(a => a.Id, StringComparer.Ordinal);
        _items = items.ToFrozenDictionary(i => i.Id, StringComparer.Ordinal);
        _monsters = monsters.ToFrozenDictionary(m => m.Id, StringComparer.Ordinal);
        _lootTables = lootTables.ToFrozenDictionary(l => l.Id, StringComparer.Ordinal);
        _vendors = vendors.ToFrozenDictionary(v => v.Id, StringComparer.Ordinal);
        _unavailable = spells.Select(s => (s.Id, Reason: EngineCapabilities.UnavailableReason(s))).Where(x => x.Reason is not null)
            .ToFrozenDictionary(x => x.Id, x => x.Reason!, StringComparer.Ordinal);
        _spellsByClass = classes.ToFrozenDictionary(c => c.Id,
            c => (IReadOnlyList<SpellDef>)spells.Where(s => s.Source == SpellSource.Class && s.ClassId == c.Id).OrderBy(s => s.LevelReq).ThenBy(s => s.Id, StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);
    }

    public IReadOnlyList<ClassDef> Classes { get; }
    public IReadOnlyList<SpellDef> Spells { get; }
    public IReadOnlyList<AuraDef> Auras { get; }
    public IReadOnlyList<ItemTemplate> Items { get; }
    public IReadOnlyList<MonsterTemplate> Monsters { get; }
    public IReadOnlyList<LootTable> LootTables { get; }
    public IReadOnlyList<VendorDef> Vendors { get; }
    public RulesDb Rules { get; }

    /// <summary>spellId → motivo por el que no está disponible (ADR-023).</summary>
    public IReadOnlyDictionary<string, string> UnavailableSpells => _unavailable;

    public ClassDef Class(string id) => Get(_classes, id, "clase", "classes.json");
    public SpellDef Spell(string id) => Get(_spells, id, "hechizo", "spells.json");
    public AuraDef Aura(string id) => Get(_auras, id, "aura", "auras.json");
    public ItemTemplate Item(string id) => Get(_items, id, "item", "items.json");
    public MonsterTemplate Monster(string id) => Get(_monsters, id, "monstruo", "monsters.json");
    public LootTable LootTable(string id) => Get(_lootTables, id, "tabla de botín", "loot_tables.json");
    public VendorDef Vendor(string id) => Get(_vendors, id, "vendedor", "vendors.json");

    public bool TryGetSpell(string id, out SpellDef? spell) => _spells.TryGetValue(id, out spell);
    public bool TryGetItem(string id, out ItemTemplate? item) => _items.TryGetValue(id, out item);
    public bool TryGetMonster(string id, out MonsterTemplate? monster) => _monsters.TryGetValue(id, out monster);
    public bool TryGetClass(string id, out ClassDef? cls) => _classes.TryGetValue(id, out cls);
    public bool HasVendor(string id) => _vendors.ContainsKey(id);

    public bool IsSpellAvailable(string spellId) => !_unavailable.ContainsKey(spellId);

    /// <summary>Hechizos de clase ordenados por levelReq (los 8 del grupo, disponibles o no).</summary>
    public IReadOnlyList<SpellDef> ClassSpells(string classId) => _spellsByClass.TryGetValue(classId, out var l) ? l : [];

    /// <summary>Hechizos que un personaje de esa clase y nivel conoce y puede usar (disponibles, levelReq ≤ nivel).</summary>
    public IEnumerable<SpellDef> KnownSpells(string classId, int level) =>
        ClassSpells(classId).Where(s => s.LevelReq <= level && IsSpellAvailable(s.Id));

    private static T Get<T>(FrozenDictionary<string, T> dict, string id, string kind, string file) =>
        dict.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"No existe {kind} '{id}' en {file}");
}
