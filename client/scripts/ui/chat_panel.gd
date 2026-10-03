class_name ChatPanel
extends Control
## Chat (HU-060): Enter enfoca, Enter envía, Esc cancela; sin prefijo → say; `/g`, `/w Nombre`, `/p`, `/invite`, `/leave`,
## `/kick`, `/duel`, `/rendirse`, `/trade`, `/who`; colores por canal; BBCode escapado; burbuja 4 s sobre la cabeza (el mundo la dibuja).
## Mientras la caja tiene el foco, el mundo no mueve al personaje (`is_typing`). Tras FADE_DELAY_SEC sin mensajes se
## desvanece en FADE_SEC; vuelve al llegar un mensaje o al pulsar Enter, y no se esconde mientras escribes.
## Historial (HU-097): rueda del ratón sobre el registro o Re Pág / Av Pág; leyendo arriba no salta al final al llegar
## mensajes (aparece "↓ nuevos") y el chat no se desvanece con el ratón encima o desplazado hacia arriba.

signal bubble_requested(from: String, text: String)
signal command(name: String, args: String)

const COLORS := {"say": "#fbf3e0", "global": "#f79617", "party": "#8fd3ff", "whisper": "#ed8099", "system": "#f9c22b"}
const MAX_LINES := 200
## Estrecho para no tapar la barra de casteo, centrada sobre la barra rápida.
const WIDTH := 156
const LOG_HEIGHT := 48
const INPUT_HEIGHT := 14
const FADE_DELAY_SEC := 8.0
const FADE_SEC := 1.0

var _log: RichTextLabel
var _input: LineEdit
var _lines: Array[String] = []
## Aviso de mensajes nuevos mientras se lee el historial; al pulsarlo vuelve al final.
var _new_badge: Button
var _last_activity_ms: int = 0


func is_typing() -> bool:
	return _input != null and _input.has_focus()


func _ready() -> void:
	custom_minimum_size = Vector2(WIDTH, LOG_HEIGHT + INPUT_HEIGHT + UiTheme.GAP)
	mouse_filter = Control.MOUSE_FILTER_PASS
	# Abajo a la izquierda, encima de la barra rápida (CombatHud).
	UiTheme.dock(self, Control.PRESET_BOTTOM_LEFT, UiTheme.SCREEN_MARGIN, -(CombatHud.HOTBAR_HEIGHT + UiTheme.SCREEN_MARGIN + UiTheme.GAP))
	# Fondo tenue detrás del registro para leerlo sobre cualquier suelo; el texto lleva contorno.
	var back := PanelContainer.new()
	back.add_theme_stylebox_override("panel", UiTheme.nine("tooltip", 4, 2, UiTheme.TOOLTIP_BG))
	back.self_modulate = Color(1, 1, 1, 0.4)
	back.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	back.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(back)
	var v := VBoxContainer.new()
	v.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	v.grow_vertical = Control.GROW_DIRECTION_BEGIN
	add_child(v)
	_log = RichTextLabel.new()
	_log.bbcode_enabled = true
	_log.scroll_following = false  # se sigue el final a mano: solo si ya estabas abajo
	_log.custom_minimum_size = Vector2(WIDTH, LOG_HEIGHT)
	_log.add_theme_font_size_override("normal_font_size", UiTheme.FONT_SMALL)
	_log.add_theme_constant_override("outline_size", UiTheme.OUTLINE_THICK)
	_log.add_theme_constant_override("line_separation", 0)
	_log.mouse_filter = Control.MOUSE_FILTER_PASS  # recibe la rueda; los clics siguen al mundo
	v.add_child(_log)
	_new_badge = Button.new()
	_new_badge.text = "↓ nuevos"
	_new_badge.theme_type_variation = "SmallButton"
	_new_badge.visible = false
	_new_badge.focus_mode = Control.FOCUS_NONE
	_new_badge.pressed.connect(scroll_to_end)
	_log.add_child(_new_badge)
	_new_badge.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT, Control.PRESET_MODE_MINSIZE, 1)
	_log.get_v_scroll_bar().value_changed.connect(func(_v: float) -> void:
		if is_at_bottom():
			_new_badge.visible = false)
	_input = LineEdit.new()
	_input.placeholder_text = "Enter para escribir"
	_input.custom_minimum_size = Vector2(WIDTH, INPUT_HEIGHT)
	_input.add_theme_font_size_override("font_size", UiTheme.FONT_SMALL)
	_input.max_length = int(Content.rule("social", "chatMaxLength", 200))
	_input.text_submitted.connect(_on_submitted)
	_input.gui_input.connect(_on_input_gui)
	v.add_child(_input)
	GameState.chat_received.connect(add_message)
	GameState.notice.connect(func(t: String) -> void: add_message("system", "", t))
	_last_activity_ms = Time.get_ticks_msec()


func _process(_delta: float) -> void:
	update_fade(Time.get_ticks_msec())


## Opacidad del chat según el tiempo desde el último mensaje o desde que se dejó de escribir.
func update_fade(now_ms: int) -> void:
	if is_typing() or not is_at_bottom() or _is_hovered():
		_last_activity_ms = now_ms
	var idle_sec := (now_ms - _last_activity_ms) / 1000.0
	modulate.a = clampf(1.0 - (idle_sec - FADE_DELAY_SEC) / FADE_SEC, 0.0, 1.0)


## Enter: enfoca la caja y muestra el chat.
func open_input() -> void:
	_last_activity_ms = Time.get_ticks_msec()
	_input.grab_focus()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("chat_focus") and not is_typing():
		open_input()
		get_viewport().set_input_as_handled()
	elif event is InputEventKey and event.is_pressed() and (event as InputEventKey).keycode in [KEY_PAGEUP, KEY_PAGEDOWN]:
		scroll_page(-1 if (event as InputEventKey).keycode == KEY_PAGEUP else 1)
		get_viewport().set_input_as_handled()


## ¿Se ve el último mensaje?
func is_at_bottom() -> bool:
	var bar := _log.get_v_scroll_bar()
	return bar.value >= bar.max_value - bar.page - 1.0


## Desplaza una página del registro hacia arriba (−1) o abajo (1).
func scroll_page(direction: int) -> void:
	var bar := _log.get_v_scroll_bar()
	bar.value = clampf(bar.value + direction * maxf(1.0, bar.page - 8.0), 0.0, bar.max_value)
	_last_activity_ms = Time.get_ticks_msec()


func scroll_to_end() -> void:
	_log.scroll_to_line(maxi(0, _log.get_line_count() - 1))
	var bar := _log.get_v_scroll_bar()
	bar.value = bar.max_value
	_new_badge.visible = false


func _is_hovered() -> bool:
	return is_visible_in_tree() and get_global_rect().has_point(get_global_mouse_position())


func _on_input_gui(event: InputEvent) -> void:
	# Mientras escribes, la caja se queda las teclas: Re Pág / Av Pág también desplazan el historial.
	if event is InputEventKey and event.is_pressed() and (event as InputEventKey).keycode in [KEY_PAGEUP, KEY_PAGEDOWN]:
		scroll_page(-1 if (event as InputEventKey).keycode == KEY_PAGEUP else 1)
		_input.accept_event()
		return
	if event.is_action_pressed("ui_cancel") and is_typing():
		_input.text = ""
		_input.release_focus()
		get_viewport().set_input_as_handled()


func _on_submitted(text: String) -> void:
	_input.text = ""
	_input.release_focus()
	var t := text.strip_edges()
	if t.is_empty():
		return
	if t.begins_with("/"):
		var sp := t.find(" ")
		var cmd := (t.substr(1, sp - 1) if sp > 0 else t.substr(1)).to_lower()
		var args := t.substr(sp + 1).strip_edges() if sp > 0 else ""
		match cmd:
			"g":
				Net.send("ChatSend", {"channel": "global", "text": args})
			"p":
				Net.send("ChatSend", {"channel": "party", "text": args})
			"w":
				var sp2 := args.find(" ")
				if sp2 > 0:
					Net.send("ChatSend", {"channel": "whisper", "text": args.substr(sp2 + 1), "to": args.substr(0, sp2)})
			"who":
				Net.send("ChatSend", {"channel": "who", "text": "/who"})
			_:
				command.emit(cmd, args)
		return
	Net.send("ChatSend", {"channel": "say", "text": t})
	bubble_requested.emit(GameState.character_name, t)


## Escapa `[` para que nadie inyecte BBCode (CA5).
static func escape(text: String) -> String:
	return text.replace("[", "[lb]")


func add_message(channel: String, from: String, text: String) -> void:
	var color: String = COLORS.get(channel, "#ffffff")
	var prefix := ""
	match channel:
		"global": prefix = "[G] "
		"party": prefix = "[P] "
		"whisper": prefix = "[W] "
		"system": prefix = ""
	var who := ("%s: " % escape(from)) if not from.is_empty() else ""
	var line := "[color=%s]%s%s%s[/color]" % [color, prefix, who, escape(text)]
	var follow := is_at_bottom()
	# Se añade sin rehacer el texto: rehacerlo devolvía el registro al principio y no se podía leer hacia atrás.
	_log.append_text(("\n" if not _lines.is_empty() else "") + line)
	_lines.append(line)
	while _lines.size() > MAX_LINES:
		_lines.remove_at(0)
		_log.remove_paragraph(0)
	if follow:
		scroll_to_end.call_deferred()
	else:
		_new_badge.visible = true
	_last_activity_ms = Time.get_ticks_msec()
	if channel == "say" and not from.is_empty() and from != GameState.character_name:
		bubble_requested.emit(from, text)
