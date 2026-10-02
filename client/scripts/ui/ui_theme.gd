class_name UiTheme
## Tema único de la interfaz: tamaños de letra, espaciados, colores y estilos de panel, botón, campo de texto y tooltip.
## Todo en píxeles lógicos de la resolución base (480×270). Con `stretch/mode="canvas_items"` el texto se rasteriza al tamaño
## final de la ventana (nítido) y la escala entera la pone Godot según la resolución.
## Variaciones de Label: "SmallLabel", "TitleLabel" y "HeadlineLabel" (`theme_type_variation`).

const FONT_SMALL := 7
const FONT_BODY := 8
const FONT_TITLE := 10
const FONT_HEADLINE := 16

## Separación con los bordes de la pantalla, relleno dentro de un panel y hueco entre elementos.
const SCREEN_MARGIN := 4
const PADDING := 3
const GAP := 2

const TEXT := Color(0.94, 0.92, 0.86)
const TEXT_MUTED := Color(0.68, 0.66, 0.6)
const TEXT_DISABLED := Color(0.5, 0.48, 0.44)
const ACCENT := Color(1.0, 0.86, 0.42)
const ERROR := Color(1.0, 0.36, 0.32)
const PANEL_BG := Color(0.1, 0.09, 0.08, 0.9)
const PANEL_BORDER := Color(0.42, 0.35, 0.24)
const BUTTON_BG := Color(0.2, 0.18, 0.15, 0.95)
const BUTTON_HOVER := Color(0.28, 0.25, 0.2, 0.95)
const BUTTON_PRESSED := Color(0.14, 0.12, 0.1, 0.95)
const TOOLTIP_BG := Color(0.06, 0.05, 0.05, 0.96)
## Ancho máximo de un tooltip: nunca más de un tercio de la pantalla base.
const TOOLTIP_MAX_WIDTH := 150


## Tamaño lógico de la pantalla (project.godot: 480×270), en el que se colocan todos los paneles.
static func base_size() -> Vector2:
	return Vector2(float(ProjectSettings.get_setting("display/window/size/viewport_width", 480)), float(ProjectSettings.get_setting("display/window/size/viewport_height", 270)))


## Ancla un panel a una esquina, a un borde o al centro de la pantalla (Control.PRESET_*) con su tamaño mínimo, separado
## `margin` del borde y `offset_y` extra hacia abajo. Crece hacia dentro de la pantalla si su contenido cambia.
static func dock(c: Control, preset: Control.LayoutPreset, margin: int = SCREEN_MARGIN, offset_y: float = 0.0) -> void:
	c.set_anchors_and_offsets_preset(preset, Control.PRESET_MODE_MINSIZE, margin)
	match preset:
		Control.PRESET_TOP_RIGHT, Control.PRESET_CENTER_RIGHT, Control.PRESET_BOTTOM_RIGHT:
			c.grow_horizontal = Control.GROW_DIRECTION_BEGIN
		Control.PRESET_CENTER, Control.PRESET_CENTER_TOP, Control.PRESET_CENTER_BOTTOM:
			c.grow_horizontal = Control.GROW_DIRECTION_BOTH
		_:
			c.grow_horizontal = Control.GROW_DIRECTION_END
	match preset:
		Control.PRESET_BOTTOM_LEFT, Control.PRESET_BOTTOM_RIGHT, Control.PRESET_CENTER_BOTTOM:
			c.grow_vertical = Control.GROW_DIRECTION_BEGIN
		Control.PRESET_CENTER, Control.PRESET_CENTER_LEFT, Control.PRESET_CENTER_RIGHT:
			c.grow_vertical = Control.GROW_DIRECTION_BOTH
		_:
			c.grow_vertical = Control.GROW_DIRECTION_END
	c.offset_top += offset_y
	c.offset_bottom += offset_y


## Al abrir una ventana: por encima de las demás.
static func bring_to_front(c: Control) -> void:
	if c.visible:
		c.move_to_front()


static func build() -> Theme:
	var t := Theme.new()
	t.default_font_size = FONT_BODY
	var panel := _box(PANEL_BG, PANEL_BORDER, PADDING)
	t.set_stylebox("panel", "PanelContainer", panel)
	t.set_stylebox("panel", "Panel", panel)
	t.set_stylebox("panel", "TooltipPanel", _box(TOOLTIP_BG, PANEL_BORDER, PADDING))
	t.set_stylebox("panel", "PopupMenu", panel)
	t.set_stylebox("panel", "PopupPanel", panel)
	for name: String in ["normal", "hover", "pressed", "disabled", "focus"]:
		var bg := {"normal": BUTTON_BG, "hover": BUTTON_HOVER, "pressed": BUTTON_PRESSED, "disabled": PANEL_BG, "focus": BUTTON_BG}[name] as Color
		var box := _box(bg, PANEL_BORDER if name != "hover" else ACCENT.darkened(0.3), 2)
		if name == "focus":
			box.bg_color = Color(0, 0, 0, 0)
		t.set_stylebox(name, "Button", box)
	t.set_stylebox("normal", "LineEdit", _box(Color(0.06, 0.05, 0.05, 0.85), PANEL_BORDER, 2))
	t.set_stylebox("focus", "LineEdit", _box(Color(0.06, 0.05, 0.05, 0.95), ACCENT.darkened(0.2), 2))
	for type: String in ["Label", "Button", "LineEdit", "TooltipLabel", "PopupMenu"]:
		t.set_color("font_color", type, TEXT)
		t.set_font_size("font_size", type, FONT_BODY)
	t.set_color("font_disabled_color", "Button", TEXT_DISABLED)
	t.set_color("font_hover_color", "Button", ACCENT)
	t.set_color("font_placeholder_color", "LineEdit", TEXT_MUTED)
	for key: String in ["normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size"]:
		t.set_font_size(key, "RichTextLabel", FONT_BODY)
	t.set_color("default_color", "RichTextLabel", TEXT)
	t.set_constant("separation", "VBoxContainer", GAP)
	t.set_constant("separation", "HBoxContainer", GAP)
	t.set_constant("h_separation", "GridContainer", GAP)
	t.set_constant("v_separation", "GridContainer", GAP)
	t.set_type_variation("SmallButton", "Button")
	t.set_font_size("font_size", "SmallButton", FONT_SMALL)
	_label_variation(t, "SmallLabel", FONT_SMALL, TEXT_MUTED)
	_label_variation(t, "TitleLabel", FONT_TITLE, ACCENT)
	_label_variation(t, "HeadlineLabel", FONT_HEADLINE, TEXT)
	return t


static func _label_variation(t: Theme, name: String, size: int, color: Color) -> void:
	t.set_type_variation(name, "Label")
	t.set_font_size("font_size", name, size)
	t.set_color("font_color", name, color)


static func _box(bg: Color, border: Color, padding: int) -> StyleBoxFlat:
	var b := StyleBoxFlat.new()
	b.bg_color = bg
	b.border_color = border
	b.set_border_width_all(1)
	b.set_content_margin_all(padding)
	return b
