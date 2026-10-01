extends Node
## Estado espejo del servidor (skill godot-client §Autoloads 5). La UI solo escucha a GameState; nunca se calcula nada aquí.

signal stats_changed
signal inventory_changed
signal target_changed(entity_id: int)
signal map_changed(map_id: String)
signal vitals_changed  ## vida/recurso propios (Snapshot.self)
signal auras_changed(entity_id: int)
signal cooldowns_changed
signal cast_changed  ## casteo propio empezó/terminó
signal died(killer_id: int)
signal respawned

var self_id: int = -1
var character_name: String = ""
var class_id: String = ""
var level: int = 1
var map_id: String = ""
var stats: Dictionary = {}
var inventory: Array = []
var equipment: Array = []
var hotbar: Array = []
var known_spells: Array[String] = []
var target_id: int = -1
var party: Dictionary = {}
var cooldowns: Dictionary = {}  # spellId → msec de fin (predicho por el cliente, corregido por `Cooldown`)
var gcd_end_ms: int = 0
var rules_hash: String = ""
## Vida y recurso propios (espejo del Snapshot.self y del Welcome.self).
var hp: int = 0
var max_hp: int = 0
var resource: int = 0
var max_resource: int = 0
var resource_kind: String = "mana"  # mana | rage | energy
var is_dead: bool = false
## Casteo propio en curso: {spellId, startedMs, durationMs} o vacío.
var own_cast: Dictionary = {}
## Auras por entidad: entity_id → Array de {auraId, casterId, stacks, endsMs}.
var auras: Dictionary = {}
## Ticket obtenido en la selección de personaje; lo consume la escena World al conectar (HU-014).
var pending_ticket: String = ""
## Id del personaje elegido; la escena World lo usa para pedir tickets nuevos al reconectar (HU-025).
var pending_character_id: String = ""


func _ready() -> void:
	Net.register_handler("Welcome", _on_welcome)
	Net.register_handler("StatsUpdate", _on_stats_update)
	Net.register_handler("InventoryUpdate", _on_inventory_update)
	Net.register_handler("ChangeMap", _on_change_map)
	Net.register_handler("Cooldown", _on_cooldown)
	Net.register_handler("AuraApplied", _on_aura_applied)
	Net.register_handler("AuraRemoved", _on_aura_removed)
	Net.register_handler("CastStarted", _on_cast_started)
	Net.register_handler("CastEnded", _on_cast_ended)
	Net.register_handler("Died", _on_died)
	Net.snapshot.connect(_on_snapshot)


func set_target(entity_id: int) -> void:
	if target_id == entity_id:
		return
	target_id = entity_id
	target_changed.emit(entity_id)
	EventBus.target_changed.emit(entity_id)


func _on_welcome(d: Dictionary) -> void:
	self_id = int(d.get("selfId", -1))
	map_id = str(d.get("mapId", ""))
	var self_state: Dictionary = d.get("self", {})
	character_name = str(self_state.get("name", ""))
	class_id = str(self_state.get("classId", ""))
	level = int(self_state.get("level", 1))
	hp = int(self_state.get("hp", 0))
	max_hp = int(self_state.get("maxHp", 0))
	resource = int(self_state.get("res", 0))
	max_resource = int(self_state.get("maxRes", 0))
	resource_kind = str(self_state.get("resource", "mana"))
	is_dead = hp <= 0
	own_cast = {}
	auras.clear()
	cooldowns.clear()
	gcd_end_ms = 0
	target_id = -1
	vitals_changed.emit()
	inventory = d.get("inventory", [])
	equipment = d.get("equipment", [])
	hotbar = d.get("hotbar", [])
	known_spells.clear()
	for s: Variant in d.get("knownSpells", []):
		known_spells.append(str(s))
	rules_hash = str(d.get("rulesHash", ""))
	stats_changed.emit()
	inventory_changed.emit()
	map_changed.emit(map_id)


func _on_stats_update(d: Dictionary) -> void:
	stats = d
	level = int(d.get("level", level))
	stats_changed.emit()


func _on_inventory_update(d: Dictionary) -> void:
	inventory = d.get("bag", inventory)
	equipment = d.get("equipment", equipment)
	inventory_changed.emit()


func _on_change_map(d: Dictionary) -> void:
	map_id = str(d.get("mapId", map_id))
	map_changed.emit(map_id)


func _on_snapshot(d: Dictionary) -> void:
	var self_state: Dictionary = d.get("self", {})
	var new_hp := int(self_state.get("hp", hp))
	var was_dead := is_dead
	hp = new_hp
	max_hp = int(self_state.get("maxHp", max_hp))
	resource = int(self_state.get("res", resource))
	max_resource = int(self_state.get("maxRes", max_resource))
	is_dead = hp <= 0
	if was_dead and not is_dead:
		respawned.emit()
	vitals_changed.emit()
	# Objetivo que sale de la AOI: deseleccionar (HU-030 CA5); si murió se mantiene (botín, HU-050).
	if target_id > 0:
		var still := false
		for e: Variant in d.get("ents", []):
			if int((e as Dictionary).get("id", -1)) == target_id:
				still = true
				break
		if not still:
			set_target(-1)


## GCD predicho al enviar un CastSpell (skill combat-system §Cliente); `Cooldown` del servidor lo corrige.
func predict_gcd(spell_id: String) -> void:
	var spell := Content.spell(spell_id)
	if spell.is_empty() or not bool(spell.get("triggersGcd", true)):
		return
	gcd_end_ms = Time.get_ticks_msec() + int(Content.rule("combat", "gcdMs", 1000))
	cooldowns_changed.emit()


## Revierte la predicción cuando el servidor rechaza el hechizo.
func revert_prediction(spell_id: String) -> void:
	cooldowns.erase(spell_id)
	gcd_end_ms = 0
	cooldowns_changed.emit()


func _on_cooldown(d: Dictionary) -> void:
	if d.get("gcdMs") != null:
		gcd_end_ms = Time.get_ticks_msec() + int(d["gcdMs"])
	if d.get("spellId") != null and d.get("remainingMs") != null:
		cooldowns[str(d["spellId"])] = Time.get_ticks_msec() + int(d["remainingMs"])
	cooldowns_changed.emit()


func cooldown_remaining_ms(spell_id: String) -> int:
	return maxi(0, int(cooldowns.get(spell_id, 0)) - Time.get_ticks_msec())


func gcd_remaining_ms() -> int:
	return maxi(0, gcd_end_ms - Time.get_ticks_msec())


func _on_aura_applied(d: Dictionary) -> void:
	var target := int(d.get("targetId", -1))
	var list: Array = auras.get(target, [])
	var aura_id := str(d.get("auraId", ""))
	var caster: Variant = d.get("casterId")
	var entry := {"auraId": aura_id, "casterId": caster, "stacks": int(d.get("stacks", 1)), "endsMs": Time.get_ticks_msec() + int(d.get("durationMs", 0))}
	var replaced := false
	for i: int in list.size():
		var a: Dictionary = list[i]
		if str(a["auraId"]) == aura_id and a["casterId"] == caster:
			list[i] = entry
			replaced = true
			break
	if not replaced:
		list.append(entry)
	auras[target] = list
	auras_changed.emit(target)


func _on_aura_removed(d: Dictionary) -> void:
	var target := int(d.get("targetId", -1))
	var list: Array = auras.get(target, [])
	var aura_id := str(d.get("auraId", ""))
	var caster: Variant = d.get("casterId")
	for i: int in range(list.size() - 1, -1, -1):
		var a: Dictionary = list[i]
		if str(a["auraId"]) == aura_id and a["casterId"] == caster:
			list.remove_at(i)
	auras[target] = list
	auras_changed.emit(target)


func auras_of(entity_id: int) -> Array:
	return auras.get(entity_id, [])


func _on_cast_started(d: Dictionary) -> void:
	if int(d.get("casterId", -1)) != self_id:
		return
	own_cast = {"spellId": str(d.get("spellId", "")), "startedMs": Time.get_ticks_msec(), "durationMs": int(d.get("durationMs", 0))}
	cast_changed.emit()


func _on_cast_ended(d: Dictionary) -> void:
	if int(d.get("casterId", -1)) != self_id:
		return
	own_cast = {}
	cast_changed.emit()


func _on_died(d: Dictionary) -> void:
	is_dead = true
	hp = 0
	own_cast = {}
	auras.erase(self_id)
	died.emit(int(d.get("killerId", -1)) if d.get("killerId") != null else -1)
	vitals_changed.emit()
