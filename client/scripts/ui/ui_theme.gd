class_name UiTheme
## Tema único de la interfaz: fuente HD (Alegreya Sans / Alegreya SC), tamaños de letra, espaciados, colores y estilos 9-slice (marco de madera,
## botones de tablón, casillas hundidas, barras con marco, campo de texto y tooltip). Todo en píxeles lógicos de la
## resolución base (480×270). Con `stretch/mode="canvas_items"` el texto se rasteriza al tamaño real de pantalla (×3 a
## 1440×810) con suavizado: letra nítida sobre paneles y sprites pixel art. Las texturas salen de tools/art/gen_ui.py.
## Variaciones de Label: "SmallLabel", "TitleLabel", "HeadlineLabel" y "OutlinedLabel" (`theme_type_variation`).

## Alegreya Sans (OFL) para el texto y Alegreya SC (versalitas) para títulos (HU-092). Los tamaños están en píxeles
## lógicos: a 8 px la altura de mayúscula es la misma que tenía la fuente pixel, así que los paneles no cambian de tamaño.
const FONT_PATH := "res://assets/fonts/AlegreyaSans-Medium.ttf"
const FONT_BOLD_PATH := "res://assets/fonts/AlegreyaSans-Bold.ttf"
const FONT_TITLE_PATH := "res://assets/fonts/AlegreyaSC-Bold.ttf"
## Contorno en píxeles lógicos (×3 en pantalla): fino en paneles, grueso en el texto sobre el mundo.
const OUTLINE_THIN := 1
const OUTLINE_THICK := 2
const FONT_SMALL := 8
const FONT_BODY := 8
const FONT_TITLE := 8
const FONT_HEADLINE := 16

## Separación con los bordes de la pantalla, relleno dentro de un panel y hueco entre elementos.
const SCREEN_MARGIN := 4
const PADDING := 3
const GAP := 2

## Paleta Resurrect 64 (assets/palette.png).
const TEXT := Color("fbf3e0")
const TEXT_MUTED := Color("c7b8a0")
const TEXT_DISABLED := Color("7f708a")
const ACCENT := Color("f9c22b")
const ERROR := Color("ea4f36")
const OUTLINE := Color("2e222f")
const PANEL_BG := Color("45293f")
const PANEL_BORDER := Color("9e4539")
const BUTTON_BG := Color("cd683d")
const BUTTON_HOVER := Color("e6904e")
const BUTTON_PRESSED := Color("9e4539")
const TOOLTIP_BG := Color("2e222f")
## Ancho máximo de un tooltip: nunca más de un tercio de la pantalla base.
const TOOLTIP_MAX_WIDTH := 150

## Márgenes 9-slice de cada textura (los mismos que escribe gen_ui.py en assets/ui/margins.json).
const UI_DIR := "res://assets/ui/"

static var _font: FontFile
static var _font_bold: FontFile
static var _font_title: FontFile
static var _textures: Dictionary = {}
static var _missing_icon: Texture2D
static var _warned_icons: Dictionary = {}
## Signo "?" del ícono que falta (HU-082 CA2): 8×10, "#" = acento.
const MISSING_GLYPH: Array[String] = [
	"..####..",
	".##..##.",
	".##..##.",
	".....##.",
	"....##..",
	"...##...",
	"...##...",
	"........",
	"...##...",
	"...##...",
]


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


## Mete un Control dentro de los 480×270 (p. ej. tras crecer por su contenido). Las posiciones se redondean a píxel.
static func clamp_to_screen(c: Control, margin: int = SCREEN_MARGIN) -> void:
	var base := base_size()
	var s := c.get_combined_minimum_size().max(c.size)
	var p := c.position
	p.x = clampf(p.x, margin, maxf(margin, base.x - margin - s.x))
	p.y = clampf(p.y, margin, maxf(margin, base.y - margin - s.y))
	c.position = p.round()


## Fuente del texto: suavizado en gris, hinting ligero y posiciones subpíxel (se ve igual de nítida a cualquier escala).
static func font() -> Font:
	if _font == null:
		_font = _load_font(FONT_PATH)
	return _font


static func font_bold() -> Font:
	if _font_bold == null:
		_font_bold = _load_font(FONT_BOLD_PATH)
	return _font_bold if _font_bold != null else font()


static func font_title() -> Font:
	if _font_title == null:
		_font_title = _load_font(FONT_TITLE_PATH)
	return _font_title if _font_title != null else font()


static func _load_font(path: String) -> FontFile:
	if not ResourceLoader.exists(path):
		return null
	var f := (load(path) as FontFile).duplicate() as FontFile
	f.antialiasing = TextServer.FONT_ANTIALIASING_GRAY
	f.hinting = TextServer.HINTING_LIGHT
	f.subpixel_positioning = TextServer.SUBPIXEL_POSITIONING_AUTO
	f.generate_mipmaps = false
	return f


static func texture(rel: String) -> Texture2D:
	if not _textures.has(rel):
		var path := rel if rel.begins_with("res://") else UI_DIR + rel
		_textures[rel] = load(path) as Texture2D if ResourceLoader.exists(path) else null
	return _textures[rel]


## Ícono de 16×16 de content/ ("items/sword_worn", "spells/fireball"); null sin referencia. Si la referencia no tiene PNG, un
## "?" y un aviso en el log, una vez por referencia (HU-082 CA2): nunca una casilla vacía que parezca otra cosa.
static func icon(ref: String) -> Texture2D:
	if ref.is_empty():
		return null
	var tex := texture("res://assets/icons/%s.png" % ref)
	if tex != null:
		return tex
	if not _warned_icons.has(ref):
		_warned_icons[ref] = true
		push_warning("Falta el ícono '%s' (assets/icons/%s.png): se muestra '?'" % [ref, ref])
	return missing_icon()


## "?" de 16×16 dibujado aquí, sin archivo: fondo y marco de panel con el signo en el color de acento.
static func missing_icon() -> Texture2D:
	if _missing_icon == null:
		var img := Image.create(16, 16, false, Image.FORMAT_RGBA8)
		img.fill(PANEL_BG)
		for i: int in 16:
			for p: Vector2i in [Vector2i(i, 0), Vector2i(i, 15), Vector2i(0, i), Vector2i(15, i)]:
				img.set_pixelv(p, PANEL_BORDER)
		for y: int in MISSING_GLYPH.size():
			for x: int in MISSING_GLYPH[y].length():
				if MISSING_GLYPH[y][x] == "#":
					img.set_pixel(4 + x, 3 + y, ACCENT)
		_missing_icon = ImageTexture.create_from_image(img)
	return _missing_icon


## StyleBox 9-slice desde assets/ui/<name>.png; si falta la textura, un StyleBoxFlat equivalente (sin degradados).
static func nine(name: String, margin: int, content: int = -1, fallback_bg: Color = PANEL_BG) -> StyleBox:
	var tex := texture(name + ".png")
	if tex == null:
		var flat := StyleBoxFlat.new()
		flat.bg_color = fallback_bg
		flat.border_color = PANEL_BORDER
		flat.set_border_width_all(1)
		flat.set_content_margin_all(maxi(content, 2))
		return flat
	var b := StyleBoxTexture.new()
	b.texture = tex
	b.texture_margin_left = margin
	b.texture_margin_top = margin
	b.texture_margin_right = margin
	b.texture_margin_bottom = margin
	b.set_content_margin_all(content if content >= 0 else margin)
	return b


static func panel_style() -> StyleBox:
	return nine("panel", 7, 6)


## Etiqueta con contorno oscuro de 1 px (texto sobre el mundo: nombres, números, avisos).
static func outlined(label: Label, color: Color = TEXT, size: int = FONT_BODY) -> void:
	label.add_theme_color_override("font_color", color)
	label.add_theme_color_override("font_outline_color", OUTLINE)
	label.add_theme_constant_override("outline_size", OUTLINE_THICK if size <= FONT_BODY else OUTLINE_THICK * 2)
	label.add_theme_font_size_override("font_size", size)


static func build() -> Theme:
	var t := Theme.new()
	var f := font()
	if f != null:
		t.default_font = f
	t.default_font_size = FONT_BODY
	var panel := panel_style()
	t.set_stylebox("panel", "PanelContainer", panel)
	t.set_stylebox("panel", "Panel", panel)
	t.set_stylebox("panel", "TooltipPanel", nine("tooltip", 4, 4, TOOLTIP_BG))
	t.set_stylebox("panel", "PopupMenu", nine("tooltip", 4, 3, TOOLTIP_BG))
	t.set_stylebox("panel", "PopupPanel", panel)
	t.set_stylebox("embedded_border", "Window", panel)
	t.set_stylebox("embedded_unfocused_border", "Window", panel)
	t.set_stylebox("panel", "AcceptDialog", nine("panel_light", 7, 6))
	t.set_stylebox("hover", "PopupMenu", nine("button_hover", 4, 1))
	t.set_type_variation("LightPanel", "PanelContainer")
	t.set_stylebox("panel", "LightPanel", nine("panel_light", 7, 4))
	t.set_type_variation("SlotPanel", "PanelContainer")
	t.set_stylebox("panel", "SlotPanel", nine("slot", 3, 1))
	t.set_type_variation("ToastPanel", "PanelContainer")
	t.set_stylebox("panel", "ToastPanel", nine("toast", 4, 3, Color("6e2727")))
	for state: String in ["normal", "hover", "pressed", "disabled"]:
		t.set_stylebox(state, "Button", nine("button_" + state, 4, 3, BUTTON_BG))
	t.set_stylebox("focus", "Button", StyleBoxEmpty.new())
	t.set_stylebox("normal", "LineEdit", nine("line_edit", 3, 3))
	t.set_stylebox("focus", "LineEdit", nine("line_edit_focus", 3, 3))
	t.set_stylebox("read_only", "LineEdit", nine("line_edit", 3, 3))
	# Casillas de item y de la barra: fondo hundido; el marco de rareza va encima (ItemSlot).
	t.set_type_variation("SlotButton", "Button")
	for state: String in ["normal", "hover", "pressed", "disabled", "focus"]:
		t.set_stylebox(state, "SlotButton", nine("slot", 3, 1) if state != "focus" else StyleBoxEmpty.new())
	t.set_stylebox("background", "ProgressBar", nine("bar_back", 1, 0))
	t.set_stylebox("fill", "ProgressBar", nine("bar_hp", 1, 0))
	t.set_stylebox("panel", "ItemList", nine("panel_light", 7, 4))
	t.set_stylebox("focus", "ItemList", StyleBoxEmpty.new())
	t.set_stylebox("selected", "ItemList", nine("button_pressed", 4, 2))
	t.set_stylebox("selected_focus", "ItemList", nine("button_pressed", 4, 2))
	t.set_stylebox("hovered", "ItemList", nine("button_hover", 4, 2))
	t.set_stylebox("cursor", "ItemList", StyleBoxEmpty.new())
	t.set_stylebox("cursor_unfocused", "ItemList", StyleBoxEmpty.new())
	t.set_stylebox("scroll", "VScrollBar", nine("scroll_track", 2, 0))
	t.set_stylebox("grabber", "VScrollBar", nine("scroll_grabber", 2, 0))
	t.set_stylebox("grabber_highlight", "VScrollBar", nine("scroll_grabber", 2, 0))
	t.set_stylebox("grabber_pressed", "VScrollBar", nine("scroll_grabber", 2, 0))
	t.set_stylebox("separator", "HSeparator", nine("separator", 0, 0))
	for key: String in ["checked", "checked_disabled"]:
		t.set_icon(key, "CheckBox", texture("check_on.png"))
	for key: String in ["unchecked", "unchecked_disabled"]:
		t.set_icon(key, "CheckBox", texture("check_off.png"))
	for state: String in ["normal", "hover", "pressed", "hover_pressed", "disabled", "focus"]:
		t.set_stylebox(state, "CheckBox", StyleBoxEmpty.new())
	t.set_constant("separation", "HSeparator", 3)
	for type: String in ["Label", "Button", "LineEdit", "TooltipLabel", "PopupMenu", "CheckBox", "ItemList", "LinkButton", "SpinBox", "AcceptDialog"]:
		t.set_color("font_color", type, TEXT)
		t.set_font_size("font_size", type, FONT_BODY)
	t.set_color("font_outline_color", "Button", OUTLINE)
	t.set_constant("outline_size", "Button", OUTLINE_THIN)
	t.set_color("font_hover_color", "Button", Color.WHITE)
	t.set_color("font_pressed_color", "Button", ACCENT)
	t.set_color("font_disabled_color", "Button", TEXT_DISABLED)
	t.set_color("font_placeholder_color", "LineEdit", TEXT_MUTED)
	t.set_color("font_selected_color", "ItemList", ACCENT)
	t.set_color("font_hovered_color", "ItemList", Color.WHITE)
	t.set_color("font_outline_color", "ItemList", OUTLINE)
	t.set_constant("outline_size", "ItemList", OUTLINE_THIN)
	t.set_constant("v_separation", "ItemList", 2)
	t.set_constant("icon_margin", "ItemList", 4)
	t.set_color("font_hover_color", "PopupMenu", Color.WHITE)
	t.set_color("font_outline_color", "TooltipLabel", OUTLINE)
	for key: String in ["normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size"]:
		t.set_font_size(key, "RichTextLabel", FONT_BODY)
	if f != null:
		# Sin cursiva: la cursiva usa la normal; la negrita, Alegreya Sans Bold.
		t.set_font("normal_font", "RichTextLabel", f)
		t.set_font("italics_font", "RichTextLabel", f)
		t.set_font("bold_font", "RichTextLabel", font_bold())
		t.set_font("bold_italics_font", "RichTextLabel", font_bold())
	t.set_color("default_color", "RichTextLabel", TEXT)
	t.set_color("font_outline_color", "RichTextLabel", OUTLINE)
	t.set_constant("line_separation", "RichTextLabel", 1)
	t.set_constant("separation", "VBoxContainer", GAP)
	t.set_constant("separation", "HBoxContainer", GAP)
	t.set_constant("h_separation", "GridContainer", GAP)
	t.set_constant("v_separation", "GridContainer", GAP)
	t.set_type_variation("SmallButton", "Button")
	t.set_font_size("font_size", "SmallButton", FONT_SMALL)
	_label_variation(t, "SmallLabel", FONT_SMALL, TEXT_MUTED)
	_label_variation(t, "TitleLabel", FONT_TITLE, ACCENT)
	t.set_font("font", "TitleLabel", font_title())
	t.set_color("font_outline_color", "TitleLabel", OUTLINE)
	t.set_constant("outline_size", "TitleLabel", OUTLINE_THIN)
	_label_variation(t, "HeadlineLabel", FONT_HEADLINE, TEXT)
	t.set_font("font", "HeadlineLabel", font_title())
	t.set_color("font_outline_color", "HeadlineLabel", OUTLINE)
	t.set_constant("outline_size", "HeadlineLabel", OUTLINE_THICK)
	_label_variation(t, "OutlinedLabel", FONT_BODY, TEXT)
	t.set_color("font_outline_color", "OutlinedLabel", OUTLINE)
	t.set_constant("outline_size", "OutlinedLabel", OUTLINE_THICK)
	return t


static func _label_variation(t: Theme, name: String, size: int, color: Color) -> void:
	t.set_type_variation(name, "Label")
	t.set_font_size("font_size", name, size)
	t.set_color("font_color", name, color)


## Barra con marco de madera: PanelContainer (marco) con un ProgressBar dentro (fondo hundido y relleno con brillo).
## `fill`: "hp", "mana", "energy", "rage", "xp", "cast" o "enemy". Devuelve el marco; la barra es su único hijo.
static func framed_bar(fill: String, height: int) -> PanelContainer:
	var frame := PanelContainer.new()
	frame.add_theme_stylebox_override("panel", nine("bar_frame", 3, 2))
	var bar := ProgressBar.new()
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(0, height)
	bar.add_theme_stylebox_override("fill", nine("bar_" + fill, 1, 0))
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	frame.add_child(bar)
	return frame


static func bar_fill_style(fill: String) -> StyleBox:
	return nine("bar_" + fill, 1, 0)
