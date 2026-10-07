extends GutTest
## HU-103: desde el nivel 7 hay más hechizos que casillas. El hechizo que no cabe no se coloca solo: el aviso manda al libro,
## que lo marca como nuevo hasta cerrarlo, cuenta los equipados y deja cambiarlos fuera de combate (en combate, no).

const WORLD := preload("res://scenes/world/world.tscn")
const KIT := ["warrior_heroic_strike", "warrior_taunt", "warrior_charge", "warrior_whirlwind"]


func _dispatch(t: String, d: Dictionary) -> void:
	Net._dispatch(JSON.stringify({"t": t, "d": d}))


## Guerrero de nivel 6 con sus 4 hechizos en las casillas 1–4.
func _world() -> Node2D:
	Settings._config.set_value("net", "server_url", "http://127.0.0.1:1")
	GameState.pending_ticket = "t"
	GameState.pending_character_id = "c"
	GameState.unseen_spells.clear()
	GameState.last_combat_ms = -1000000
	var w := WORLD.instantiate() as Node2D
	add_child_autofree(w)
	await get_tree().process_frame
	Net.disconnect_from_server()
	var hotbar: Array = []
	for i: int in KIT.size():
		hotbar.append({"slot": i, "kind": "spell", "ref": KIT[i]})
	_dispatch("Welcome", {
		"selfId": 1, "tick": 10, "tickRate": 20, "snapshotRate": 10, "mapId": "meadow",
		"self": {"x": 300.0, "y": 800.0, "level": 6, "xp": 0, "xpNext": 100, "hp": 80, "maxHp": 100, "res": 40, "maxRes": 100, "resource": "rage", "classId": "warrior", "name": "Ana"},
		"inventory": [], "equipment": [], "hotbar": hotbar, "knownSpells": KIT, "rulesHash": "x"})
	for i: int in 3:
		await get_tree().process_frame
	return w


func _entry(book: SpellbookWindow, spell_id: String) -> SpellbookWindow.SpellEntry:
	var list: VBoxContainer = book.get("_list")
	for row: Node in list.get_children():
		var entry := row as SpellbookWindow.SpellEntry
		if entry.spell_id == spell_id:
			return entry
	return null


func test_a_spell_that_does_not_fit_stays_in_the_book_marked_as_new() -> void:
	var w := await _world()
	_dispatch("LevelUp", {"level": 7, "newSpells": ["warrior_shield_block"], "rankUps": []})
	assert_eq(GameState.spell_slot_of("warrior_shield_block"), -1, "no se coloca solo con la barra llena")
	assert_true(GameState.known_spells.has("warrior_shield_block"))
	var hud: CombatHud = w.get("_hud")
	var notice: String = (hud.get("_notice_label") as Label).text
	assert_string_contains(notice, "¡Nivel 7!", "los avisos juntos se apilan: el del nivel no tapa al del hechizo")
	assert_string_contains(notice, "Bloqueo con escudo (en el libro, P)")

	var book: SpellbookWindow = w.get("_spellbook")
	book.toggle()
	await get_tree().process_frame
	assert_string_contains((book.get("_count") as Label).text, "Equipados 4/5")
	var block := _entry(book, "warrior_shield_block")
	assert_true(block.is_new)
	assert_eq(block.slot, -1)
	assert_eq(block.name_color(), UiTheme.ACCENT)
	assert_eq(_entry(book, "warrior_taunt").slot, 1, "los equipados llevan su casilla")
	assert_true(_entry(book, "warrior_cleave").name_color() == UiTheme.TEXT_DISABLED, "nivel 9: sin aprender")

	book.toggle()
	assert_true(GameState.unseen_spells.is_empty(), "al cerrar el libro, ya está visto")
	book.toggle()
	await get_tree().process_frame
	assert_false(_entry(book, "warrior_shield_block").is_new)
	assert_eq(_entry(book, "warrior_shield_block").name_color(), UiTheme.TEXT_MUTED, "aprendido sin equipar")


func test_equipped_spells_change_out_of_combat_but_not_in_combat() -> void:
	var w := await _world()
	_dispatch("LevelUp", {"level": 7, "newSpells": ["warrior_shield_block"], "rankUps": []})
	var hud: CombatHud = w.get("_hud")
	var book: SpellbookWindow = w.get("_spellbook")
	book.toggle()
	await get_tree().process_frame

	GameState.last_combat_ms = Time.get_ticks_msec()
	hud.call("_assign_slot", 3, "spell", "warrior_shield_block")
	assert_eq(GameState.spell_slot_of("warrior_whirlwind"), 3, "en combate la casilla ocupada no cambia")
	assert_eq((hud.get("_error_label") as Label).text, "No puedes cambiar hechizos en combate")

	GameState.last_combat_ms = -1000000
	hud.call("_assign_slot", 3, "spell", "warrior_shield_block")
	assert_eq(GameState.spell_slot_of("warrior_shield_block"), 3)
	assert_eq(GameState.spell_slot_of("warrior_whirlwind"), -1)
	assert_eq(_entry(book, "warrior_shield_block").slot, 3, "el libro abierto se actualiza con la barra")
	assert_eq(_entry(book, "warrior_whirlwind").name_color(), UiTheme.TEXT_MUTED)
	assert_string_contains((book.get("_count") as Label).text, "Equipados 4/5")
