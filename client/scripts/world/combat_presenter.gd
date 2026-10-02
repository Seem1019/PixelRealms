class_name CombatPresenter
extends Node
## Traduce los mensajes de combate del servidor a animaciones del cuerpo y efectos (HU-090, HU-091). Solo refleja: el daño,
## los objetivos y los tiempos los decide el servidor; aquí únicamente se elige qué dibujar y cuándo.
## - CastStarted{durationMs > 0} → animación de casteo mirando al objetivo y brillo bajo los pies hasta CastEnded.
## - CastEnded{done} → ataque de un disparo; si el hechizo tiene proyectil sale hacia el objetivo y tarda lo mismo que en el
##   servidor (distancia / velocidad); si es un área, estallidos sobre la marca del suelo.
## - CombatEvents → número flotante, golpe recibido (destello + 1 px) e impacto según la escuela. Los básicos sin hechizo
##   hacen atacar al src; a distancia viajan como proyectil 150-250 ms y el número sale al llegar. Las entradas de un
##   proyectil que aún vuela esperan a que llegue.
## El jugador propio usa los mismos mensajes que los demás.

## Posición de los pies de una entidad (Vector2.INF si no está en la AOI).
var entity_pos: Callable
## EntityVisual de una entidad o null.
var visual_of: Callable
## Clase (jugadores) o plantilla (monstruos) de una entidad, para el proyectil de su ataque básico a distancia.
var archetype_of: Callable
var floating: FloatingText
var vfx: VfxLayer

var _casts: Dictionary = {}  # caster → {spell, target_id, target_pos}
## Proyectiles en vuelo: "src:dst" → {arrive_ms, queued: Array[Dictionary]}
var _inflight: Dictionary = {}
## Proyectiles que ya llegaron (para no repetir el impacto cuando el número llega después): "src:dst" → ms
var _landed: Dictionary = {}
const LANDED_MEMORY_MS := 1500


func _pos(id: int) -> Vector2:
	return entity_pos.call(id) if entity_pos.is_valid() else Vector2.INF


func _visual(id: int) -> EntityVisual:
	return visual_of.call(id) as EntityVisual if visual_of.is_valid() else null


func cast_started(d: Dictionary) -> void:
	var caster := int(d.get("casterId", -1))
	var spell := Content.spell(str(d.get("spellId", "")))
	var target_id := int(d.get("targetId", -1)) if d.get("targetId") != null else -1
	var target_pos := Vector2.INF
	if d.get("targetPos") is Dictionary:
		var tp: Dictionary = d["targetPos"]
		target_pos = Vector2(float(tp.get("x", 0)), float(tp.get("y", 0)))
	_casts[caster] = {"spell": spell, "target_id": target_id, "target_pos": target_pos}
	if int(d.get("durationMs", 0)) <= 0:
		return
	var v := _visual(caster)
	if v != null:
		v.begin_cast(_toward(caster, target_id, target_pos))
	if vfx != null and not spell.is_empty():
		vfx.play_loop(VfxCatalog.cast_sheet(spell), "cast:%d" % caster, func() -> Vector2: return _pos(caster))


func cast_ended(d: Dictionary) -> void:
	var caster := int(d.get("casterId", -1))
	if vfx != null:
		vfx.stop("cast:%d" % caster)
	var v := _visual(caster)
	if v != null:
		v.end_cast()
	var info: Dictionary = _casts.get(caster, {})
	_casts.erase(caster)
	if str(d.get("result", "")) != "done":
		return
	var spell: Dictionary = info.get("spell", Content.spell(str(d.get("spellId", ""))))
	var target_id := int(info.get("target_id", -1))
	var target_pos: Vector2 = info.get("target_pos", Vector2.INF)
	if v != null and str(spell.get("targeting", "")) != "self":
		v.play_attack(_toward(caster, target_id, target_pos))
	var origin := _pos(caster)
	if origin == Vector2.INF or vfx == null:
		return
	var proj := VfxCatalog.projectile_sheet(spell)
	if not proj.is_empty() and target_id > 0 and _pos(target_id) != Vector2.INF:
		var dist := origin.distance_to(_pos(target_id))
		var travel := maxi(VfxCatalog.BASIC_TRAVEL_MIN_MS, roundi(1000.0 * dist / (16.0 * maxf(0.1, VfxCatalog.projectile_speed(spell)))))
		_fly(proj, caster, target_id, travel, spell)
	elif VfxCatalog.is_area(spell):
		var center := target_pos if target_pos != Vector2.INF else origin
		var sheet := "area_%s" % VfxCatalog.element(spell)
		var i := 0
		for p: Vector2 in VfxCatalog.area_points(spell, origin, center):
			vfx.play_once(sheet, p, i * VfxCatalog.AREA_STAGGER_MS)
			i += 1


func combat_events(d: Dictionary) -> void:
	var attacked := {}
	for e: Variant in d.get("e", []):
		var ed: Dictionary = e
		var src := int(ed.get("src", -1))
		var dst := int(ed.get("dst", -1))
		var spell_id: Variant = ed.get("spellId")
		var basic := spell_id == null or str(spell_id).is_empty()
		var key := "%d:%d" % [src, dst]
		if basic and src != dst and not attacked.has(src):
			attacked[src] = true
			var sv := _visual(src)
			if sv != null and _pos(src) != Vector2.INF and _pos(dst) != Vector2.INF:
				sv.play_attack(_pos(dst) - _pos(src))
		if _inflight.has(key):
			(_inflight[key]["queued"] as Array).append(ed)
			continue
		if basic and _ranged_basic(src, dst) and vfx != null:
			var dist := _pos(src).distance_to(_pos(dst))
			var travel := clampi(roundi(dist * 1000.0 / (16.0 * 14.0)), VfxCatalog.BASIC_TRAVEL_MIN_MS, VfxCatalog.BASIC_TRAVEL_MAX_MS)
			_fly(_basic_projectile(src), src, dst, travel, {})
			(_inflight[key]["queued"] as Array).append(ed)
			continue
		_show(ed, not _landed.has(key))


## Proyectil de src a dst: al llegar, impacto y las entradas que esperaban.
func _fly(sheet: String, src: int, dst: int, travel_ms: int, spell: Dictionary) -> void:
	var key := "%d:%d" % [src, dst]
	_inflight[key] = {"arrive_ms": Time.get_ticks_msec() + travel_ms, "queued": []}
	var last := _pos(dst)
	var follow := func() -> Vector2:
		var p := _pos(dst)
		if p != Vector2.INF:
			last = p
		return last + Vector2(0, -8)
	vfx.launch(sheet, _pos(src) + Vector2(0, -8), follow, travel_ms, func() -> void: _arrive(key, dst, spell))


func _arrive(key: String, dst: int, spell: Dictionary) -> void:
	var entry: Dictionary = _inflight.get(key, {})
	_inflight.erase(key)
	_landed[key] = Time.get_ticks_msec()
	var queued: Array = entry.get("queued", [])
	if queued.is_empty():
		# El número aún no llegó (el servidor lo resuelve a la vez): solo el impacto.
		var pos := _pos(dst)
		var sheet := VfxCatalog.impact_sheet(spell, "dmg", true)
		if pos != Vector2.INF and vfx != null and not sheet.is_empty():
			vfx.play_once(sheet, pos + Vector2(0, -8))
		return
	for ed: Variant in queued:
		_show(ed as Dictionary, true)


## Número, golpe recibido e impacto de una entrada.
func _show(ed: Dictionary, with_impact: bool) -> void:
	var src := int(ed.get("src", -1))
	var dst := int(ed.get("dst", -1))
	var pos := _pos(dst)
	if pos == Vector2.INF:
		return
	var kind := str(ed.get("kind", ""))
	if floating != null:
		floating.show_event(dst, kind, int(ed.get("amount", 0)), bool(ed.get("crit", false)), pos)
	var v := _visual(dst)
	var spell := Content.spell(str(ed.get("spellId", ""))) if ed.get("spellId") != null else {}
	var aura_tick := ed.get("spellId") != null and spell.is_empty()
	if v != null:
		if kind == "dmg" and int(ed.get("amount", 0)) > 0:
			var from := _pos(src)
			v.hurt(pos - from if from != Vector2.INF and src != dst else Vector2.ZERO)
		elif kind == "heal":
			v.flash(Color(0.6, 2.2, 0.6))
	if not with_impact or vfx == null or (aura_tick and kind != "heal"):
		return
	var ranged := _pos(src) != Vector2.INF and _pos(src).distance_to(pos) > VfxCatalog.RANGED_BASIC_MIN_TILES * 16.0
	var sheet := VfxCatalog.impact_sheet(spell, kind, ranged)
	if not sheet.is_empty():
		vfx.play_once(sheet, pos + Vector2(0, -8 if sheet != "heal" else -10))


func _ranged_basic(src: int, dst: int) -> bool:
	var a := _pos(src)
	var b := _pos(dst)
	return a != Vector2.INF and b != Vector2.INF and a.distance_to(b) > VfxCatalog.RANGED_BASIC_MIN_TILES * 16.0


func _basic_projectile(src: int) -> String:
	var arch := str(archetype_of.call(src)) if archetype_of.is_valid() else ""
	return str(VfxCatalog.RANGED_BASIC.get(arch, "arrow"))


func _toward(caster: int, target_id: int, target_pos: Vector2) -> Vector2:
	var from := _pos(caster)
	if from == Vector2.INF:
		return Vector2.ZERO
	var to := _pos(target_id) if target_id > 0 else Vector2.INF
	if to == Vector2.INF:
		to = target_pos
	return Vector2.ZERO if to == Vector2.INF or to == from else to - from


func forget(entity_id: int) -> void:
	_casts.erase(entity_id)
	if vfx != null:
		vfx.stop("cast:%d" % entity_id)


func clear() -> void:
	_casts.clear()
	_inflight.clear()
	_landed.clear()
	if vfx != null:
		vfx.clear_all()


func _process(_delta: float) -> void:
	if _landed.is_empty():
		return
	var now := Time.get_ticks_msec()
	for k: Variant in _landed.keys():
		if now - int(_landed[k]) > LANDED_MEMORY_MS:
			_landed.erase(k)
