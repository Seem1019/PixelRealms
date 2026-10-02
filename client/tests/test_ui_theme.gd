extends GutTest
## Puntos 3 y 13 de la prueba de juego: un solo tema para toda la interfaz (también dentro del HUD, que vive en un
## CanvasLayer), tooltips de tamaño contenido, todo el texto en español y nombres que no se cortan a media palabra.


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
	assert_eq(slot._name.text, "Poción", "antes «Poci»")
	assert_eq(slot._qty.text, "20")


func test_spell_tooltip_lists_cost_cast_and_cooldown() -> void:
	var text := TooltipBuilder.build_spell(Content.spell("mage_flame_burst"))
	assert_string_contains(text, "Estallido de llamas")
	assert_string_contains(text, "25 de maná")
	assert_string_contains(text, "Lanzamiento 1.5 s")
	assert_string_contains(text, "Recarga 8 s")
