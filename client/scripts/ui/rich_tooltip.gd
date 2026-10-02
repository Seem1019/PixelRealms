class_name RichTooltip
## Tooltip propio para `_make_custom_tooltip`: panel del tema con texto BBCode (colores de rareza y comparación), ancho
## máximo UiTheme.TOOLTIP_MAX_WIDTH y alto según el contenido. Godot lo coloca junto al ratón y lo mantiene dentro de la
## ventana; al ser de tamaño contenido, cabe sin taparlo todo.


static func make(bbcode: String) -> Control:
	var label := RichTextLabel.new()
	label.bbcode_enabled = true
	label.fit_content = true
	label.scroll_active = false
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.custom_minimum_size = Vector2(UiTheme.TOOLTIP_MAX_WIDTH, 0)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.text = bbcode
	return label


## Ancho que de verdad ocupa el texto (para tooltips cortos, que no estiren el panel al máximo).
static func fit_width(label: RichTextLabel) -> void:
	label.custom_minimum_size.x = minf(UiTheme.TOOLTIP_MAX_WIDTH, label.get_content_width() + 2.0)
