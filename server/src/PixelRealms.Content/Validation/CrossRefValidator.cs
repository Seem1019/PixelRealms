using System.Text.Json;

namespace PixelRealms.Content.Validation;

/// <summary>
/// Validaciones cruzadas entre archivos de content/: ids únicos, referencias (auras, hechizos, items, tablas, clases),
/// coherencia con rules.json y las reglas de la skill game-content §Procedimiento (HU-003 CA 3, 4, 4b, 4d).
/// </summary>
public static class CrossRefValidator
{
    /// <summary>Casillas de la barra (teclas 1–8 del cliente, `Player.Hotbar`): `loadout` no puede repartir más.</summary>
    private const int HotbarKeys = 8;

    private static HashSet<string> ImplementedShapes => EngineCapabilities.Shapes;
    private static HashSet<string> ImplementedTargetings => EngineCapabilities.Targetings;
    private static HashSet<string> ImplementedEffects => EngineCapabilities.Effects;

    public static void Run(IReadOnlyDictionary<string, JsonDocument> files, ValidationReport report)
    {
        var classes = Arr(files["classes"], "classes");
        var spells = Arr(files["spells"], "spells");
        var auras = Arr(files["auras"], "auras");
        var items = Arr(files["items"], "items");
        var monsters = Arr(files["monsters"], "monsters");
        var lootTables = Arr(files["loot_tables"], "lootTables");
        var vendors = Arr(files["vendors"], "vendors");
        var rules = files["rules"].RootElement;

        // --- ids únicos por archivo
        var classIds = UniqueIds("classes.json", "classes", classes, report);
        var spellIds = UniqueIds("spells.json", "spells", spells, report);
        var auraIds = UniqueIds("auras.json", "auras", auras, report);
        var itemIds = UniqueIds("items.json", "items", items, report);
        var monsterIds = UniqueIds("monsters.json", "monsters", monsters, report);
        var lootIds = UniqueIds("loot_tables.json", "lootTables", lootTables, report);
        UniqueIds("vendors.json", "vendors", vendors, report);

        var classById = ById(classes);
        var itemById = ById(items);
        var spellById = ById(spells);
        var auraById = ById(auras);

        var progression = rules.GetProperty("progression");
        var maxLevel = progression.GetProperty("maxLevel").GetInt32();
        var affinity = rules.GetProperty("affinity");
        var weaponScaling = affinity.GetProperty("weaponScaling");
        var byClass = affinity.GetProperty("byClass");
        var combat = rules.GetProperty("combat");
        var minInstantCd = combat.GetProperty("minInstantSpellCooldownMs").GetInt32();
        var instantConeMaxRadius = combat.GetProperty("instantConeMaxRadiusTiles").GetDouble();
        var areaTickMs = rules.GetProperty("limits").GetProperty("persistentAreaTickMs").GetInt32();
        var maxSummonsPerCaster = rules.GetProperty("limits").GetProperty("maxSummonsPerCaster").GetInt32();
        // Tope de nivel de la fase activa: lo que ya se alcanza no puede quedarse en aviso.
        var phaseCaps = progression.GetProperty("levelCapByPhase").EnumerateArray().Select(e => e.GetInt32()).ToList();
        var phase = rules.GetProperty("world").GetProperty("currentPhase").GetInt32();
        var activeCap = phase >= 1 && phase <= phaseCaps.Count ? phaseCaps[phase - 1] : maxLevel;
        var maxSpellsPerClass = rules.GetProperty("loadout").GetProperty("maxSpellsPerClass").GetInt32();

        var referencedAuras = new HashSet<string>(StringComparer.Ordinal);
        var upgradeIds = new HashSet<string>(StringComparer.Ordinal);

        // --- hechizos
        var spellsByClass = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        for (var i = 0; i < spells.Count; i++)
        {
            var sp = spells[i];
            var p = $"/spells/{i}";
            var id = Str(sp, "id");
            var effects = sp.TryGetProperty("effects", out var effEl) ? effEl.EnumerateArray().ToList() : new List<JsonElement>();
            var targeting = Str(sp, "targeting");
            var source = Str(sp, "source");

            foreach (var (eff, k) in effects.Select((e, k) => (e, k)))
            {
                var type = Str(eff, "type");
                if (eff.TryGetProperty("auraId", out var auraRef))
                {
                    var auraId = auraRef.GetString()!;
                    referencedAuras.Add(auraId);
                    if (!auraIds.Contains(auraId))
                        report.Error("spells.json", $"{p}/effects/{k}/auraId", $"{id}: auraId '{auraId}' no existe en auras.json");
                }
                if (!ImplementedEffects.Contains(type))
                    report.Warn("spells.json", $"{p}/effects/{k}/type", $"{id}: efecto '{type}' aún no implementado → hechizo no disponible (ADR-023)");
                if (type == "summon")
                {
                    // HU-116: solo monstruos invocan, una plantilla que exista y dentro del tope por invocador.
                    if (source != "monster")
                        report.Error("spells.json", $"{p}/effects/{k}", $"{id}: solo los hechizos de monstruo invocan (HU-116)");
                    var summoned = Str(eff, "monsterId");
                    if (!monsterIds.Contains(summoned))
                        report.Error("spells.json", $"{p}/effects/{k}/monsterId", $"{id}: monsterId '{summoned}' no existe en monsters.json");
                    if (eff.TryGetProperty("count", out var cnt) && cnt.GetInt32() > maxSummonsPerCaster)
                        report.Error("spells.json", $"{p}/effects/{k}/count", $"{id}: invoca {cnt.GetInt32()}, más que rules.limits.maxSummonsPerCaster ({maxSummonsPerCaster})");
                }
            }

            // HU-100: un área duradera es un área que dura al menos un pulso; sin desplazamientos, invocaciones ni proyectil (los
            // pulsos solo tocan a los objetivos y un proyectil no deja área).
            if (sp.TryGetProperty("areaDurationMs", out var durEl))
            {
                if (!targeting.StartsWith("ground_aoe", StringComparison.Ordinal) && !targeting.StartsWith("self_aoe", StringComparison.Ordinal))
                    report.Error("spells.json", $"{p}/areaDurationMs", $"{id}: solo un hechizo de área puede dejar un área duradera");
                else if (effects.Any(e => Str(e, "type") is "leap" or "dash" or "summon"))
                    report.Error("spells.json", $"{p}/areaDurationMs", $"{id}: un salto, una carga o una invocación no dejan un área duradera");
                if (sp.TryGetProperty("projectile", out _))
                    report.Error("spells.json", $"{p}/areaDurationMs", $"{id}: un hechizo con proyectil no deja un área duradera");
                if (durEl.GetInt32() < areaTickMs)
                    report.Error("spells.json", $"{p}/areaDurationMs", $"{id}: dura {durEl.GetInt32()} ms, menos que un pulso (rules.limits.persistentAreaTickMs = {areaTickMs})");
            }

            if (sp.TryGetProperty("upgrades", out var upgradesEl))
                CheckUpgrades(sp, id, source, effects, upgradesEl, $"{p}/upgrades", auraIds, upgradeIds, referencedAuras, report);

            if (!ImplementedTargetings.Contains(targeting))
                report.Warn("spells.json", $"{p}/targeting", $"{id}: targeting '{targeting}' aún no implementado → hechizo no disponible (ADR-023)");
            if (sp.TryGetProperty("shape", out var shapeEl) && !ImplementedShapes.Contains(shapeEl.GetString()!))
                report.Warn("spells.json", $"{p}/shape", $"{id}: forma '{shapeEl.GetString()}' aún no implementada → hechizo no disponible (ADR-023, HU-086)");

            if (targeting == "ground_aoe_all")
            {
                // HU-085: dentro del área, lo positivo va a aliados y lo negativo a enemigos; hace falta al menos uno de cada.
                bool IsDebuffAura(JsonElement e) => e.TryGetProperty("auraId", out var a) && auraById.TryGetValue(a.GetString()!, out var au)
                                                     && au.TryGetProperty("isDebuff", out var d) && d.ValueKind == JsonValueKind.True;
                var hasPositive = effects.Any(e => Str(e, "type") == "heal" || (Str(e, "type") == "apply_aura" && !IsDebuffAura(e) && Str(e, "applyTo") != "self"));
                var hasNegative = effects.Any(e => Str(e, "type") == "damage" || (Str(e, "type") == "apply_aura" && IsDebuffAura(e)));
                if (!hasPositive || !hasNegative)
                    report.Error("spells.json", $"{p}/effects", $"{id}: un hechizo ground_aoe_all necesita al menos un efecto positivo (heal o aura beneficiosa) y uno negativo (damage o aura perjudicial) (HU-085 CA4)");
            }

            if (source == "class")
            {
                var classId = Str(sp, "classId");
                if (!classById.TryGetValue(classId, out var cls))
                {
                    report.Error("spells.json", $"{p}/classId", $"{id}: classId '{classId}' no existe en classes.json");
                    continue;
                }
                spellsByClass.TryAdd(classId, new List<JsonElement>());
                spellsByClass[classId].Add(sp);

                var classResource = Str(cls, "resource");
                var costResource = sp.TryGetProperty("cost", out var cost) && cost.TryGetProperty("resource", out var r) ? r.GetString() : null;
                if (costResource != null && costResource != classResource)
                    report.Error("spells.json", $"{p}/cost/resource", $"{id}: cost.resource '{costResource}' no coincide con el recurso de la clase '{classResource}'");

                var levelReq = sp.TryGetProperty("levelReq", out var lr) ? lr.GetInt32() : 1;
                if (levelReq < 1 || levelReq > maxLevel)
                    report.Error("spells.json", $"{p}/levelReq", $"{id}: levelReq {levelReq} fuera de 1..{maxLevel}");

                var castMs = sp.TryGetProperty("castMs", out var cm) ? cm.GetInt32() : 0;
                var cooldownMs = sp.TryGetProperty("cooldownMs", out var cd) ? cd.GetInt32() : 0;
                if (castMs == 0 && cooldownMs < minInstantCd)
                    report.Error("spells.json", $"{p}/cooldownMs", $"{id}: hechizo instantáneo de clase con cooldown {cooldownMs} ms < rules.combat.minInstantSpellCooldownMs ({minInstantCd})");

                // ADR-027 D4: un cono cuerpo a cuerpo se esquiva saliendo del frente del lanzador; no necesita casteo.
                var meleeCone = Str(sp, "shape") == "cone" && sp.TryGetProperty("aoeRadius", out var ar) && ar.GetDouble() <= instantConeMaxRadius;
                if (targeting.StartsWith("ground_aoe", StringComparison.Ordinal) && castMs == 0 && !meleeCone
                    && effects.Any(e => Str(e, "type") == "damage") && !effects.Any(e => Str(e, "type") == "leap"))
                {
                    // Revisión de autoridad (HU-102): en una fase que aún no llega a su nivel es un aviso; si ya se alcanza, error.
                    if (levelReq <= activeCap)
                        report.Error("spells.json", $"{p}/castMs", $"{id}: área apuntada de daño sin casteo que ya se alcanza (nivel {levelReq} ≤ tope {activeCap}): no se puede esquivar (ADR-015, ADR-027 D4)");
                    else
                        report.Warn("spells.json", $"{p}/castMs", $"{id}: área apuntada de daño sin casteo: no se puede esquivar (ADR-015)");
                }
            }
        }

        // --- clases: número de hechizos y niveles de desbloqueo frente a rules.progression.spellUnlockLevels
        var unlockLevels = progression.GetProperty("spellUnlockLevels").EnumerateArray().Select(e => e.GetInt32()).ToList();
        foreach (var (classId, list) in spellsByClass)
        {
            if (list.Count > maxSpellsPerClass)
                report.Error("spells.json", "", $"la clase '{classId}' tiene {list.Count} hechizos > rules.loadout.maxSpellsPerClass ({maxSpellsPerClass})");
            var levels = list.Select(s => s.GetProperty("levelReq").GetInt32()).Order().ToList();
            if (!levels.SequenceEqual(unlockLevels.Take(levels.Count)))
                report.Error("spells.json", "", $"los levelReq de '{classId}' [{string.Join(", ", levels)}] no coinciden con rules.progression.spellUnlockLevels [{string.Join(", ", unlockLevels)}]");
        }
        foreach (var classId in classIds.Where(c => !spellsByClass.ContainsKey(c)))
            report.Warn("classes.json", "", $"la clase '{classId}' no tiene hechizos en spells.json");

        // --- clases: equipo inicial existe y tiene afinidad alta
        for (var i = 0; i < classes.Count; i++)
        {
            var cls = classes[i];
            var classId = Str(cls, "id");
            if (!cls.TryGetProperty("startingItems", out var starting)) continue;
            var k = 0;
            foreach (var si in starting.EnumerateArray())
            {
                var itemId = Str(si, "itemId");
                var p = $"/classes/{i}/startingItems/{k++}/itemId";
                if (!itemById.TryGetValue(itemId, out var item))
                {
                    report.Error("classes.json", p, $"{classId}: itemId '{itemId}' no existe en items.json");
                    continue;
                }
                var type = item.TryGetProperty("weaponType", out var wt) ? wt.GetString() : item.TryGetProperty("armorType", out var at) ? at.GetString() : null;
                if (type == null) continue; // consumibles
                var aff = byClass.TryGetProperty(classId, out var row) && row.TryGetProperty(type, out var a) ? a.GetString() : "baja";
                if (aff != "alta")
                    report.Error("classes.json", p, $"{classId}: el item inicial '{itemId}' ({type}) tiene afinidad {aff}, debe ser alta");
            }
        }

        // --- items
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var id = Str(it, "id");
            var p = $"/items/{i}";
            if (it.TryGetProperty("damageMin", out var dmin) && it.TryGetProperty("damageMax", out var dmax) && dmin.GetDouble() > dmax.GetDouble())
                report.Error("items.json", $"{p}/damageMin", $"{id}: damageMin {dmin} > damageMax {dmax}");
            if (it.TryGetProperty("weaponType", out var wt))
            {
                var expected = weaponScaling.TryGetProperty(wt.GetString()!, out var ws) ? ws.GetString() : null;
                var actual = it.TryGetProperty("scaling", out var sc) ? sc.GetString() : null;
                if (expected == null)
                    report.Error("items.json", $"{p}/weaponType", $"{id}: weaponType '{wt}' no está en rules.affinity.weaponScaling");
                else if (actual != expected)
                    report.Error("items.json", $"{p}/scaling", $"{id}: scaling '{actual}' debe ser '{expected}' (rules.affinity.weaponScaling.{wt})");
            }
            if (it.TryGetProperty("levelReq", out var lr) && (lr.GetInt32() < 1 || lr.GetInt32() > maxLevel))
                report.Error("items.json", $"{p}/levelReq", $"{id}: levelReq {lr.GetInt32()} fuera de 1..{maxLevel}");
            if (it.TryGetProperty("useSpellId", out var use) && !spellIds.Contains(use.GetString()!))
                report.Error("items.json", $"{p}/useSpellId", $"{id}: useSpellId '{use}' no existe en spells.json");
            else if (it.TryGetProperty("useSpellId", out var use2) && Str(spellById[use2.GetString()!], "source") != "item")
                report.Warn("items.json", $"{p}/useSpellId", $"{id}: el hechizo '{use2}' no tiene source 'item'");
        }

        // --- monstruos
        for (var i = 0; i < monsters.Count; i++)
        {
            var m = monsters[i];
            var id = Str(m, "id");
            var p = $"/monsters/{i}";
            var level = m.GetProperty("level").GetInt32();
            if (level > maxLevel)
                report.Error("monsters.json", $"{p}/level", $"{id}: nivel {level} > rules.progression.maxLevel ({maxLevel})");
            if (m.TryGetProperty("damageMin", out var dmin) && m.TryGetProperty("damageMax", out var dmax) && dmin.GetDouble() > dmax.GetDouble())
                report.Error("monsters.json", $"{p}/damageMin", $"{id}: damageMin {dmin} > damageMax {dmax}");
            var isBossType = Str(m, "type") == "boss";
            var bossFlag = m.TryGetProperty("boss", out var b) && b.ValueKind == JsonValueKind.True;
            if (isBossType != bossFlag)
                report.Error("monsters.json", $"{p}/boss", $"{id}: type 'boss' y boss:true deben ir juntos (type={Str(m, "type")}, boss={bossFlag})");
            if (m.TryGetProperty("lootTableId", out var lt) && !lootIds.Contains(lt.GetString()!))
                report.Error("monsters.json", $"{p}/lootTableId", $"{id}: lootTableId '{lt}' no existe en loot_tables.json");
            if (m.TryGetProperty("spells", out var msp))
            {
                var k = 0;
                foreach (var ms in msp.EnumerateArray())
                {
                    var sid = Str(ms, "spellId");
                    if (!spellById.TryGetValue(sid, out var spell))
                        report.Error("monsters.json", $"{p}/spells/{k}/spellId", $"{id}: spellId '{sid}' no existe en spells.json");
                    else if (Str(spell, "source") != "monster")
                        report.Warn("monsters.json", $"{p}/spells/{k}/spellId", $"{id}: el hechizo '{sid}' no tiene source 'monster'");
                    k++;
                }
            }
            if (m.TryGetProperty("xp", out _))
                report.Error("monsters.json", $"{p}/xp", $"{id}: la XP no se escribe, se calcula con rules.progression (GDD §Progresión)");
        }

        // --- tablas de botín
        for (var i = 0; i < lootTables.Count; i++)
        {
            var t = lootTables[i];
            var id = Str(t, "id");
            var p = $"/lootTables/{i}";
            if (t.TryGetProperty("gold", out var gold) && gold.GetProperty("min").GetDouble() > gold.GetProperty("max").GetDouble())
                report.Error("loot_tables.json", $"{p}/gold", $"{id}: gold.min > gold.max");
            if (t.TryGetProperty("entries", out var entries))
            {
                var k = 0;
                foreach (var e in entries.EnumerateArray())
                {
                    var itemId = Str(e, "itemId");
                    if (!itemIds.Contains(itemId))
                        report.Error("loot_tables.json", $"{p}/entries/{k}/itemId", $"{id}: itemId '{itemId}' no existe en items.json");
                    if (e.TryGetProperty("min", out var mn) && e.TryGetProperty("max", out var mx) && mn.GetDouble() > mx.GetDouble())
                        report.Error("loot_tables.json", $"{p}/entries/{k}", $"{id}: min > max en '{itemId}'");
                    k++;
                }
            }
            if (t.TryGetProperty("groups", out var groups))
            {
                var g = 0;
                foreach (var grp in groups.EnumerateArray())
                {
                    var rolls = grp.GetProperty("rolls").GetInt32();
                    var gEntries = grp.GetProperty("entries").EnumerateArray().ToList();
                    if (rolls > gEntries.Count)
                        report.Error("loot_tables.json", $"{p}/groups/{g}/rolls", $"{id}: rolls {rolls} > {gEntries.Count} entradas");
                    var k = 0;
                    foreach (var e in gEntries)
                    {
                        var itemId = Str(e, "itemId");
                        if (!itemIds.Contains(itemId))
                            report.Error("loot_tables.json", $"{p}/groups/{g}/entries/{k}/itemId", $"{id}: itemId '{itemId}' no existe en items.json");
                        k++;
                    }
                    g++;
                }
            }
        }
        var usedLoot = monsters.Where(m => m.TryGetProperty("lootTableId", out _)).Select(m => Str(m, "lootTableId")).ToHashSet(StringComparer.Ordinal);
        foreach (var orphan in lootIds.Where(l => !usedLoot.Contains(l)))
            report.Warn("loot_tables.json", "", $"la tabla '{orphan}' no la usa ningún monstruo");

        // --- vendedores
        for (var i = 0; i < vendors.Count; i++)
        {
            var v = vendors[i];
            var k = 0;
            foreach (var itemId in v.GetProperty("items").EnumerateArray().Select(e => e.GetString()!))
            {
                if (!itemIds.Contains(itemId))
                    report.Error("vendors.json", $"/vendors/{i}/items/{k}", $"{Str(v, "id")}: itemId '{itemId}' no existe en items.json");
                k++;
            }
        }

        // --- auras huérfanas (aviso: pueden ser de items o monstruos futuros)
        foreach (var it in items)
            if (it.TryGetProperty("useSpellId", out var use) && spellById.TryGetValue(use.GetString()!, out var sp))
                foreach (var e in sp.GetProperty("effects").EnumerateArray())
                    if (e.TryGetProperty("auraId", out var a)) referencedAuras.Add(a.GetString()!);
        foreach (var orphan in auraIds.Where(a => !referencedAuras.Contains(a)))
            report.Warn("auras.json", "", $"el aura '{orphan}' no la aplica ningún hechizo");

        // --- rules.json: coherencia interna y con los enums de common.schema.json
        var weaponTypes = new[] { "sword", "axe", "mace", "dagger", "staff", "wand" };
        var armorTypes = new[] { "cloth", "leather", "mail", "plate", "shield", "jewelry" };
        foreach (var classId in classIds)
        {
            if (!byClass.TryGetProperty(classId, out var row))
            {
                report.Error("rules.json", "/affinity/byClass", $"falta la fila de la clase '{classId}'");
                continue;
            }
            foreach (var type in weaponTypes.Concat(armorTypes))
                if (!row.TryGetProperty(type, out _))
                    report.Error("rules.json", $"/affinity/byClass/{classId}", $"falta la afinidad con '{type}'");
        }
        foreach (var type in weaponTypes)
            if (!weaponScaling.TryGetProperty(type, out _))
                report.Error("rules.json", "/affinity/weaponScaling", $"falta el weaponType '{type}'");
        var weaponTypeRules = rules.GetProperty("weapons").GetProperty("types").EnumerateObject().Select(p => p.Name).Order().ToList();
        var scalingKeys = weaponScaling.EnumerateObject().Select(p => p.Name).Order().ToList();
        if (!weaponTypeRules.SequenceEqual(scalingKeys))
            report.Error("rules.json", "/weapons/types", $"las claves [{string.Join(", ", weaponTypeRules)}] no coinciden con affinity.weaponScaling [{string.Join(", ", scalingKeys)}]");

        var group = rules.GetProperty("group");
        var maxMembers = group.GetProperty("maxMembers").GetInt32();
        var bonusCount = group.GetProperty("bonusBySize").GetArrayLength();
        if (bonusCount != maxMembers)
            report.Error("rules.json", "/group/bonusBySize", $"tiene {bonusCount} entradas, debe tener maxMembers ({maxMembers})");

        // Números que el tick usa como rango o índice: un `/reload rules` con ellos al revés rompería el bucle.
        var ai = rules.GetProperty("ai");
        if (ai.GetProperty("wanderPauseMinMs").GetInt32() > ai.GetProperty("wanderPauseMaxMs").GetInt32())
            report.Error("rules.json", "/ai/wanderPauseMinMs", "wanderPauseMinMs no puede ser mayor que wanderPauseMaxMs");
        var loadout = rules.GetProperty("loadout");
        var slots = loadout.GetProperty("spellSlots").GetInt32() + loadout.GetProperty("usableSlots").GetInt32();
        if (slots > HotbarKeys)
            report.Error("rules.json", "/loadout", $"spellSlots + usableSlots = {slots}, más que las {HotbarKeys} teclas de la barra");

        var caps = progression.GetProperty("levelCapByPhase").EnumerateArray().Select(e => e.GetInt32()).ToList();
        if (caps.Count == 0 || caps[^1] != maxLevel)
            report.Error("rules.json", "/progression/levelCapByPhase", $"el último tope ({(caps.Count == 0 ? "—" : caps[^1])}) debe ser igual a maxLevel ({maxLevel})");
        if (!caps.SequenceEqual(caps.Order()))
            report.Error("rules.json", "/progression/levelCapByPhase", "los topes deben ser crecientes");
        var currentPhase = rules.GetProperty("world").GetProperty("currentPhase").GetInt32();
        if (currentPhase < 1 || currentPhase > caps.Count)
            report.Error("rules.json", "/world/currentPhase", $"la fase {currentPhase} no tiene tope en levelCapByPhase ({caps.Count} fases)");
        var minutes = progression.GetProperty("minutesPerLevel").GetArrayLength();
        if (minutes != maxLevel - 1)
            report.Error("rules.json", "/progression/minutesPerLevel", $"tiene {minutes} entradas, debe tener maxLevel − 1 ({maxLevel - 1})");
        if (progression.TryGetProperty("spellRankLevels", out var rankLevels))
            foreach (var lvl in rankLevels.EnumerateArray().Select(e => e.GetInt32()))
                if (lvl < 2 || lvl > maxLevel) report.Error("rules.json", "/progression/spellRankLevels", $"nivel de rango {lvl} fuera de 2..{maxLevel}");
                else if (unlockLevels.Contains(lvl)) report.Warn("rules.json", "/progression/spellRankLevels", $"el nivel {lvl} desbloquea hechizo y sube rango a la vez");
        // ADR-027 D1: la elección de mejoras llega con un rango (el segundo, nivel 8).
        var upgradeLevel = progression.GetProperty("spellUpgradeLevel").GetInt32();
        if (!progression.GetProperty("spellRankLevels").EnumerateArray().Any(e => e.GetInt32() == upgradeLevel))
            report.Error("rules.json", "/progression/spellUpgradeLevel", $"spellUpgradeLevel {upgradeLevel} no es un nivel de rango (spellRankLevels)");

        foreach (var classId in classIds)
            if (!rules.GetProperty("classScaling").TryGetProperty(classId, out _))
                report.Error("rules.json", "/classScaling", $"falta la fila de la clase '{classId}'");
        var adv = rules.GetProperty("classAdvantage");
        foreach (var a in classIds)
        {
            if (!adv.TryGetProperty(a, out var row)) { report.Error("rules.json", "/classAdvantage", $"falta la fila '{a}'"); continue; }
            foreach (var d in classIds)
                if (!row.TryGetProperty(d, out _)) report.Error("rules.json", $"/classAdvantage/{a}", $"falta la columna '{d}'");
        }
        var pvp = rules.GetProperty("pvp");
        var rulesets = pvp.GetProperty("rulesets");
        foreach (var rs in pvp.GetProperty("enabledRulesets").EnumerateArray().Select(e => e.GetString()!).Append(pvp.GetProperty("defaultRuleset").GetString()!))
            if (!rulesets.TryGetProperty(rs, out _))
                report.Error("rules.json", "/pvp", $"el ruleset '{rs}' no está definido en pvp.rulesets");
        var pent = rules.GetProperty("balanceTargets").GetProperty("pentagram");
        var budget = pent.GetProperty("budget").GetInt32();
        var maxAxis = pent.GetProperty("maxPerAxis").GetInt32();
        foreach (var cls in pent.GetProperty("classes").EnumerateObject())
        {
            var axes = new[] { "single", "aoe", "cc", "mobility", "armor" }.Select(a => cls.Value.GetProperty(a).GetInt32()).ToList();
            if (axes.Sum() != budget)
                report.Error("rules.json", $"/balanceTargets/pentagram/classes/{cls.Name}", $"las puntas suman {axes.Sum()}, el presupuesto es {budget}");
            if (axes.Any(a => a > maxAxis))
                report.Error("rules.json", $"/balanceTargets/pentagram/classes/{cls.Name}", $"una punta supera maxPerAxis ({maxAxis})");
            if (!classIds.Contains(cls.Name))
                report.Error("rules.json", $"/balanceTargets/pentagram/classes/{cls.Name}", "la clase no existe en classes.json");
            var refWeapon = cls.Value.GetProperty("referenceWeapon").GetString()!;
            if (byClass.TryGetProperty(cls.Name, out var row) && row.TryGetProperty(refWeapon, out var aff) && aff.GetString() != "alta")
                report.Warn("rules.json", $"/balanceTargets/pentagram/classes/{cls.Name}/referenceWeapon", $"el arma de referencia '{refWeapon}' no tiene afinidad alta");
        }
    }

    /// <summary>
    /// HU-104: mejoras de un hechizo. Solo de clase; ids únicos en todo spells.json; cada modificador es de uno de los cuatro
    /// tipos (campo del hechizo, efecto + mult, aura + campo, efecto añadido) y apunta a algo que el hechizo tiene.
    /// </summary>
    private static void CheckUpgrades(JsonElement sp, string id, string source, List<JsonElement> effects, JsonElement upgrades, string p,
        HashSet<string> auraIds, HashSet<string> upgradeIds, HashSet<string> referencedAuras, ValidationReport report)
    {
        if (source != "class")
        {
            report.Error("spells.json", p, $"{id}: solo los hechizos de clase tienen mejoras (HU-104)");
            return;
        }
        var effectTypes = effects.Select(e => Str(e, "type")).ToHashSet(StringComparer.Ordinal);
        var appliedAuras = effects.Where(e => Str(e, "type") == "apply_aura").Select(e => Str(e, "auraId")).ToHashSet(StringComparer.Ordinal);
        foreach (var (up, u) in upgrades.EnumerateArray().Select((x, u) => (x, u)))
        {
            var upId = Str(up, "id");
            if (!upgradeIds.Add(upId))
                report.Error("spells.json", $"{p}/{u}/id", $"{id}: id de mejora '{upId}' repetido en spells.json");
            if (!up.TryGetProperty("mods", out var mods)) continue;
            foreach (var (m, k) in mods.EnumerateArray().Select((x, k) => (x, k)))
            {
                var mp = $"{p}/{u}/mods/{k}";
                var stat = Str(m, "stat");
                var aura = Str(m, "aura");
                var effect = Str(m, "effect");
                var hasAdd = m.TryGetProperty("add", out _);
                var hasMult = m.TryGetProperty("mult", out _);
                var kinds = (stat != "" && aura == "" ? 1 : 0) + (effect != "" ? 1 : 0) + (aura != "" ? 1 : 0) + (m.TryGetProperty("addEffect", out _) ? 1 : 0);
                if (kinds != 1)
                {
                    report.Error("spells.json", mp, $"{id}/{upId}: un modificador cambia un campo ('stat'), un tipo de efecto ('effect'), un aura ('aura' + 'stat') o añade un efecto ('addEffect'), uno solo");
                    continue;
                }
                if (m.TryGetProperty("addEffect", out var added))
                {
                    // Las mismas reglas que los efectos del hechizo (revisión de autoridad de HU-100/HU-116).
                    if (Str(added, "type") == "summon")
                        report.Error("spells.json", $"{mp}/addEffect", $"{id}/{upId}: solo los hechizos de monstruo invocan (HU-116)");
                    if (sp.TryGetProperty("areaDurationMs", out _) && Str(added, "type") is "leap" or "dash")
                        report.Error("spells.json", $"{mp}/addEffect", $"{id}/{upId}: un salto o una carga no van en un área duradera");
                    if (added.TryGetProperty("auraId", out var addedAura)) referencedAuras.Add(addedAura.GetString()!);
                    if (added.TryGetProperty("auraId", out addedAura) && !auraIds.Contains(addedAura.GetString()!))
                        report.Error("spells.json", $"{mp}/addEffect/auraId", $"{id}/{upId}: auraId '{addedAura.GetString()}' no existe en auras.json");
                    if (!ImplementedEffects.Contains(Str(added, "type")))
                        report.Error("spells.json", $"{mp}/addEffect/type", $"{id}/{upId}: el efecto añadido '{Str(added, "type")}' no está implementado");
                    continue;
                }
                if (!hasAdd && !hasMult)
                    report.Error("spells.json", mp, $"{id}/{upId}: el modificador no cambia nada (falta 'add' o 'mult')");
                if (effect != "")
                {
                    if (hasAdd) report.Error("spells.json", $"{mp}/add", $"{id}/{upId}: un modificador de efecto solo escala ('mult')");
                    if (!effectTypes.Contains(effect)) report.Error("spells.json", $"{mp}/effect", $"{id}/{upId}: el hechizo no tiene efectos '{effect}'");
                }
                else if (aura != "")
                {
                    if (!appliedAuras.Contains(aura)) report.Error("spells.json", $"{mp}/aura", $"{id}/{upId}: el hechizo no aplica el aura '{aura}'");
                    if (!Content.SpellUpgrades.AuraStats.Contains(stat)) report.Error("spells.json", $"{mp}/stat", $"{id}/{upId}: '{stat}' no es un campo de aura que se pueda mejorar (durationMs, pct, amount)");
                }
                else if (!Content.SpellUpgrades.SpellStats.Contains(stat))
                {
                    report.Error("spells.json", $"{mp}/stat", $"{id}/{upId}: '{stat}' no es un campo de hechizo que se pueda mejorar");
                }
                else if (stat == "cost" && !sp.TryGetProperty("cost", out _))
                {
                    report.Error("spells.json", $"{mp}/stat", $"{id}/{upId}: el hechizo no tiene coste");
                }
            }
        }
    }

    private static List<JsonElement> Arr(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToList() : new List<JsonElement>();

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    /// <summary>Primer elemento por id (los duplicados ya los reporta UniqueIds).</summary>
    private static Dictionary<string, JsonElement> ById(List<JsonElement> list)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var e in list) d.TryAdd(Str(e, "id"), e);
        return d;
    }

    private static HashSet<string> UniqueIds(string file, string key, List<JsonElement> list, ValidationReport report)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < list.Count; i++)
        {
            var id = Str(list[i], "id");
            if (id.Length == 0) { report.Error(file, $"/{key}/{i}/id", "falta el id"); continue; }
            if (!ids.Add(id)) report.Error(file, $"/{key}/{i}/id", $"id duplicado '{id}'");
        }
        return ids;
    }
}
