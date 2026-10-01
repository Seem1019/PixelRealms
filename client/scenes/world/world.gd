extends Node2D
## Escena del mundo (HU-014 CA2, HU-020 CA3 provisional): conecta con el ticket, envía Hello, carga el mapa del Welcome y
## coloca al jugador. El movimiento, la predicción y los demás jugadores llegan en HU-021/022/023.

@onready var _ground: PlaceholderMapRenderer = %Ground
@onready var _above: PlaceholderMapRenderer = %Above
@onready var _entities: Node2D = %Entities
@onready var _player: Node2D = %PlayerSelf
@onready var _player_name: Label = %PlayerName
@onready var _camera: Camera2D = %Camera
@onready var _hud_status: Label = %HudStatus

var map: TmjMap


func _ready() -> void:
	Net.register_handler("Welcome", _on_welcome)
	Net.disconnected.connect(_on_disconnected)
	EventBus.ui_error.connect(_on_ui_error)
	_player.visible = false
	_hud_status.text = "Conectando…"
	var ticket := GameState.pending_ticket
	GameState.pending_ticket = ""
	Net.connect_to(Settings.ws_url(), ticket)
	if not Net.connected.is_connected(_on_connected):
		Net.connected.connect(_on_connected.bind(ticket))


func _on_connected(ticket: String) -> void:
	Net.send("Hello", {"protocolVersion": Protocol.VERSION, "ticket": ticket})


func _on_welcome(d: Dictionary) -> void:
	GameState._on_welcome(d)
	_hud_status.text = ""
	_load_map(GameState.map_id)
	var self_state: Dictionary = d.get("self", {})
	_player.position = Vector2(float(self_state.get("x", 0)), float(self_state.get("y", 0)))
	_player_name.text = GameState.character_name
	_player.visible = true
	_camera.position = _player.position


func _load_map(map_id: String) -> void:
	map = TmjMap.load_from("res://maps/%s.tmj" % map_id)
	if map == null:
		_hud_status.text = "Falta client/maps/%s.tmj (tools/sync_content.gd)" % map_id
		return
	_ground.setup(map, ["ground", "detail", "walls"])
	_above.setup(map, ["above"])
	var ts := map.tile_size
	_camera.limit_left = 0
	_camera.limit_top = 0
	_camera.limit_right = map.width * ts
	_camera.limit_bottom = map.height * ts


func _on_disconnected(reason: String) -> void:
	_hud_status.text = "Reconectando… (intento %d/%d)" % [Net.reconnect_attempt(), Net.MAX_ATTEMPTS] if Net.reconnect_attempt() > 0 else "Desconectado: %s" % reason


func _on_ui_error(code: String, _req_id: int) -> void:
	match code:
		"bad_version", "bad_ticket", "disconnected":
			get_tree().set_meta("login_notice", ApiMessages.text_for(code))
			get_tree().change_scene_to_file("res://scenes/login/login.tscn")
		_:
			_hud_status.text = ApiMessages.text_for(code)
