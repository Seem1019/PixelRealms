class_name GameMenu
extends Control
## Menú del juego (HU-015): Esc (cuando no queda nada que cerrar o cancelar) o el engranaje del HUD. "Continuar" lo cierra;
## "Volver a selección de personaje" y "Salir del juego" piden `Logout` al servidor y esperan su `LoggedOut` (en combate
## llega `Error{in_combat}` y se avisa "No puedes salir en combate"). Mientras está abierto la barra rápida y el movimiento
## no actúan (el mundo mira `is_open()`), y un velo oscuro tapa el mundo.

signal resume_requested
signal character_select_requested
signal quit_requested

var _panel: PanelContainer
var _buttons: Array[Button] = []
## Esperando la respuesta del servidor a un Logout: los botones de salir quedan desactivados.
var waiting: bool = false:
	set(value):
		waiting = value
		for i: int in range(1, _buttons.size()):
			_buttons[i].disabled = value


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP  # el clic no llega al mundo
	visible = false
	var veil := ColorRect.new()
	veil.color = Color(UiTheme.OUTLINE, 0.55)
	veil.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	veil.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(veil)
	_panel = PanelContainer.new()
	_panel.custom_minimum_size = Vector2(150, 0)
	add_child(_panel)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", UiTheme.GAP + 1)
	_panel.add_child(box)
	var title := Label.new()
	title.text = "Menú"
	title.theme_type_variation = "HeadlineLabel"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)
	for entry: Array in [["Continuar", resume_requested], ["Volver a selección de personaje", character_select_requested], ["Salir del juego", quit_requested]]:
		var b := Button.new()
		b.text = entry[0]
		b.focus_mode = Control.FOCUS_ALL
		var sig: Signal = entry[1]
		b.pressed.connect(func() -> void: sig.emit())
		box.add_child(b)
		_buttons.append(b)
	UiTheme.dock(_panel, Control.PRESET_CENTER)


func open() -> void:
	waiting = false
	visible = true
	_buttons[0].grab_focus()


func close() -> void:
	visible = false
	waiting = false


func is_open() -> bool:
	return visible


func button(text: String) -> Button:
	for b: Button in _buttons:
		if b.text == text:
			return b
	return null


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		resume_requested.emit()
