extends Control
## Pantalla de inicio de sesión y registro (HU-010 CA4, HU-011 CA4/CA5).

@onready var _username: LineEdit = %Username
@onready var _password: LineEdit = %Password
@onready var _remember: CheckBox = %Remember
@onready var _username_error: Label = %UsernameError
@onready var _password_error: Label = %PasswordError
@onready var _status: Label = %Status
@onready var _login_button: Button = %LoginButton
@onready var _register_button: Button = %RegisterButton

var _api: ApiClient


func _ready() -> void:
	_api = get_node("/root/Api") as ApiClient
	var remembered := str(Settings.get_value("auth", "username", ""))
	_username.text = remembered
	_remember.button_pressed = not remembered.is_empty()
	_login_button.pressed.connect(_on_login)
	_register_button.pressed.connect(_on_register)
	_password.text_submitted.connect(func(_t: String) -> void: _on_login())
	var notice := str(get_tree().get_meta("login_notice", "")) if get_tree().has_meta("login_notice") else ""
	if not notice.is_empty():
		_status.text = notice
		get_tree().remove_meta("login_notice")


func _clear_errors() -> void:
	_username_error.text = ""
	_password_error.text = ""
	_status.text = ""


func _show_field_errors(errors: Array) -> void:
	for e: Variant in errors:
		if e is Dictionary:
			var fe: Dictionary = e
			var text := ApiMessages.field_text(fe)
			match str(fe.get("field", "")):
				"username":
					_username_error.text = text
				"password":
					_password_error.text = text
				_:
					_status.text = text


func _set_busy(busy: bool) -> void:
	_login_button.disabled = busy
	_register_button.disabled = busy


func _on_login() -> void:
	_clear_errors()
	_set_busy(true)
	var r := await _api.login(_username.text.strip_edges(), _password.text)
	_set_busy(false)
	if r.ok():
		_remember_user()
		get_tree().change_scene_to_file("res://scenes/character_select/character_select.tscn")
	else:
		_status.text = r.message


func _on_register() -> void:
	_clear_errors()
	_set_busy(true)
	var username := _username.text.strip_edges()
	var password := _password.text
	var r := await _api.register(username, password)
	if not r.ok():
		_set_busy(false)
		if r.field_errors.is_empty():
			_status.text = r.message
		else:
			_show_field_errors(r.field_errors)
		return
	# Registro correcto: inicia sesión automáticamente (HU-010 CA4).
	var l := await _api.login(username, password)
	_set_busy(false)
	if l.ok():
		_remember_user()
		get_tree().change_scene_to_file("res://scenes/character_select/character_select.tscn")
	else:
		_status.text = l.message


func _remember_user() -> void:
	# Se guarda el usuario, nunca la contraseña (HU-011 CA4).
	Settings.set_value("auth", "username", _username.text.strip_edges() if _remember.button_pressed else "")
