class_name AuraStyle
extends RefCounted
## Cómo se ve un aura (HU-098): categoría según `kind` e `isDebuff` de content/auras.json, color del marco (verde
## beneficiosa, rojo perjudicial), insignia de tipo de 5×5 px y el resumen de efectos visibles sobre una entidad.
## Puro y estático: lo usan el HUD (casillas de aura) y el mundo (indicadores sobre la entidad y mini-íconos en la placa).

const BUFF_FRAME := Color("1ebc73")
const DEBUFF_FRAME := Color("ea4f36")

## Insignias de 5×5 (filas de arriba abajo, "#" = píxel) y su color, por categoría.
const BADGES := {
	"stun": {"color": Color("f9c22b"), "rows": ["..#..", ".###.", "#####", ".###.", "#.#.#"]},
	"root": {"color": Color("8fd3ff"), "rows": ["#.#.#", ".###.", "#####", ".###.", "#.#.#"]},
	"slow": {"color": Color("8fd3ff"), "rows": ["..#..", "..#..", "#####", ".###.", "..#.."]},
	"dot": {"color": Color("ea4f36"), "rows": ["..#..", ".###.", "#####", "#####", ".###."]},
	"hot": {"color": Color("91db69"), "rows": ["..#..", "..#..", "#####", "..#..", "..#.."]},
	"shield": {"color": Color("fbf3e0"), "rows": ["#####", "#####", "#####", ".###.", "..#.."]},
	"buff": {"color": Color("f9c22b"), "rows": ["..#..", ".###.", "#####", "..#..", "..#.."]},
	"debuff": {"color": Color("ea4f36"), "rows": ["..#..", "..#..", "#####", ".###.", "..#.."]},
}

## Nombres en español para el tooltip.
const CATEGORY_NAMES := {
	"stun": "Aturdido", "root": "Inmovilizado", "slow": "Ralentizado", "dot": "Daño en el tiempo",
	"hot": "Curación en el tiempo", "shield": "Escudo", "buff": "Mejora", "debuff": "Perjuicio",
}


## Categoría visible del aura: el tipo de control o de efecto, y para modificadores de estadística, mejora o perjuicio.
static func category(def: Dictionary) -> String:
	var kind := str(def.get("kind", ""))
	if kind in ["stun", "root", "slow", "dot", "hot", "shield"]:
		return kind
	if kind == "stat_mod" and float(_mods(def).get("speedPct", 0.0)) < 0.0:
		return "slow"
	return "debuff" if is_debuff(def) else "buff"


static func is_debuff(def: Dictionary) -> bool:
	return bool(def.get("isDebuff", false))


static func frame_color(def: Dictionary) -> Color:
	return DEBUFF_FRAME if is_debuff(def) else BUFF_FRAME


## Píxeles encendidos de la insignia (coordenadas 0..4).
static func badge_pixels(cat: String) -> Array[Vector2i]:
	var out: Array[Vector2i] = []
	var rows: Array = (BADGES.get(cat, BADGES["buff"]) as Dictionary)["rows"]
	for y: int in rows.size():
		var row := str(rows[y])
		for x: int in row.length():
			if row[x] == "#":
				out.append(Vector2i(x, y))
	return out


static func badge_color(cat: String) -> Color:
	return (BADGES.get(cat, BADGES["buff"]) as Dictionary)["color"]


## Dibuja la insignia con contorno oscuro en `origin` (esquina superior izquierda) sobre cualquier CanvasItem.
static func draw_badge(ci: CanvasItem, origin: Vector2, cat: String) -> void:
	var px := badge_pixels(cat)
	for p: Vector2i in px:
		ci.draw_rect(Rect2(origin + Vector2(p) - Vector2.ONE, Vector2(3, 3)), UiTheme.OUTLINE)
	var c := badge_color(cat)
	for p: Vector2i in px:
		ci.draw_rect(Rect2(origin + Vector2(p), Vector2.ONE), c)


## Resumen de la lista de auras de una entidad ({auraId, …} de GameState): qué efectos de control se ven sobre ella y
## los mini-íconos de la placa (perjudiciales primero), con `defs` = auraId → definición (Content.aura).
static func summarize(list: Array, defs: Callable) -> Dictionary:
	var s := {"stunned": false, "rooted": false, "slowed": false, "shielded": false, "icons": []}
	var debuffs: Array = []
	var buffs: Array = []
	for a: Variant in list:
		var def: Dictionary = defs.call(str((a as Dictionary).get("auraId", "")))
		var cat := category(def)
		match cat:
			"stun": s["stunned"] = true
			"root": s["rooted"] = true
			"slow": s["slowed"] = true
			"shield": s["shielded"] = true
		var icon := {"icon": str(def.get("icon", "")), "debuff": is_debuff(def), "category": cat}
		(debuffs if is_debuff(def) else buffs).append(icon)
	s["icons"] = debuffs + buffs
	return s


static func _mods(def: Dictionary) -> Dictionary:
	return def.get("mods", {}) if def.get("mods") is Dictionary else {}
