extends GutTest
## HU-105: los vectores de shared/test-vectors/spell_upgrades.json (copiados a tests/vectors/) pasan en spell_upgrades.gd, como en
## SpellUpgrades.cs: el libro muestra los números que usa el servidor.

const VECTORS_PATH := "res://tests/vectors/spell_upgrades.json"


func test_all_vectors() -> void:
	assert_true(FileAccess.file_exists(VECTORS_PATH), "faltan los vectores en %s" % VECTORS_PATH)
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(VECTORS_PATH))
	var cases: Array = data.get("cases", [])
	assert_gt(cases.size(), 0)
	for c: Variant in cases:
		var cd: Dictionary = c
		var name := str(cd["name"])
		var auras := {}
		for a: Variant in cd["auras"]:
			auras[str((a as Dictionary)["id"])] = a
		var s := SpellUpgrades.apply(cd["spell"], str(cd["upgradeId"]), func(id: String) -> Dictionary: return auras.get(id, {}))
		var exp: Dictionary = cd["expected"]
		for key: String in ["castMs", "cooldownMs", "maxTargets"]:
			assert_eq(int(s.get(key, 10 if key == "maxTargets" else 0)), int(exp[key]), "%s: %s" % [name, key])
		assert_eq(int((s.get("cost", {}) as Dictionary).get("amount", 0)) if s.get("cost") is Dictionary else 0, int(exp["cost"]), "%s: cost" % name)
		for key: String in ["range", "aoeRadius", "aoeAngleDeg", "aoeLength", "aoeWidth"]:
			assert_almost_eq(float(s.get(key, 0.0)), float(exp[key]), 0.000001, "%s: %s" % [name, key])
		var effects: Array = s["effects"]
		var exp_effects: Array = exp["effects"]
		assert_eq(effects.size(), exp_effects.size(), "%s: efectos" % name)
		for i: int in mini(effects.size(), exp_effects.size()):
			var e: Dictionary = effects[i]
			var x: Dictionary = exp_effects[i]
			assert_eq(str(e["type"]), str(x["type"]), name)
			for key: String in ["base", "apCoef", "spCoef", "weaponPct"]:
				if x.has(key):
					assert_almost_eq(float(e.get(key, 0.0)), float(x[key]), 0.000001, "%s: efecto %d %s" % [name, i, key])
		var exp_auras: Dictionary = exp["auras"]
		for aura_id: String in exp_auras.keys():
			var over := {}
			for e: Variant in effects:
				if str((e as Dictionary).get("auraId", "")) == aura_id and (e as Dictionary).get("auraOverride") is Dictionary:
					over = (e as Dictionary)["auraOverride"]
			var xa: Dictionary = exp_auras[aura_id]
			assert_eq(int(over.get("durationMs", -1)), int(xa["durationMs"]), "%s: %s durationMs" % [name, aura_id])
			assert_almost_eq(float(over.get("pct", 0.0)), float(xa["pct"]), 0.000001, "%s: %s pct" % [name, aura_id])
			assert_almost_eq(float(over.get("base", 0.0)), float(xa["base"]), 0.000001, "%s: %s base" % [name, aura_id])
			assert_almost_eq(float(over.get("spCoef", 0.0)), float(xa["spCoef"]), 0.000001, "%s: %s spCoef" % [name, aura_id])


func test_diff_lines_say_what_changes() -> void:
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(VECTORS_PATH))
	var by_name := {}
	for c: Variant in data["cases"]:
		by_name[str((c as Dictionary)["name"])] = c
	var quick: Dictionary = by_name["casteo y coste (redondeo a la mitad hacia arriba)"]
	var lines := SpellUpgrades.diff_lines(quick["spell"], SpellUpgrades.apply(quick["spell"], str(quick["upgradeId"])))
	assert_has(lines, "Lanzamiento 2 s → 1.5 s")
	assert_has(lines, "Coste 8 → 3")
	var hot: Dictionary = by_name["potencia del daño"]
	assert_has(SpellUpgrades.diff_lines(hot["spell"], SpellUpgrades.apply(hot["spell"], str(hot["upgradeId"]))), "+20 % de daño")
