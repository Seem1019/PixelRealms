extends Node
## Configuración local del jugador en user://settings.cfg (URL del servidor, usuario recordado, posiciones de ventanas).

const PATH := "user://settings.cfg"
const DEFAULT_SERVER_URL := "http://localhost:5080"

var _config := ConfigFile.new()


func _ready() -> void:
	var err := _config.load(PATH)
	if err != OK and err != ERR_FILE_NOT_FOUND:
		push_warning("No se pudo leer %s (código %d)" % [PATH, err])


## URL base HTTP del servidor (la de WebSocket se deriva: ws(s)://host/ws).
func server_url() -> String:
	if OS.has_feature("release") and not OS.has_feature("editor"):
		return str(_config.get_value("net", "server_url", DEFAULT_SERVER_URL))
	return str(_config.get_value("net", "server_url", DEFAULT_SERVER_URL))


func ws_url() -> String:
	var base := server_url()
	if base.begins_with("https://"):
		return "wss://" + base.trim_prefix("https://") + "/ws"
	return "ws://" + base.trim_prefix("http://") + "/ws"


func get_value(section: String, key: String, default: Variant) -> Variant:
	return _config.get_value(section, key, default)


func set_value(section: String, key: String, value: Variant) -> void:
	_config.set_value(section, key, value)
	var err := _config.save(PATH)
	if err != OK:
		push_warning("No se pudo guardar %s (código %d)" % [PATH, err])
