class_name TooltipBuilder
## Tooltip de item (HU-053, skill inventory-items §Cliente): nombre en color de rareza, slot y tipo, afinidad de mi clase con
## multiplicador, valores ya multiplicados por afinidad (base entre paréntesis si difiere), DPS, escuela del básico, armadura,
## stats, nivel requerido (rojo si no alcanza), precio de venta y comparación con lo equipado (▲ verde / ▼ rojo). Texto BBCode.

const RARITY_COLORS := {"junk": "#9d9d9d", "common": "#ffffff", "uncommon": "#1eff00", "rare": "#0070dd", "epic": "#a335ee"}
const AFFINITY_COLORS := {"alta": "#44dd44", "media": "#ffd54a", "baja": "#ff5555"}
const STAT_NAMES := {"str": "Fuerza", "agi": "Agilidad", "int": "Intelecto", "spi": "Espíritu", "sta": "Aguante"}
## La fuente pixel no tiene cursiva: las descripciones van en un color atenuado.
const MUTED := "#c7b8a0"
const SLOT_NAMES := {"head": "Cabeza", "neck": "Cuello", "chest": "Pecho", "hands": "Manos", "legs": "Piernas", "feet": "Pies", "ring": "Anillo", "main_hand": "Mano principal", "off_hand": "Mano secundaria"}


## Afinidad (alta/media/baja) de una clase con el tipo del item, leyendo rules.affinity.
static func affinity_of(class_id: String, item: Dictionary) -> String:
	var item_type := str(item.get("weaponType", item.get("armorType", "")))
	if item_type.is_empty():
		return ""
	var by_class: Dictionary = Content.rule("affinity", "byClass", {})
	var row: Dictionary = by_class.get(class_id, {})
	return str(row.get(item_type, "baja"))


static func affinity_mult(affinity: String) -> float:
	if affinity.is_empty():
		return 1.0
	var mults: Dictionary = Content.rule("affinity", "multipliers", {})
	return float(mults.get(affinity, 0.7))


## `buy_price` ≥ 0 añade el precio de compra (tienda, HU-055).
static func build(item: Dictionary, qty: int, class_id: String, level: int, equipped: Dictionary = {}, buy_price: int = -1) -> String:
	if item.is_empty():
		return ""
	var lines: Array[String] = []
	var rarity := str(item.get("rarity", "common"))
	var color: String = RARITY_COLORS.get(rarity, "#ffffff")
	var name := str(item.get("name", item.get("id", "")))
	lines.append("[color=%s][b]%s[/b][/color]%s" % [color, name, (" ×%d" % qty) if qty > 1 else ""])
	var kind := str(item.get("type", ""))
	if item.has("slot"):
		var gear := str(item.get("weaponType", item.get("armorType", "")))
		lines.append("%s · %s" % [str(SLOT_NAMES.get(str(item["slot"]), str(item["slot"]))), UiText.gear_type(gear) if not gear.is_empty() else UiText.item_type(kind)])
	else:
		lines.append(UiText.item_type(kind))

	var affinity := affinity_of(class_id, item)
	var mult := affinity_mult(affinity)
	if not affinity.is_empty():
		lines.append("[color=%s]Afinidad: %s (×%s)[/color]" % [AFFINITY_COLORS.get(affinity, "#ffffff"), affinity, _mult(mult)])

	var haste := _haste(class_id)
	if item.has("damageMin"):
		var dmin := float(item.get("damageMin", 0)) * mult
		var dmax := float(item.get("damageMax", 0)) * mult
		var speed := float(item.get("speedMs", 1000)) / haste / 1000.0
		var base_txt := "" if is_equal_approx(mult, 1.0) and is_equal_approx(haste, 1.0) else " (base %d–%d, %.1f s)" % [int(item.get("damageMin", 0)), int(item.get("damageMax", 0)), float(item.get("speedMs", 1000)) / 1000.0]
		lines.append("Daño %s–%s · %.2f s%s" % [_num(dmin), _num(dmax), speed, base_txt])
		lines.append("DPS %.1f · básico %s" % [dps(item, mult, haste), "mágico" if str(item.get("scaling", "str")) == "int" else "físico"])
	if int(item.get("armor", 0)) > 0:
		lines.append("Armadura %s%s" % [_num(float(item["armor"]) * mult), "" if is_equal_approx(mult, 1.0) else " (base %d)" % int(item["armor"])])
	if int(item.get("spellPower", 0)) > 0:
		lines.append("+%s poder de hechizo%s" % [_num(float(item["spellPower"]) * mult), "" if is_equal_approx(mult, 1.0) else " (base %d)" % int(item["spellPower"])])
	var stats: Dictionary = item.get("stats", {}) if item.get("stats") != null else {}
	for stat: String in stats.keys():
		var v := float(stats[stat]) * mult
		lines.append("+%s %s%s" % [_num(v), str(STAT_NAMES.get(stat, stat)), "" if is_equal_approx(mult, 1.0) else " (base %d)" % int(stats[stat])])
	var level_req := int(item.get("levelReq", 1))
	if level_req > 1:
		lines.append(("[color=#ff5555]Requiere nivel %d[/color]" if level < level_req else "Requiere nivel %d") % level_req)
	if item.has("description"):
		lines.append("[color=%s]%s[/color]" % [MUTED, str(item["description"])])
	var use_cd := int(item.get("useCooldownMs", 0))
	if use_cd >= 2000:  # el segundo del pan no merece línea
		lines.append("Recarga %s s (compartida con las demás de su tipo)" % _num(use_cd / 1000.0))
	if buy_price >= 0:
		lines.append("[color=#ffdb6b]Compra: %s[/color]" % MoneyFormat.format(buy_price))
	var sell := int(item.get("sellPrice", 0))
	lines.append("Venta: %s" % (MoneyFormat.format(sell) if sell > 0 else "no se puede vender"))

	if not equipped.is_empty() and equipped.get("id") != item.get("id"):
		lines.append_array(compare(item, equipped, class_id))
	return "\n".join(lines)


## Tooltip de hechizo: nombre, coste, lanzamiento, recarga, alcance, radio y descripción. Texto BBCode.
static func build_spell(spell: Dictionary) -> String:
	if spell.is_empty():
		return ""
	var lines: Array[String] = ["[color=#ffdb6b][b]%s[/b][/color]" % str(spell.get("name", spell.get("id", "")))]
	var facts: Array[String] = []
	var cost: Dictionary = spell.get("cost", {}) if spell.get("cost") != null else {}
	if int(cost.get("amount", 0)) > 0:
		facts.append("%d de %s" % [int(cost["amount"]), UiText.resource(str(cost.get("resource", ""))).to_lower()])
	var cast_ms := int(spell.get("castMs", 0))
	facts.append("Instantáneo" if cast_ms <= 0 else "Lanzamiento %s s" % _num(cast_ms / 1000.0))
	if int(spell.get("cooldownMs", 0)) > 0:
		facts.append("Recarga %s s" % _num(int(spell["cooldownMs"]) / 1000.0))
	lines.append(" · ".join(facts))
	if float(spell.get("range", 0)) > 0.0:
		lines.append("Alcance %s casillas%s" % [_num(float(spell["range"])), (" · radio %s" % _num(float(spell["aoeRadius"]))) if spell.get("aoeRadius") != null else ""])
	if spell.has("description"):
		lines.append("[color=%s]%s[/color]" % [MUTED, str(spell["description"])])
	return "\n".join(lines)


## DPS del arma con afinidad y haste: media del daño / (speedMs / haste / 1000).
static func dps(item: Dictionary, mult: float, haste: float) -> float:
	var avg := (float(item.get("damageMin", 0)) + float(item.get("damageMax", 0))) / 2.0 * mult
	var speed := float(item.get("speedMs", 1000)) / haste / 1000.0
	return avg / speed if speed > 0.0 else 0.0


## Diferencias con lo equipado (▲ verde / ▼ rojo), stats, armadura, poder de hechizo y DPS.
static func compare(item: Dictionary, equipped: Dictionary, class_id: String) -> Array[String]:
	var out: Array[String] = []
	var m1 := affinity_mult(affinity_of(class_id, item))
	var m2 := affinity_mult(affinity_of(class_id, equipped))
	var haste := _haste(class_id)
	var s1: Dictionary = item.get("stats", {}) if item.get("stats") != null else {}
	var s2: Dictionary = equipped.get("stats", {}) if equipped.get("stats") != null else {}
	for stat: String in STAT_NAMES.keys():
		var diff := float(s1.get(stat, 0)) * m1 - float(s2.get(stat, 0)) * m2
		if not is_zero_approx(diff):
			out.append(_delta(diff, str(STAT_NAMES[stat])))
	var armor_diff := float(item.get("armor", 0)) * m1 - float(equipped.get("armor", 0)) * m2
	if not is_zero_approx(armor_diff):
		out.append(_delta(armor_diff, "Armadura"))
	var sp_diff := float(item.get("spellPower", 0)) * m1 - float(equipped.get("spellPower", 0)) * m2
	if not is_zero_approx(sp_diff):
		out.append(_delta(sp_diff, "Poder de hechizo"))
	if item.has("damageMin") and equipped.has("damageMin"):
		var dps_diff := dps(item, m1, haste) - dps(equipped, m2, haste)
		if not is_zero_approx(dps_diff):
			out.append(_delta(dps_diff, "DPS"))
	return out


static func _delta(diff: float, label: String) -> String:
	var sign := "▲ +" if diff > 0.0 else "▼ "
	var color := "#44dd44" if diff > 0.0 else "#ff5555"
	return "[color=%s]%s%s %s[/color]" % [color, sign, _num(diff), label]


static func _haste(class_id: String) -> float:
	var scaling: Dictionary = Content.rule("classScaling", class_id, {})
	return float(scaling.get("haste", 1.0))


## Multiplicador de afinidad con hasta dos decimales (HU-053 CA3b): ×1, ×0.85, ×0.7.
static func _mult(v: float) -> String:
	var text := "%.2f" % v
	while text.ends_with("0"):
		text = text.left(text.length() - 1)
	return text.trim_suffix(".")


static func _num(v: float) -> String:
	return str(int(round(v))) if is_equal_approx(v, round(v)) else "%.1f" % v
