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
signal xp_changed
signal leveled_up(level: int, new_spells: Array, rank_ups: Array)
signal notice(text: String)  ## avisos cortos para el HUD ("Nuevo hechizo: …")
signal chat_received(channel: String, from: String, text: String)
signal party_changed
signal party_invited(leader: String)
signal duel_changed(state: String, opponent_id: int, winner_id: int, starts_in_ms: int)
signal trade_changed(d: Dictionary)

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
var party: Dictionary = {}  # {leader, members: [{name, entityId, classId, level, hpPct, online, mapId}]}
var duel_opponent_id: int = -1
var duel_state: String = ""
var trade: Dictionary = {}  # último TradeUpdate o vacío
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
var xp: int = 0
var xp_next: int = 0
var gold: int = 0
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
	Net.register_handler("XpGain", _on_xp_gain)
	Net.register_handler("LevelUp", _on_level_up)
	Net.register_handler("ChatMessage", _on_chat_message)
	Net.register_handler("PartyUpdate", _on_party_update)
	Net.register_handler("DuelUpdate", _on_duel_update)
	Net.register_handler("TradeUpdate", _on_trade_update)
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
	xp = int(self_state.get("xp", 0))
	xp_next = int(self_state.get("xpNext", 0))
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
	xp = int(d.get("xp", xp))
	xp_next = int(d.get("xpNext", xp_next))
	gold = int(d.get("gold", gold))
	var derived: Dictionary = d.get("derived", {})
	if not derived.is_empty():
		max_hp = int(derived.get("maxHp", max_hp))
		max_resource = int(derived.get("maxRes", max_resource))
	stats_changed.emit()
	xp_changed.emit()
	vitals_changed.emit()


func _on_inventory_update(d: Dictionary) -> void:
	inventory = d.get("bag", inventory)
	equipment = d.get("equipment", equipment)
	gold = int(d.get("gold", gold))
	inventory_changed.emit()


## Item de la bolsa por id (o vacío).
func bag_item(item_id: String) -> Dictionary:
	for it: Variant in inventory:
		if it is Dictionary and str((it as Dictionary).get("id", "")) == item_id:
			return it
	return {}


## Primera pila de la bolsa con esa plantilla (o vacío): lo que usa una casilla de utilizable.
func first_bag_item(template_id: String) -> Dictionary:
	for it: Variant in inventory:
		if it is Dictionary and str((it as Dictionary).get("templateId", "")) == template_id:
			return it
	return {}


## Cantidad total de una plantilla en la bolsa (casillas de utilizables, HU-043 CA3).
func bag_count(template_id: String) -> int:
	var n := 0
	for it: Variant in inventory:
		if it is Dictionary and str((it as Dictionary).get("templateId", "")) == template_id:
			n += int((it as Dictionary).get("qty", 0))
	return n


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


## ¿Estoy en el tope de nivel de la fase? (rules.progression.levelCapByPhase[world.currentPhase − 1], HU-040 CA4)
func at_level_cap() -> bool:
	var caps: Variant = Content.rule("progression", "levelCapByPhase", [])
	var phase := int(Content.rule("world", "currentPhase", 1))
	if caps is Array and phase - 1 < (caps as Array).size():
		return level >= int((caps as Array)[phase - 1])
	return xp_next <= 0


func _on_xp_gain(d: Dictionary) -> void:
	xp += int(d.get("amount", 0))
	xp_changed.emit()


## HU-041 CA2: los hechizos nuevos van a la primera casilla libre de hechizos (SetHotbar) con aviso; CA3b: aviso de rangos.
func _on_level_up(d: Dictionary) -> void:
	level = int(d.get("level", level))
	var new_spells: Array = d.get("newSpells", [])
	var rank_ups: Array = d.get("rankUps", []) if d.get("rankUps") != null else []
	var spell_slots := int(Content.rule("loadout", "spellSlots", 4))
	for s: Variant in new_spells:
		var spell_id := str(s)
		if not known_spells.has(spell_id):
			known_spells.append(spell_id)
		var free := _first_free_slot(spell_slots)
		if free >= 0:
			hotbar.append({"slot": free, "kind": "spell", "ref": spell_id})
			Net.send("SetHotbar", {"slot": free, "kind": "spell", "ref": spell_id})
		notice.emit("Nuevo hechizo: %s" % str(Content.spell(spell_id).get("name", spell_id)))
	if not rank_ups.is_empty():
		notice.emit("Tus hechizos suben de rango (+%d %%)" % roundi(float(Content.rule("progression", "spellRankBonusPct", 0.15)) * 100.0))
	notice.emit("¡Nivel %d!" % level)
	stats_changed.emit()
	leveled_up.emit(level, new_spells, rank_ups)


func _first_free_slot(spell_slots: int) -> int:
	for slot: int in spell_slots:
		var taken := false
		for h: Variant in hotbar:
			if int((h as Dictionary).get("slot", -1)) == slot:
				taken = true
				break
		if not taken:
			return slot
	return -1


func _on_chat_message(d: Dictionary) -> void:
	chat_received.emit(str(d.get("channel", "say")), str(d.get("from", "")), str(d.get("text", "")))


## PartyUpdate con members vacío y leader ≠ "" = invitación pendiente (HU-061 CA1); vacío del todo = sin grupo.
func _on_party_update(d: Dictionary) -> void:
	var members: Array = d.get("members", [])
	var leader := str(d.get("leader", ""))
	if members.is_empty() and not leader.is_empty():
		party_invited.emit(leader)
		return
	party = {"leader": leader, "members": members}
	party_changed.emit()


func in_party() -> bool:
	return not party.is_empty() and (party.get("members", []) as Array).size() > 1


func party_member_names() -> Array[String]:
	var out: Array[String] = []
	for m: Variant in party.get("members", []):
		out.append(str((m as Dictionary).get("name", "")))
	return out


func _on_duel_update(d: Dictionary) -> void:
	duel_state = str(d.get("state", ""))
	duel_opponent_id = int(d.get("opponentId", -1)) if duel_state in ["requested", "countdown", "active"] else -1
	duel_changed.emit(duel_state, int(d.get("opponentId", -1)), int(d.get("winnerId", -1)) if d.get("winnerId") != null else -1, int(d.get("startsInMs", 0)) if d.get("startsInMs") != null else 0)


func _on_trade_update(d: Dictionary) -> void:
	trade = d if str(d.get("state", "")) in ["requested", "open"] else {}
	trade_changed.emit(d)
