class_name ChatPanel
extends Control
## Chat (HU-060): Enter enfoca, Enter envía, Esc cancela; sin prefijo → say; `/g`, `/w Nombre`, `/p`, `/invite`, `/leave`,
## `/kick`, `/duel`, `/rendirse`, `/trade`, `/who`; colores por canal; BBCode escapado; burbuja 4 s sobre la cabeza (el mundo la dibuja).
## Mientras la caja tiene el foco, el mundo no mueve al personaje (`is_typing`). Tras FADE_DELAY_SEC sin mensajes se
## desvanece en FADE_SEC; vuelve al llegar un mensaje o al pulsar Enter, y no se esconde mientras escribes.

signal bubble_requested(from: String, text: String)
signal command(name: String, args: String)

const COLORS := {"say": "#fbf3e0", "global": "#f79617", "party": "#8fd3ff", "whisper": "#ed8099", "system": "#f9c22b"}
const MAX_LINES := 60
## Estrecho para no tapar la barra de casteo, centrada sobre la barra rápida.
const WIDTH := 156
const LOG_HEIGHT := 48
const INPUT_HEIGHT := 14
const FADE_DELAY_SEC := 8.0
const FADE_SEC := 1.0

var _log: RichTextLabel
var _input: LineEdit
var _lines: Array[String] = []
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
	_log.scroll_following = true
	_log.custom_minimum_size = Vector2(WIDTH, LOG_HEIGHT)
	_log.add_theme_font_size_override("normal_font_size", UiTheme.FONT_SMALL)
	_log.add_theme_constant_override("outline_size", 2)
	_log.add_theme_constant_override("line_separation", 0)
	_log.mouse_filter = Control.MOUSE_FILTER_IGNORE
	v.add_child(_log)
	_input = LineEdit.new()
	_input.placeholder_text = "Enter para escribir"
	_input.custom_minimum_size = Vector2(WIDTH, INPUT_HEIGHT)
	_input.add_theme_font_size_override("font_size", UiTheme.FONT_SMALL)
	_input.max_length = 200
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
	if is_typing():
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


func _on_input_gui(event: InputEvent) -> void:
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
	_lines.append("[color=%s]%s%s%s[/color]" % [color, prefix, who, escape(text)])
	while _lines.size() > MAX_LINES:
		_lines.remove_at(0)
	_log.text = "\n".join(_lines)
	_last_activity_ms = Time.get_ticks_msec()
	if channel == "say" and not from.is_empty() and from != GameState.character_name:
		bubble_requested.emit(from, text)
