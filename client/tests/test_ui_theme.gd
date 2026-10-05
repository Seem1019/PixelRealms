extends GutTest
## Puntos 3 y 13 de la prueba de juego: un solo tema para toda la interfaz (también dentro del HUD, que vive en un
## CanvasLayer), tooltips de tamaño contenido, todo el texto en español y nombres que no se cortan a media palabra.


## HU-082 CA2: un ícono que no existe se ve como "?" (y avisa en el log), no como una casilla vacía; sin referencia, nada.
func test_missing_icon_shows_a_question_mark() -> void:
	var missing := UiTheme.icon("items/this_icon_does_not_exist")
	assert_not_null(missing)
	assert_eq(missing, UiTheme.missing_icon())
	assert_eq(missing.get_size(), Vector2(16, 16))
	assert_eq(missing.get_image().get_pixel(7, 3), UiTheme.ACCENT, "arriba del signo")
	assert_null(UiTheme.icon(""))
	assert_ne(UiTheme.icon("items/bread"), UiTheme.missing_icon(), "los que existen salen tal cual")


func test_theme_reaches_controls_inside_a_canvas_layer() -> void:
	var layer := CanvasLayer.new()
	add_child_autofree(layer)
	var label := Label.new()
	layer.add_child(label)
	var small := Label.new()
	small.theme_type_variation = "SmallLabel"
	layer.add_child(small)
	await get_tree().process_frame
	assert_eq(label.get_theme_font_size("font_size"), UiTheme.FONT_BODY, "antes cada panel fijaba su tamaño y el resto salía a 16")
	assert_eq(small.get_theme_font_size("font_size"), UiTheme.FONT_SMALL)


func test_item_tooltip_is_width_capped_and_keeps_rarity_colors() -> void:
	var slot := ItemSlot.new()
	add_child_autofree(slot)
	slot.set_item({"id": "i1", "templateId": "wolf_tooth_necklace", "qty": 1})
	var tip := slot._make_custom_tooltip("") as RichTextLabel
	assert_not_null(tip)
	add_child_autofree(tip)
	await get_tree().process_frame
	assert_true(tip.size.x <= UiTheme.TOOLTIP_MAX_WIDTH + 1.0, "ancho %.0f" % tip.size.x)
	assert_string_contains(tip.text, "[color=")


func test_item_types_are_shown_in_spanish() -> void:
	var text := TooltipBuilder.build(Content.item("wolf_tooth_necklace"), 1, "mage", 4)
	assert_false(text.contains("jewelry"))
	assert_string_contains(text, "Cuello · Joya")
	assert_string_contains(TooltipBuilder.build(Content.item("bread"), 1, "mage", 4), "Consumible")
	assert_eq(UiText.class_name_of("mage"), "Mago")
	assert_eq(UiText.resource("mana"), "Maná")
	assert_eq(UiText.role("healer"), "Sanador")


func test_slot_labels_never_cut_words() -> void:
	assert_eq(UiText.short_name("Collar de dientes de lobo"), "Collar")
	assert_eq(UiText.short_name("Pan"), "Pan")
	var slot := ItemSlot.new()
	add_child_autofree(slot)
	slot.set_item({"id": "i1", "templateId": "minor_healing_potion", "qty": 20})
	# Rediseño: la casilla muestra el ícono (16×16 a ×2) y la cantidad; el nombre entero, nunca cortado, va en el tooltip.
	assert_not_null(slot.icon_texture(), "antes salía la palabra «Poción» en vez del ícono")
	assert_eq(slot.icon_texture().get_size(), Vector2(16, 16))
	assert_eq(slot._qty.text, "20")
	assert_string_contains(slot.tooltip_bbcode, "Poción menor de vida")


func test_theme_uses_the_hd_font_smoothed() -> void:
	# HU-092: Alegreya Sans con suavizado en vez de la fuente pixel; los títulos en Alegreya SC.
	var font := ThemeDB.get_default_theme().default_font as FontFile
	assert_not_null(font, "la fuente por defecto de Godot es otra sans")
	assert_string_contains(font.resource_path if not font.resource_path.is_empty() else font.get_font_name(), "Alegreya")
	assert_eq(font.antialiasing, TextServer.FONT_ANTIALIASING_GRAY)
	var title := ThemeDB.get_default_theme().get_font("font", "TitleLabel") as FontFile
	assert_not_null(title)
	assert_string_contains(title.get_font_name(), "Alegreya SC")


func test_panels_and_buttons_are_9_slice_textures() -> void:
	var t := ThemeDB.get_default_theme()
	assert_is(t.get_stylebox("panel", "PanelContainer"), StyleBoxTexture, "antes caja oscura con borde de 1 px")
	assert_is(t.get_stylebox("normal", "Button"), StyleBoxTexture)
	assert_is(t.get_stylebox("panel", "TooltipPanel"), StyleBoxTexture)


func test_empty_equipment_slot_shows_a_faint_silhouette_not_text() -> void:
	var slot := ItemSlot.new()
	slot.container = "equip"
	add_child_autofree(slot)
	slot.set_placeholder("Cabeza", "head")
	assert_not_null(slot.icon_texture(), "antes salía el texto «Cabeza»")
	assert_lt(slot._icon.modulate.a, 1.0)
	assert_eq(slot.tooltip_text, "Cabeza")


func test_rarity_is_the_slot_frame_color() -> void:
	var slot := ItemSlot.new()
	add_child_autofree(slot)
	slot.set_item({"id": "i1", "templateId": "wolf_tooth_necklace", "qty": 1})
	var rarity := str(Content.item("wolf_tooth_necklace").get("rarity", ""))
	assert_true(slot._frame.visible)
	assert_eq(slot._frame.self_modulate, ItemSlot.RARITY_COLORS[rarity])


func test_spell_tooltip_lists_cost_cast_and_cooldown() -> void:
	var text := TooltipBuilder.build_spell(Content.spell("mage_flame_burst"))
	assert_string_contains(text, "Estallido de llamas")
	assert_string_contains(text, "25 de maná")
	assert_string_contains(text, "Lanzamiento 1.5 s")
	assert_string_contains(text, "Recarga 8 s")
