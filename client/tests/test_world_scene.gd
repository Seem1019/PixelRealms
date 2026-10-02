extends GutTest
## Instancia la escena World sin servidor y le inyecta mensajes como si llegaran por Net: Welcome, EntitySpawn, Snapshot,
## CastStarted/CastEnded, CombatEvents, AuraApplied, Died. Comprueba que el HUD y las entidades reaccionan sin errores.

const WORLD := preload("res://scenes/world/world.tscn")

var _world: Node2D


func before_each() -> void:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")  # nadie escucha: la conexión falla sin romper el test
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	_world = WORLD.instantiate()
	add_child_autofree(_world)
	await get_tree().process_frame
	Net.disconnect_from_server()  # sin reintentos durante el test
	for i: int in 5:
		await get_tree().process_frame


func _dispatch(type: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": type, "d": d}))


func test_welcome_spawn_snapshot_combat_death() -> void:
	_dispatch("Welcome", {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 100.0, "y": 100.0, "level": 3, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_fireball"}, {"slot": 1, "kind": "spell", "ref": "mage_frost_nova"}],
		"knownSpells": ["mage_fireball", "mage_frost_nova"], "rulesHash": "x",
	})
	await get_tree().process_frame
	assert_eq(GameState.self_id, 1)
	assert_eq(GameState.hp, 80)
	assert_true(_world.in_world)

	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 120.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("EntitySpawn", {"id": 8, "kind": "player", "templateId": "warrior", "name": "Bob", "x": 90.0, "y": 100.0, "dir": "s", "level": 2, "classId": "warrior", "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	assert_eq(_world._remotes.size(), 2)
	var slime: RemoteEntity = _world._remotes[7]
	assert_true(slime.hostile)
	assert_eq(RemoteEntity.level_color(0), Color(1, 0.9, 0.2))
	assert_eq(RemoteEntity.level_color(5), Color(1, 0.2, 0.2))
	assert_eq(RemoteEntity.level_color(-5), Color(0.6, 0.6, 0.6))

	# Selección por Tab (más cercano) y marco de objetivo.
	_world._cycle_target()
	assert_eq(GameState.target_id, 7)
	assert_true(slime.selected)

	_dispatch("Snapshot", {"tick": 12, "ackSeq": 0, "self": {"x": 100.0, "y": 100.0, "speed": 4.0, "hp": 70, "maxHp": 100, "res": 40, "maxRes": 60},
		"ents": [{"id": 7, "x": 121.0, "y": 100.0, "dir": "w", "hpPct": 60, "anim": "walk"}, {"id": 8, "x": 90.0, "y": 100.0, "dir": "s", "hpPct": 100, "anim": "idle"}]})
	await get_tree().process_frame
	assert_eq(GameState.hp, 70)
	assert_eq(slime.hp_pct, 60)

	# Casteo propio + fin; casteo ajeno con marca en el suelo.
	_dispatch("CastStarted", {"casterId": 1, "spellId": "mage_fireball", "targetId": 7, "durationMs": 2000})
	assert_false(GameState.own_cast.is_empty())
	_dispatch("CastEnded", {"casterId": 1, "spellId": "mage_fireball", "result": "interrupted"})
	assert_true(GameState.own_cast.is_empty())
	_dispatch("CastStarted", {"casterId": 8, "spellId": "warrior_whirlwind", "targetPos": {"x": 90.0, "y": 100.0}, "durationMs": 1000})
	assert_true(_world._reticle._marks.has(8))
	_dispatch("CastEnded", {"casterId": 8, "spellId": "warrior_whirlwind", "result": "done"})
	assert_false(_world._reticle._marks.has(8))

	# Lote de combate → textos flotantes; auras; cooldown.
	_dispatch("CombatEvents", {"tick": 13, "e": [{"src": 1, "dst": 7, "spellId": "mage_fireball", "kind": "dmg", "amount": 37, "crit": true, "school": "magic"},
		{"src": 7, "dst": 1, "kind": "miss", "amount": 0, "crit": false, "school": "physical"}]})
	await get_tree().process_frame
	assert_eq(_world._floating._active.size(), 2)
	_dispatch("AuraApplied", {"targetId": 7, "auraId": "mage_chill", "casterId": 1, "stacks": 1, "durationMs": 4000})
	assert_eq(GameState.auras_of(7).size(), 1)
	_dispatch("AuraRemoved", {"targetId": 7, "auraId": "mage_chill", "casterId": 1})
	assert_eq(GameState.auras_of(7).size(), 0)
	_dispatch("Cooldown", {"spellId": "mage_frost_nova", "remainingMs": 18000})
	assert_gt(GameState.cooldown_remaining_ms("mage_frost_nova"), 17000)

	# Objetivo fuera de la AOI → deseleccionado.
	_dispatch("EntityDespawn", {"id": 7, "reason": "left"})
	_dispatch("Snapshot", {"tick": 14, "ackSeq": 0, "self": {"x": 100.0, "y": 100.0, "speed": 4.0, "hp": 70, "maxHp": 100, "res": 40, "maxRes": 60}, "ents": []})
	assert_eq(GameState.target_id, -1)

	# XP y subida de nivel: hechizo nuevo a la primera casilla libre (2) y aviso.
	_dispatch("XpGain", {"amount": 6, "sourceId": 7})
	assert_eq(GameState.xp, 6)
	_dispatch("LevelUp", {"level": 4, "newSpells": ["mage_flame_burst"], "rankUps": [{"spellId": "mage_fireball", "rank": 1}]})
	assert_eq(GameState.level, 4)
	assert_true(GameState.known_spells.has("mage_flame_burst"))
	var placed := false
	for h: Variant in GameState.hotbar:
		if str((h as Dictionary).get("ref", "")) == "mage_flame_burst" and int((h as Dictionary).get("slot", -1)) == 2:
			placed = true
	assert_true(placed)
	assert_ne(_world._hud._notice_label.text, "")

	# Inventario, botín y vendedor.
	_dispatch("InventoryUpdate", {"bag": [{"id": "i1", "templateId": "bread", "qty": 3}, null], "equipment": [], "gold": 12345, "reqId": null})
	assert_eq(GameState.gold, 12345)
	_world._inventory.toggle()
	assert_true(_world._inventory.visible)
	assert_eq(_world._inventory._slots[0].item.get("templateId"), "bread")
	_dispatch("LootWindow", {"lootId": 7, "gold": 5, "items": [{"index": 0, "templateId": "slime_goo", "qty": 2, "ownerId": 1, "freeInMs": 30000}, {"index": 1, "templateId": "bread", "qty": 1, "ownerId": 8, "freeInMs": 30000}]})
	assert_true(_world._loot.visible)
	assert_eq(_world._loot._list.get_child_count(), 2)
	_dispatch("EntitySpawn", {"id": 9, "kind": "npc", "templateId": "vendor", "name": "Marta la tendera", "x": 100.0, "y": 110.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("VendorWindow", {"npcId": 9, "items": [{"templateId": "bread", "price": 4}, {"templateId": "minor_healing_potion", "price": 20}]})
	assert_true(_world._vendor.visible)
	assert_true(_world._inventory.vendor_mode)
	assert_eq(_world._vendor._title.text, "Marta la tendera")

	# Social: chat, grupo, duelo, intercambio, cambio de clase, libro y panel.
	_dispatch("ChatMessage", {"channel": "say", "from": "Bob", "text": "hola [b]x[/b]", "ts": 1})
	assert_string_contains(_world._chat._lines[-1], "[lb]b]x[lb]/b]")
	assert_string_contains(_world._chat._log.get_parsed_text(), "hola [b]x[/b]", "se ve tal cual, sin negrita")
	_dispatch("PartyUpdate", {"leader": "Bob", "members": []})
	assert_true(_world._social._prompt.visible)
	_world._social._prompt.hide()
	_dispatch("PartyUpdate", {"leader": "Bob", "members": [{"name": "Ana", "entityId": 1, "classId": "mage", "level": 4, "hpPct": 100, "online": true, "mapId": "meadow"}, {"name": "Bob", "entityId": 8, "classId": "warrior", "level": 2, "hpPct": 55, "online": true, "mapId": "meadow"}]})
	assert_true(GameState.in_party())
	assert_eq(_world._social._frames.get_child_count(), 1)
	_dispatch("DuelUpdate", {"state": "countdown", "opponentId": 8, "startsInMs": 3000})
	assert_string_contains(_world._social._duel_label.text, "Duelo en")
	_dispatch("TradeUpdate", {"state": "open", "partnerId": 8, "version": 1, "mine": {"items": [], "gold": 0}, "theirs": {"items": [], "gold": 5}, "confirmedMine": false, "confirmedTheirs": false})
	assert_true(_world._social._trade.visible)
	_dispatch("TradeUpdate", {"state": "cancelled", "partnerId": 8, "version": 2, "mine": {"items": [], "gold": 0}, "theirs": {"items": [], "gold": 0}, "confirmedMine": false, "confirmedTheirs": false, "reason": "distance"})
	assert_false(_world._social._trade.visible)
	_world._social.open_class_change(9)
	assert_true(_world._social._class_window.visible)
	_world._spellbook.toggle()
	assert_gt(_world._spellbook._list.get_child_count(), 0)
	_world._character.toggle()
	assert_true(_world._character.visible)
	assert_string_contains(_world._character.stats_text(), "Vida")

	# Muerte y pantalla.
	_dispatch("Died", {"killerId": 7, "respawnInMs": 0})
	await get_tree().process_frame
	assert_true(GameState.is_dead)
	assert_true(_world._hud._death_panel.visible)
	_dispatch("Snapshot", {"tick": 20, "ackSeq": 0, "self": {"x": 10.0, "y": 10.0, "speed": 4.0, "hp": 50, "maxHp": 100, "res": 30, "maxRes": 60}, "ents": []})
	await get_tree().process_frame
	assert_false(GameState.is_dead)
	assert_false(_world._hud._death_panel.visible)
