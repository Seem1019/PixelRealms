extends Node
## Estado espejo del servidor (skill godot-client §Autoloads 5). La UI solo escucha a GameState; nunca se calcula nada aquí.

signal stats_changed
signal inventory_changed
signal target_changed(entity_id: int)
signal map_changed(map_id: String)

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
var cooldowns: Dictionary = {}  # spellId → msec de fin (predicho)
var rules_hash: String = ""
## Ticket obtenido en la selección de personaje; lo consume la escena World al conectar (HU-014).
var pending_ticket: String = ""


func _ready() -> void:
	Net.register_handler("Welcome", _on_welcome)
	Net.register_handler("StatsUpdate", _on_stats_update)
	Net.register_handler("InventoryUpdate", _on_inventory_update)
	Net.register_handler("ChangeMap", _on_change_map)


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
