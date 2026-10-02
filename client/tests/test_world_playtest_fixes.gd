extends GutTest
## Arreglos de la prueba de juego: cada test reproduce un fallo visto jugando (barra, apuntado, cambio de clase, XP, vida).
## Instancia la escena World sin servidor y le inyecta mensajes como si llegaran por Net.

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
	for i: int in 3:
		await get_tree().process_frame


func _dispatch(type: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": type, "d": d}))


func _welcome(extra: Dictionary = {}) -> void:
	var d := {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 100.0, "y": 100.0, "level": 3, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 60, "resource": "mana", "classId": "mage", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_fireball"}],
		"knownSpells": ["mage_fireball"], "rulesHash": "x",
	}
	d.merge(extra, true)
	_dispatch("Welcome", d)


# --- Punto 1: el seq de movimiento es por conexión ---------------------------------------------------------------------

func test_second_welcome_on_the_same_connection_keeps_the_move_seq() -> void:
	_welcome()
	await get_tree().process_frame
	_world._movement.seq = 120  # lleva un rato caminando
	_welcome()  # p. ej. el que reenvía el servidor tras cambiar de clase
	assert_eq(_world._movement.seq, 120, "el servidor descartaría los MoveInput con seq ≤ 120")


func test_new_connection_restarts_the_move_seq() -> void:
	_welcome()
	_world._movement.seq = 120
	_world._on_connected("t")  # reconexión: Hello nuevo, el servidor reinicia LastInputSeq
	assert_eq(_world._movement.seq, 0)


# --- Punto 8: cambio de clase ----------------------------------------------------------------------------------------

func test_class_change_welcome_keeps_the_world_and_applies_the_new_class() -> void:
	_welcome()
	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 120.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("EntitySpawn", {"id": 9, "kind": "npc", "templateId": "class_change", "name": "Maestra", "x": 110.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	_world._select(7)
	_dispatch("AuraApplied", {"targetId": 7, "auraId": "mage_chill", "casterId": 1, "stacks": 1, "durationMs": 4000})
	var map_before: TmjMap = _world.map
	# El servidor reenvía Welcome por la misma conexión con la clase nueva (EventDispatcher, ClassChangedEvent).
	_welcome({"self": {"x": 100.0, "y": 100.0, "level": 3, "xp": 0, "xpNext": 100, "hp": 90, "maxHp": 110, "res": 100, "maxRes": 100, "resource": "mana", "classId": "priest", "name": "Ana"},
		"hotbar": [{"slot": 0, "kind": "spell", "ref": "priest_heal"}], "knownSpells": ["priest_heal"]})
	await get_tree().process_frame
	assert_eq(_world._remotes.size(), 2, "el servidor no vuelve a mandar EntitySpawn: borrarlas deja el mundo vacío")
	assert_eq(_world.map, map_before, "mismo mapa: no se recarga")
	assert_eq(GameState.class_id, "priest")
	assert_eq(GameState.known_spells, ["priest_heal"] as Array[String])
	assert_eq(str((GameState.hotbar[0] as Dictionary).get("ref", "")), "priest_heal")
	assert_eq(GameState.target_id, 7, "el objetivo sigue seleccionado")
	assert_eq(GameState.auras_of(7).size(), 1, "las auras de otros no cambian con mi clase")


# --- Punto 2: barras de vida sobre las entidades ---------------------------------------------------------------------

func test_health_bars_follow_hp_for_monsters_players_and_self() -> void:
	_welcome()
	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 120.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 60, "flags": 0})
	_dispatch("EntitySpawn", {"id": 8, "kind": "player", "templateId": "warrior", "name": "Bob", "x": 90.0, "y": 100.0, "dir": "s", "level": 2, "classId": "warrior", "hpPct": 100, "flags": 0})
	_dispatch("EntitySpawn", {"id": 9, "kind": "npc", "templateId": "vendor", "name": "Marta", "x": 110.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	var slime: RemoteEntity = _world._remotes[7]
	assert_true(slime.health_bar.visible)
	assert_eq(slime.health_bar.pct, 60)
	assert_true((_world._remotes[8] as RemoteEntity).health_bar.visible)
	assert_false((_world._remotes[9] as RemoteEntity).health_bar.visible, "los NPC no combaten")
	_dispatch("Snapshot", {"tick": 12, "ackSeq": 0, "self": {"x": 100.0, "y": 100.0, "speed": 4.0, "hp": 25, "maxHp": 100, "res": 40, "maxRes": 60},
		"ents": [{"id": 7, "x": 120.0, "y": 100.0, "dir": "w", "hpPct": 30, "anim": "idle"}]})
	await get_tree().process_frame
	assert_eq(slime.health_bar.pct, 30)
	assert_eq(_world._self_health.pct, 25, "la propia también, encima del personaje")
	_dispatch("Snapshot", {"tick": 14, "ackSeq": 0, "self": {"x": 100.0, "y": 100.0, "speed": 4.0, "hp": 25, "maxHp": 100, "res": 40, "maxRes": 60},
		"ents": [{"id": 7, "x": 120.0, "y": 100.0, "dir": "w", "hpPct": 0, "anim": "dead"}]})
	await get_tree().process_frame
	assert_false(slime.health_bar.visible, "el cadáver no muestra barra")


# --- Punto 4: el cuadro dibujado es el que alcanza el área --------------------------------------------------------------

func test_drawn_body_matches_the_rules_body_box() -> void:
	_welcome()
	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 120.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	var slime: RemoteEntity = _world._remotes[7]
	var drawn := Rect2(slime._body.position + slime.position, slime._body.size)
	assert_eq(drawn, BodyShape.rect_px(slime.position))


func test_aiming_highlights_who_the_area_would_hit() -> void:
	_welcome({"hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_flame_burst"}], "knownSpells": ["mage_flame_burst"]})
	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 160.0, "y": 201.6, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("EntitySpawn", {"id": 8, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 160.0, "y": 115.2, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	_world._use_slot(0)
	_world._update_area_preview(Vector2(160, 160))  # radio 2,5 casillas = 40 px
	assert_true((_world._remotes[7] as RemoteEntity).area_hint, "pies a 41,6 px pero el cuadro entra")
	assert_false((_world._remotes[8] as RemoteEntity).area_hint)
	_world._stop_aiming()
	assert_false((_world._remotes[7] as RemoteEntity).area_hint, "al dejar de apuntar se quita el resaltado")


# --- Punto 5: círculos de área que no desaparecían -------------------------------------------------------------------

func _welcome_with_area_spell() -> void:
	_welcome({"hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_flame_burst"}, {"slot": 1, "kind": "spell", "ref": "mage_fireball"},
		{"slot": 4, "kind": "item", "ref": "minor_healing_potion"}], "knownSpells": ["mage_flame_burst", "mage_fireball"],
		"inventory": [{"id": "0192f0aa-0000-7000-8000-000000000002", "templateId": "minor_healing_potion", "qty": 2}]})


func test_using_another_slot_cancels_aiming() -> void:
	_welcome_with_area_spell()
	_world._use_slot(0)
	assert_true(_world._reticle.aiming)
	_world._use_slot(1)  # Bola de fuego: no apunta al suelo
	assert_false(_world._reticle.aiming, "el círculo verde se quedaba siguiendo al ratón")
	_world._use_slot(0)
	_world._use_slot(4)  # poción
	assert_false(_world._reticle.aiming)


func test_dying_or_changing_map_cancels_aiming() -> void:
	_welcome_with_area_spell()
	_world._use_slot(0)
	_dispatch("Died", {"killerId": 7, "respawnInMs": 0})
	assert_false(_world._reticle.aiming)
	_dispatch("Snapshot", {"tick": 20, "ackSeq": 0, "self": {"x": 100.0, "y": 100.0, "speed": 4.0, "hp": 50, "maxHp": 100, "res": 30, "maxRes": 60}, "ents": []})
	_world._use_slot(0)
	_dispatch("ChangeMap", {"mapId": "meadow", "x": 100.0, "y": 100.0})
	assert_false(_world._reticle.aiming)


func test_remote_mark_goes_away_when_the_caster_leaves_or_the_cast_should_have_ended() -> void:
	_welcome()
	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 120.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("EntitySpawn", {"id": 8, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 140.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("CastStarted", {"casterId": 7, "spellId": "foreman_slam", "targetPos": {"x": 100.0, "y": 100.0}, "durationMs": 1500})
	_dispatch("CastStarted", {"casterId": 8, "spellId": "foreman_slam", "targetPos": {"x": 100.0, "y": 100.0}, "durationMs": 1500})
	assert_true(_world._reticle._marks.has(7))
	_dispatch("EntityDespawn", {"id": 7, "reason": "left"})  # sale de la AOI: su CastEnded ya no llega
	assert_false(_world._reticle._marks.has(7))
	# Si el CastEnded se pierde, la marca caduca poco después de cuando debía terminar el casteo.
	_world._reticle.prune(Time.get_ticks_msec() + 1500 + AoeReticle.EXPIRY_MARGIN_MS + 1)
	assert_false(_world._reticle._marks.has(8))


# --- Punto 6: poción desde la barra ----------------------------------------------------------------------------------

func test_hotbar_item_sends_the_bag_instance_id_not_the_template() -> void:
	_welcome({
		"inventory": [{"id": "0192f0aa-0000-7000-8000-000000000001", "templateId": "bread", "qty": 3}, null,
			{"id": "0192f0aa-0000-7000-8000-000000000002", "templateId": "minor_healing_potion", "qty": 20}],
		"hotbar": [{"slot": 4, "kind": "item", "ref": "minor_healing_potion"}],
	})
	var payload: Dictionary = _world._use_item_payload("minor_healing_potion")
	assert_eq(str(payload.get("itemId", "")), "0192f0aa-0000-7000-8000-000000000002")
	assert_true(payload.has("reqId"))


func test_hotbar_item_without_stock_sends_nothing() -> void:
	_welcome({"inventory": [], "hotbar": [{"slot": 4, "kind": "item", "ref": "minor_healing_potion"}]})
	assert_true((_world._use_item_payload("minor_healing_potion") as Dictionary).is_empty())
