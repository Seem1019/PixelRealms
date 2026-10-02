extends Control
## Selección de personaje (HU-012 CA1/CA4, HU-013 CA2, HU-014 CA1): lista, crear con las 4 clases de Content, borrar con
## confirmación por nombre y "Jugar" (ticket → WebSocket → Hello).

const MAX_CHARACTERS := 4

@onready var _list: ItemList = %CharacterList
@onready var _new_button: Button = %NewButton
@onready var _delete_button: Button = %DeleteButton
@onready var _play_button: Button = %PlayButton
@onready var _logout_button: Button = %LogoutButton
@onready var _status: Label = %Status
@onready var _create_panel: PanelContainer = %CreatePanel
@onready var _name_edit: LineEdit = %NameEdit
@onready var _class_list: ItemList = %ClassList
@onready var _class_info: Label = %ClassInfo
@onready var _create_confirm: Button = %CreateConfirm
@onready var _create_cancel: Button = %CreateCancel
@onready var _create_error: Label = %CreateError
@onready var _delete_panel: PanelContainer = %DeletePanel
@onready var _delete_name: LineEdit = %DeleteName
@onready var _delete_confirm: Button = %DeleteConfirm
@onready var _delete_cancel: Button = %DeleteCancel
@onready var _delete_prompt: Label = %DeletePrompt

var _api: ApiClient
var _characters: Array = []
var _class_ids: Array[String] = []


func _ready() -> void:
	_api = get_node("/root/Api") as ApiClient
	_new_button.pressed.connect(_open_create)
	_delete_button.pressed.connect(_open_delete)
	_play_button.pressed.connect(_on_play)
	_logout_button.pressed.connect(_on_logout)
	_create_confirm.pressed.connect(_on_create_confirm)
	_create_cancel.pressed.connect(func() -> void: _create_panel.visible = false)
	_delete_confirm.pressed.connect(_on_delete_confirm)
	_delete_cancel.pressed.connect(func() -> void: _delete_panel.visible = false)
	_class_list.item_selected.connect(_on_class_selected)
	_list.item_selected.connect(func(_i: int) -> void: _refresh_buttons())
	EventBus.ui_error.connect(_on_ui_error)
	_create_panel.visible = false
	_delete_panel.visible = false
	_fill_classes()
	await _reload()


func _reload() -> void:
	_status.text = "Cargando…"
	var r := await _api.list_characters()
	if not r.ok():
		_status.text = r.message
		return
	show_characters(r.data if r.data is Array else [])


## Lista de personajes: sprite de su clase, nombre y debajo clase y nivel.
func show_characters(characters: Array) -> void:
	_characters = characters
	_list.clear()
	_list.fixed_icon_size = Vector2i(32, 32)  # retrato del cuerpo en tamaño lógico aunque la hoja sea HD (HU-099)
	for c: Variant in _characters:
		var d: Dictionary = c
		var class_id := str(d.get("classId", ""))
		var cls := Content.character_class(class_id)
		var sprite := EntitySprites.portrait(EntitySprites.ref_for("player", class_id, class_id), false)
		var idx := _list.add_item("%s  ·  %s  ·  Nv %d" % [d.get("name", "?"), cls.get("name", class_id), int(d.get("level", 1))], sprite)
		_list.set_item_tooltip_enabled(idx, false)
	_status.text = "" if not _characters.is_empty() else "No tienes personajes: pulsa Nuevo"
	if not _characters.is_empty():
		_list.select(0)
	_refresh_buttons()


func _refresh_buttons() -> void:
	var has_selection := not _list.get_selected_items().is_empty()
	_play_button.disabled = not has_selection
	_delete_button.disabled = not has_selection
	# HU-012 CA4: con 4 personajes el botón Nuevo se deshabilita.
	_new_button.disabled = _characters.size() >= MAX_CHARACTERS


func _selected_character() -> Dictionary:
	var sel := _list.get_selected_items()
	if sel.is_empty():
		return {}
	return _characters[sel[0]]


func _fill_classes() -> void:
	_class_list.clear()
	_class_ids.clear()
	for cls: Variant in Content.classes():
		var d: Dictionary = cls
		_class_ids.append(str(d["id"]))
		_class_list.add_item(str(d.get("name", d["id"])), UiTheme.icon("classes/" + str(d["id"])))
	if not _class_ids.is_empty():
		_class_list.select(0)
		_on_class_selected(0)


func _on_class_selected(index: int) -> void:
	var d := Content.character_class(_class_ids[index])
	_class_info.text = "%s · recurso: %s\n%s" % [_role_text(str(d.get("role", ""))), _resource_text(str(d.get("resource", ""))), str(d.get("description", ""))]


static func _role_text(role: String) -> String:
	match role:
		"tank": return "Tanque"
		"melee_dps": return "Daño cuerpo a cuerpo"
		"ranged_dps": return "Daño a distancia"
		"healer": return "Sanador"
	return role


static func _resource_text(resource: String) -> String:
	match resource:
		"mana": return "maná"
		"rage": return "ira"
		"energy": return "energía"
	return resource


func _open_create() -> void:
	_create_error.text = ""
	_name_edit.text = ""
	_create_panel.visible = true
	_name_edit.grab_focus()


func _on_create_confirm() -> void:
	var sel := _class_list.get_selected_items()
	if sel.is_empty():
		return
	_create_confirm.disabled = true
	var r := await _api.create_character(_name_edit.text.strip_edges(), _class_ids[sel[0]])
	_create_confirm.disabled = false
	if r.ok():
		_create_panel.visible = false
		await _reload()
	elif not r.field_errors.is_empty():
		_create_error.text = ApiMessages.field_text(r.field_errors[0])
	else:
		_create_error.text = r.message


func _open_delete() -> void:
	var c := _selected_character()
	if c.is_empty():
		return
	_delete_prompt.text = "Escribe el nombre '%s' para confirmar" % str(c.get("name", ""))
	_delete_name.text = ""
	_delete_panel.visible = true
	_delete_name.grab_focus()


func _on_delete_confirm() -> void:
	var c := _selected_character()
	if c.is_empty():
		return
	# HU-013 CA2: hay que escribir el nombre exacto.
	if _delete_name.text.strip_edges() != str(c.get("name", "")):
		_delete_prompt.text = "El nombre no coincide"
		return
	_delete_confirm.disabled = true
	var r := await _api.delete_character(str(c.get("id", "")))
	_delete_confirm.disabled = false
	_delete_panel.visible = false
	if not r.ok():
		_status.text = r.message
	await _reload()


func _on_play() -> void:
	var c := _selected_character()
	if c.is_empty():
		return
	_play_button.disabled = true
	_status.text = "Entrando…"
	var r := await _api.game_ticket(str(c.get("id", "")))
	if not r.ok():
		_status.text = r.message
		_play_button.disabled = false
		return
	var ticket := str((r.data as Dictionary).get("ticket", ""))
	GameState.pending_ticket = ticket
	GameState.pending_character_id = str(c.get("id", ""))
	get_tree().change_scene_to_file("res://scenes/world/world.tscn")


func _on_logout() -> void:
	_api.clear_token()
	get_tree().change_scene_to_file("res://scenes/login/login.tscn")


func _on_ui_error(code: String, _req_id: int) -> void:
	if code == "session_expired":
		get_tree().set_meta("login_notice", ApiMessages.text_for(code))
		get_tree().change_scene_to_file("res://scenes/login/login.tscn")
