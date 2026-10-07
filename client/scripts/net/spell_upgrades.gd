class_name SpellUpgrades
## Traducción de SpellUpgrades.cs (HU-104/HU-105): el hechizo con una mejora aplicada, para mostrar sus números (libro, barra,
## tooltips) y dibujar su forma (apuntado, marcas de los demás con `upgradeId`). Solo visual: el servidor resuelve. Los casos de
## shared/test-vectors/spell_upgrades.json (copiados a tests/vectors/) deben pasar aquí y en el servidor.
## Modificador: valor final = valor · `mult` + `add`; `stat` (campo del hechizo), `effect` + `mult` (base y coeficientes de sus
## efectos de ese tipo), `aura` + `stat` (`durationMs`, `pct`, `amount` de un aura que aplica: va en `auraOverride` del efecto)
## o `addEffect`.

const INT_STATS := ["cooldownMs", "castMs", "maxTargets"]
const FLOAT_STATS := ["range", "aoeRadius", "aoeAngleDeg", "aoeLength", "aoeWidth"]
## Nombre visible y unidad de cada campo, para "antes → después".
const LABELS := {
	"castMs": "Lanzamiento", "cooldownMs": "Recarga", "cost": "Coste", "range": "Alcance", "aoeRadius": "Radio",
	"aoeAngleDeg": "Apertura", "aoeLength": "Largo", "aoeWidth": "Ancho", "maxTargets": "Objetivos",
}


## El hechizo con la mejora `upgrade_id` aplicada (copia; el del contenido no cambia). Si no la tiene, el mismo hechizo.
## `aura_of` (id → Dictionary) da las auras; por defecto, `Content.aura`.
static func apply(spell: Dictionary, upgrade_id: String, aura_of: Callable = Callable()) -> Dictionary:
	var upgrade := find(spell, upgrade_id)
	if upgrade.is_empty():
		return spell
	var lookup := aura_of if aura_of.is_valid() else Callable(Content, "aura")
	var s := spell.duplicate(true)
	s.erase("upgrades")
	s["appliedUpgradeId"] = upgrade_id
	var effects: Array = s.get("effects", [])
	for m: Variant in upgrade.get("mods", []):
		var md: Dictionary = m
		if md.has("addEffect"):
			effects.append((md["addEffect"] as Dictionary).duplicate(true))
		elif md.has("aura"):
			for e: Variant in effects:
				var ed: Dictionary = e
				if str(ed.get("type", "")) == "apply_aura" and str(ed.get("auraId", "")) == str(md["aura"]):
					var base: Dictionary = ed.get("auraOverride", lookup.call(str(md["aura"])))
					ed["auraOverride"] = _apply_to_aura(base.duplicate(true), md)
		elif md.has("effect"):
			for e: Variant in effects:
				var ed: Dictionary = e
				if str(ed.get("type", "")) == str(md["effect"]):
					_scale(ed, float(md.get("mult", 1.0)))
		elif md.has("stat"):
			_apply_to_spell(s, str(md["stat"]), md)
	s["effects"] = effects
	return s


## El hechizo de un mensaje (`CastStarted`, `AreaSpawn`): con su `upgradeId` si lo trae, para dibujar la forma mejorada.
static func of_message(d: Dictionary) -> Dictionary:
	var spell := Content.spell(str(d.get("spellId", "")))
	return apply(spell, str(d["upgradeId"])) if d.get("upgradeId") != null else spell


static func find(spell: Dictionary, upgrade_id: String) -> Dictionary:
	for u: Variant in spell.get("upgrades", []):
		if str((u as Dictionary).get("id", "")) == upgrade_id:
			return u
	return {}


static func _v(value: float, m: Dictionary) -> float:
	return value * float(m.get("mult", 1.0)) + float(m.get("add", 0.0))


static func _apply_to_spell(s: Dictionary, stat: String, m: Dictionary) -> void:
	if stat == "cost":
		if s.get("cost") is Dictionary:
			var cost: Dictionary = s["cost"]
			cost["amount"] = roundi(_v(float(cost.get("amount", 0)), m))
	elif stat in INT_STATS:
		s[stat] = roundi(_v(float(s.get(stat, 10 if stat == "maxTargets" else 0)), m))
	elif stat in FLOAT_STATS:
		s[stat] = _v(float(s.get(stat, 0.0)), m)


static func _apply_to_aura(a: Dictionary, m: Dictionary) -> Dictionary:
	match str(m.get("stat", "")):
		"durationMs":
			a["durationMs"] = roundi(_v(float(a.get("durationMs", 0)), m))
		"pct":
			a["pct"] = _v(float(a.get("pct", 0.0)), m)
		"amount":
			var mult := float(m.get("mult", 1.0))
			a["base"] = _v(float(a.get("base", 0.0)), m)
			a["apCoef"] = float(a.get("apCoef", 0.0)) * mult
			a["spCoef"] = float(a.get("spCoef", 0.0)) * mult
	return a


static func _scale(e: Dictionary, mult: float) -> void:
	for key: String in ["base", "apCoef", "spCoef", "weaponPct", "amount"]:
		if e.has(key):
			e[key] = float(e[key]) * mult


## Lo que cambia la mejora, en líneas "Recarga 12 s → 8 s", "+20 % de daño", "Helado: 4 s → 6 s", "Además: Congelado".
static func diff_lines(base: Dictionary, up: Dictionary, aura_of: Callable = Callable()) -> Array[String]:
	var lookup := aura_of if aura_of.is_valid() else Callable(Content, "aura")
	var out: Array[String] = []
	for key: String in LABELS.keys():
		var before := _stat(base, key)
		var after := _stat(up, key)
		if not is_equal_approx(before, after):
			out.append("%s %s → %s" % [LABELS[key], _fmt(key, before), _fmt(key, after)])
	var base_effects: Array = base.get("effects", [])
	var up_effects: Array = up.get("effects", [])
	for i: int in up_effects.size():
		var ue: Dictionary = up_effects[i]
		if i >= base_effects.size():
			out.append("Además: %s" % _effect_name(ue, lookup))
			continue
		var be: Dictionary = base_effects[i]
		if be.has("base") and float(be.get("base", 0)) != 0.0 and not is_equal_approx(float(be["base"]), float(ue.get("base", 0))):
			var pct := roundi((float(ue["base"]) / float(be["base"]) - 1.0) * 100.0)
			out.append("%s%d %% de %s" % ["+" if pct > 0 else "", pct, "cura" if str(ue.get("type", "")) == "heal" else "daño"])
		if ue.get("auraOverride") is Dictionary:
			var before_aura: Dictionary = lookup.call(str(ue.get("auraId", "")))
			var after_aura: Dictionary = ue["auraOverride"]
			var aura_name := str(before_aura.get("name", ue.get("auraId", "")))
			if int(before_aura.get("durationMs", 0)) != int(after_aura.get("durationMs", 0)):
				out.append("%s: %s s → %s s" % [aura_name, _num(int(before_aura.get("durationMs", 0)) / 1000.0), _num(int(after_aura.get("durationMs", 0)) / 1000.0)])
			if not is_equal_approx(float(before_aura.get("pct", 0.0)), float(after_aura.get("pct", 0.0))):
				out.append("%s: %d %% → %d %%" % [aura_name, roundi(float(before_aura.get("pct", 0.0)) * 100.0), roundi(float(after_aura.get("pct", 0.0)) * 100.0)])
			if not is_equal_approx(float(before_aura.get("base", 0.0)), float(after_aura.get("base", 0.0))):
				var gain := roundi((float(after_aura.get("base", 0.0)) / maxf(0.001, float(before_aura.get("base", 0.0))) - 1.0) * 100.0)
				out.append("%s: %s%d %%" % [aura_name, "+" if gain > 0 else "", gain])
	return out


static func _stat(s: Dictionary, key: String) -> float:
	if key == "cost":
		return float((s.get("cost") as Dictionary).get("amount", 0)) if s.get("cost") is Dictionary else 0.0
	return float(s.get(key, 10 if key == "maxTargets" else 0))


static func _fmt(key: String, v: float) -> String:
	if key in ["castMs", "cooldownMs"]:
		return "%s s" % _num(v / 1000.0)
	if key == "aoeAngleDeg":
		return "%s°" % _num(v)
	if key in ["range", "aoeRadius", "aoeLength", "aoeWidth"]:
		return _num(v)
	return "%d" % roundi(v)


static func _effect_name(e: Dictionary, lookup: Callable) -> String:
	if str(e.get("type", "")) == "apply_aura":
		var a: Dictionary = lookup.call(str(e.get("auraId", "")))
		return str(a.get("name", e.get("auraId", "")))
	return str(e.get("type", ""))


## Mismo formato que el resto de tooltips (TooltipBuilder).
static func _num(v: float) -> String:
	return TooltipBuilder._num(v)
