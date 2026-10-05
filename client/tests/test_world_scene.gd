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


func _floating_texts() -> Array[String]:
	var out: Array[String] = []
	for e: Dictionary in _world._floating._active:
		out.append((e["label"] as Label).text)
	return out


## Lo que el servidor manda junto con ChangeMap (NPC y jugadores al lado del destino) no se pierde con el fundido.
func test_spawns_that_arrive_during_the_map_fade_are_kept() -> void:
	_dispatch("Welcome", {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "mine",
		"self": {"x": 100.0, "y": 100.0, "level": 4, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "rage", "classId": "warrior", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [], "knownSpells": [], "rulesHash": "x",
	})
	_dispatch("EntitySpawn", {"id": 40, "kind": "monster", "templateId": "kobold_miner", "name": "Kóbold", "x": 120.0, "y": 100.0, "dir": "s", "level": 5, "hpPct": 100, "flags": 0})
	_dispatch("ChangeMap", {"mapId": "meadow", "x": 368.0, "y": 960.0})
	_dispatch("EntitySpawn", {"id": 9, "kind": "npc", "templateId": "vendor", "name": "Marta la tendera", "x": 272.0, "y": 768.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().create_timer(0.6).timeout
	assert_false(_world._remotes.has(40), "lo del mapa anterior se va")
	assert_true(_world._remotes.has(9), "lo que llegó durante el fundido se queda")


## `/level` estando muerto revive con un Welcome por la misma conexión: el panel de muerte se cierra como al reaparecer.
func test_a_renewed_welcome_that_revives_counts_as_a_respawn() -> void:
	var welcome := {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 100.0, "y": 100.0, "level": 4, "xp": 0, "xpNext": 100, "hp": 0, "maxHp": 100, "res": 0, "maxRes": 100, "resource": "rage", "classId": "warrior", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [], "knownSpells": [], "rulesHash": "x",
	}
	_dispatch("Welcome", welcome)
	assert_true(GameState.is_dead)
	watch_signals(GameState)
	(welcome["self"] as Dictionary)["hp"] = 100
	_dispatch("Welcome", welcome)
	assert_false(GameState.is_dead)
	assert_signal_emitted(GameState, "respawned")


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
	# HU-035 CA6: el icono enseña a la vez las cargas y el tiempo que queda.
	_dispatch("AuraApplied", {"targetId": 1, "auraId": "rogue_poison", "casterId": 7, "stacks": 3, "durationMs": 9000})
	await get_tree().process_frame
	var aura_cell: Control = _world._hud._self_auras.get_child(_world._hud._self_auras.get_child_count() - 1)
	assert_eq((aura_cell.get_node("Stacks") as Label).text, "3")
	assert_eq((aura_cell.get_node("Time") as Label).text, "9")
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
	# HU-041 CA3: efecto y "¡Nivel N!" propio, y el nivel sobre el nombre; también al ver subir a otro jugador.
	assert_eq(_world._self_visual.plate.level_text, "4")
	assert_true(_floating_texts().has("¡Nivel 4!"))
	_dispatch("EntitySpawn", {"id": 8, "kind": "player", "templateId": "warrior", "name": "Bob", "x": 90.0, "y": 100.0, "dir": "s", "level": 3, "classId": "warrior", "hpPct": 100, "flags": 0})
	assert_true(_floating_texts().has("¡Nivel 3!"))
	assert_eq((_world._remotes[8] as RemoteEntity).visual.plate.level_text, "3")
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
	# Tras coger lo último se cierra sola, aunque siga mostrando el oro ya cobrado.
	watch_signals(GameState)
	_dispatch("LootWindow", {"lootId": 7, "gold": 5, "items": []})
	assert_false(_world._loot.visible)
	assert_signal_not_emitted(GameState, "notice", "el oro ya se vio en la ventana")
	# Un cadáver con solo oro no abre una ventana vacía ni lo repite en el chat en cada clic.
	_dispatch("LootWindow", {"lootId": 11, "gold": 5, "items": []})
	_dispatch("LootWindow", {"lootId": 11, "gold": 5, "items": []})
	assert_false(_world._loot.visible)
	assert_signal_not_emitted(GameState, "notice")
	# HU-050 CA1: el cadáver con botín para mí (bit 8) brilla hasta que no queda nada mío.
	_dispatch("EntitySpawn", {"id": 13, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 130.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 0, "flags": 2 | 8})
	var corpse: EntityVisual = (_world._remotes[13] as RemoteEntity).visual
	assert_true(corpse.lootable)
	_dispatch("LootWindow", {"lootId": 13, "gold": 0, "items": [{"index": 0, "templateId": "slime_goo", "qty": 1, "ownerId": 1, "freeInMs": 30000}, {"index": 1, "templateId": "bread", "qty": 1, "ownerId": 8, "freeInMs": 30000}]})
	assert_true(corpse.lootable, "aún queda algo mío")
	_dispatch("LootWindow", {"lootId": 13, "gold": 0, "items": [{"index": 1, "templateId": "bread", "qty": 1, "ownerId": 8, "freeInMs": 30000}]})
	assert_false(corpse.lootable, "solo queda lo de otro: deja de brillar")
	_world._loot.visible = false
	_dispatch("EntitySpawn", {"id": 12, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 140.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 0, "flags": 2})
	assert_false((_world._remotes[12] as RemoteEntity).visual.lootable, "sin el bit, no brilla")
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
	var bob_plate: Nameplate = (_world._remotes[8] as RemoteEntity).visual.plate
	assert_eq(bob_plate.name_color, _world.PARTY_NAME_COLOR, "HU-024 CA3: los del grupo en azul")
	_dispatch("DuelUpdate", {"state": "countdown", "opponentId": 8, "startsInMs": 3000})
	assert_string_contains(_world._social._duel_label.text, "Duelo en")
	assert_eq(bob_plate.name_color, _world.DUEL_NAME_COLOR, "el rival del duelo en naranja, aunque sea del grupo")
	# HU-101: fuera de la zona, el rótulo cuenta los segundos; al perder por eso, lo dice.
	_dispatch("DuelUpdate", {"state": "active", "opponentId": 8, "zone": {"x": 100.0, "y": 100.0, "r": 192.0}})
	_dispatch("DuelUpdate", {"state": "active", "opponentId": 8, "zone": {"x": 100.0, "y": 100.0, "r": 192.0}, "outsideMs": 5000})
	await get_tree().process_frame
	assert_string_contains(_world._social._duel_label.text, "Vuelve a la zona del duelo")
	_dispatch("DuelUpdate", {"state": "ended", "opponentId": 8, "winnerId": 8, "reason": "zone"})
	await get_tree().process_frame
	assert_eq(_world._social._duel_label.text, "Has perdido el duelo: saliste de la zona")
	assert_eq(bob_plate.name_color, _world.PARTY_NAME_COLOR, "al terminar vuelve al azul del grupo")
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
