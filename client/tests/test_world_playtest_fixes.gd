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

func test_highlighted_body_matches_the_rules_body_box() -> void:
	_welcome()
	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 120.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	var slime: RemoteEntity = _world._remotes[7]
	# Rediseño: el cuerpo se dibuja con un sprite de 32×32; el cuadro que se resalta al apuntar es el de las reglas.
	assert_eq(Rect2(slime.body_rect().position + slime.position, slime.body_rect().size), BodyShape.rect_px(slime.position))
	assert_true(slime.hint_rect().encloses(slime.body_rect()), "el contorno rodea el cuadro con el que decide el servidor")
	assert_true(slime.visual.sprite.visible, "el slime tiene sprite, no un cuadrado de color")
	assert_eq(slime.visual.sprite.position.y + float(EntitySprites.meta(slime.visual.sprite_ref)["feet"]), 0.0, "los pies del sprite caen en la posición de la entidad")


func test_aiming_highlights_who_the_area_would_hit() -> void:
	_welcome({"hotbar": [{"slot": 0, "kind": "spell", "ref": "mage_flame_burst"}], "knownSpells": ["mage_flame_burst"]})
	_dispatch("EntitySpawn", {"id": 7, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 160.0, "y": 201.6, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("EntitySpawn", {"id": 8, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 160.0, "y": 115.2, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	_world._use_slot(0)
	_world._reticle.aim_pos = Vector2(160, 160)
	_world._update_area_preview()  # radio 2,5 casillas = 40 px
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


# --- Punto 9: XP ganada ------------------------------------------------------------------------------------------------

func test_xp_gain_shows_a_floating_number_over_the_player() -> void:
	_welcome()
	await get_tree().process_frame
	_dispatch("XpGain", {"amount": 12, "sourceId": 7})
	await get_tree().process_frame
	var texts: Array[String] = []
	for e: Variant in _world._floating._active:
		texts.append(((e as Dictionary)["label"] as Label).text)
	assert_has(texts, "+12 XP", "antes solo se movía la barra de 3 px")


# --- Punto 7: el chat se desvanece -------------------------------------------------------------------------------------

func test_chat_fades_when_idle_and_comes_back_on_message_or_enter() -> void:
	_welcome()
	var chat: ChatPanel = _world._chat
	var t0 := Time.get_ticks_msec()
	chat.add_message("say", "Bob", "hola")
	chat.update_fade(t0)
	assert_almost_eq(chat.modulate.a, 1.0, 0.01)
	chat.update_fade(t0 + int((ChatPanel.FADE_DELAY_SEC + ChatPanel.FADE_SEC) * 1000) + 100)
	assert_almost_eq(chat.modulate.a, 0.0, 0.01, "sin mensajes nuevos se desvanece")
	chat.add_message("global", "Bob", "¿alguien?")
	chat.update_fade(Time.get_ticks_msec())
	assert_almost_eq(chat.modulate.a, 1.0, 0.01, "vuelve al llegar un mensaje")
	chat.update_fade(Time.get_ticks_msec() + 60_000)
	chat.open_input()  # Enter
	chat.update_fade(Time.get_ticks_msec() + 120_000)
	assert_almost_eq(chat.modulate.a, 1.0, 0.01, "mientras escribes no se esconde")


# --- Punto 11: a quién alcanza un área ----------------------------------------------------------------------------------

func test_every_target_of_an_area_flashes_and_gets_its_own_number() -> void:
	_welcome()
	for id: int in [7, 8, 10]:
		_dispatch("EntitySpawn", {"id": id, "kind": "monster", "templateId": "slime", "name": "Slime", "x": 100.0 + id * 10.0, "y": 100.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	await get_tree().process_frame
	_dispatch("CombatEvents", {"tick": 13, "e": [
		{"src": 1, "dst": 7, "spellId": "mage_flame_burst", "kind": "dmg", "amount": 30, "crit": false, "school": "magic"},
		{"src": 1, "dst": 8, "spellId": "mage_flame_burst", "kind": "dmg", "amount": 28, "crit": true, "school": "magic"},
		{"src": 1, "dst": 10, "spellId": "mage_flame_burst", "kind": "miss", "amount": 0, "crit": false, "school": "magic"}]})
	await get_tree().process_frame
	assert_eq(_world._floating._active.size(), 3, "un número por objetivo")
	assert_true((_world._remotes[7] as RemoteEntity).is_flashing())
	assert_true((_world._remotes[8] as RemoteEntity).is_flashing())
	assert_false((_world._remotes[10] as RemoteEntity).is_flashing(), "un fallo no destella")
	var label: Label = (_world._floating._active[0] as Dictionary)["label"]
	assert_gt(label.get_theme_constant("outline_size"), 0, "contorno para leerse sobre cualquier fondo")


# --- Punto 10: tooltip en la tienda ------------------------------------------------------------------------------------

func test_vendor_rows_show_the_item_tooltip_with_the_buy_price() -> void:
	_welcome()
	_dispatch("EntitySpawn", {"id": 9, "kind": "npc", "templateId": "vendor", "name": "Marta", "x": 100.0, "y": 110.0, "dir": "s", "level": 1, "hpPct": 100, "flags": 0})
	_dispatch("VendorWindow", {"npcId": 9, "items": [{"templateId": "novice_wand", "price": 60}]})
	await get_tree().process_frame
	var row_name: Control = (_world._vendor._list.get_child(0) as Control).get_child(0)
	assert_ne(row_name.mouse_filter, Control.MOUSE_FILTER_IGNORE, "un Label ignora el ratón: el tooltip nunca salía")
	var tip := row_name.call("_make_custom_tooltip", "") as RichTextLabel
	assert_not_null(tip)
	assert_string_contains(tip.text, "Varita de novicio")
	assert_string_contains(tip.text, "Compra: 60c")
	assert_string_contains(tip.text, "Daño")


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
