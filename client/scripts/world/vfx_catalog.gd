class_name VfxCatalog
## Qué efecto dibuja cada hechizo (HU-091), a partir de los campos que ya tiene spells.json: `projectile.sprite`, `school`,
## `targeting`, `shape`, `aoeRadius` y el tipo de sus efectos. La tabla ELEMENT_BY_SPELL solo da el color (elemento) a los
## hechizos mágicos sin proyectil, que el contenido no distingue (escarcha, fuego, luz, sombra, veneno); lo que no está en
## ella usa arcano (mágico) o acero (físico). Es del cliente y puramente visual: no cambia ninguna regla.

const ELEMENT_BY_SPELL := {
	"mage_frost_nova": "frost",
	"mage_cone_of_cold": "frost",
	"mage_flame_burst": "fire",
	"mage_burning_field": "fire",
	"mage_meteor": "fire",
	"mage_blink": "arcane",
	"priest_smite": "holy",
	"priest_heal": "holy",
	"priest_holy_pulse": "holy",
	"priest_power_shield": "holy",
	"priest_renew": "holy",
	"priest_path_of_light": "holy",
	"priest_hymn": "holy",
	"priest_desperate_prayer": "holy",
	"rogue_crippling_poison": "nature",
	"rogue_shadowstep": "shadow",
	"lich_frost_nova": "frost",
	"lich_shadow_bolt": "shadow",
}
## Proyectil (hoja de vfx/) → elemento de su impacto.
const ELEMENT_BY_PROJECTILE := {"fireball": "fire", "frostbolt": "frost", "shadow_bolt": "shadow", "arrow": "steel"}
## Ataque básico a distancia (no viaja como hechizo: el servidor lo resuelve al instante): proyectil por clase o monstruo.
const RANGED_BASIC := {"mage": "bolt_arcane", "priest": "bolt_holy", "goblin_archer": "arrow", "lesser_lich_king": "shadow_bolt"}
## A partir de qué distancia (casillas) un básico se dibuja como proyectil.
const RANGED_BASIC_MIN_TILES := 2.0
## Viaje de los proyectiles de los básicos (el número y el golpe se muestran al llegar).
const BASIC_TRAVEL_MIN_MS := 150
const BASIC_TRAVEL_MAX_MS := 250
## Estallidos por área (16×16) como mucho y escalonado entre ellos.
const AREA_MAX_BURSTS := 12
const AREA_STAGGER_MS := 25


static func element(spell: Dictionary) -> String:
	var id := str(spell.get("id", ""))
	if ELEMENT_BY_SPELL.has(id):
		return ELEMENT_BY_SPELL[id]
	var proj := projectile_sheet(spell)
	if not proj.is_empty() and ELEMENT_BY_PROJECTILE.has(proj):
		return ELEMENT_BY_PROJECTILE[proj]
	return "arcane" if str(spell.get("school", "physical")) == "magic" else "steel"


## Hoja del proyectil del hechizo ("fireball"…) o vacío.
static func projectile_sheet(spell: Dictionary) -> String:
	var p: Variant = spell.get("projectile")
	if p is Dictionary:
		return str((p as Dictionary).get("sprite", "")).trim_prefix("vfx/")
	return ""


## Velocidad del proyectil en casillas por segundo (la del servidor: el impacto llega a distancia / velocidad).
static func projectile_speed(spell: Dictionary) -> float:
	var p: Variant = spell.get("projectile")
	return float((p as Dictionary).get("speed", 10)) if p is Dictionary else 0.0


## Brillo bajo los pies mientras castea (solo hechizos con tiempo de casteo).
static func cast_sheet(spell: Dictionary) -> String:
	return "cast_%s" % element(spell)


## Impacto sobre el objetivo para una entrada de CombatEvents. Vacío = sin efecto (fallos, inmune).
static func impact_sheet(spell: Dictionary, kind: String, ranged: bool) -> String:
	match kind:
		"heal":
			return "heal"
		"dmg", "absorb":
			if spell.is_empty():
				return "impact_spark" if ranged else "impact_slash"
			var el := element(spell)
			if el == "steel":
				return "impact_spark" if not projectile_sheet(spell).is_empty() else "impact_slash"
			return "impact_%s" % el
	return ""


static func is_area(spell: Dictionary) -> bool:
	var t := str(spell.get("targeting", ""))
	return t.begins_with("ground_aoe") or t.begins_with("self_aoe")


## Puntos (px del mundo) de los estallidos de un área al resolverse: círculo, cono o línea, sin trigonometría por cuadro.
## `origin` = lanzador; `center` = targetPos (círculos de suelo) u origen (alrededor de uno mismo).
static func area_points(spell: Dictionary, origin: Vector2, center: Vector2) -> Array[Vector2]:
	var pts: Array[Vector2] = []
	var radius := float(spell.get("aoeRadius", 1.0)) * 16.0
	var shape := str(spell.get("shape", "circle"))
	var self_aoe := str(spell.get("targeting", "")).begins_with("self_aoe")
	var c := origin if self_aoe else center
	if shape == "line":
		var to := center if center != origin else origin + Vector2(radius, 0)
		var steps := clampi(int(origin.distance_to(to) / 14.0), 2, AREA_MAX_BURSTS)
		for i: int in range(1, steps + 1):
			pts.append(origin.lerp(to, float(i) / steps).round())
		return pts
	if shape == "cone":
		var dir := (center - origin).normalized() if center != origin else Vector2.RIGHT
		var base := dir.angle()
		for ring: float in [0.45, 0.8]:
			for k: int in [-2, -1, 0, 1, 2]:
				pts.append((origin + Vector2.from_angle(base + k * 0.32) * radius * ring).round())
		return pts
	pts.append(c.round())
	var rings: Array = [[0.5, 4], [0.9, 7]] if radius > 20.0 else [[0.7, 5]]
	for rd: Variant in rings:
		var ring: Array = rd
		for k: int in int(ring[1]):
			var a := TAU * k / float(ring[1]) + float(ring[0])
			pts.append((c + Vector2(cos(a), sin(a)) * radius * float(ring[0])).round())
	if pts.size() > AREA_MAX_BURSTS:
		pts.resize(AREA_MAX_BURSTS)
	return pts
