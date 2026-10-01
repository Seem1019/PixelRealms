extends Control
## Escena inicial (HU-005): muestra el título y el estado del contenido; conecta al servidor para medir el RTT (HU-006).

@onready var _status: Label = %StatusLabel


func _ready() -> void:
	if Content.loaded:
		_status.text = "Contenido cargado · pulsa F3 para ver el RTT"
	else:
		_status.text = "Falta client/content/: ejecuta tools/sync_content.gd"
	Net.connect_to(Settings.ws_url())
	Net.connected.connect(func() -> void: _status.text = "Conectado a %s" % Settings.ws_url())
	Net.disconnected.connect(func(reason: String) -> void: _status.text = "Desconectado (%s) · reintento %d/%d" % [reason, Net.reconnect_attempt(), Net.MAX_ATTEMPTS])
