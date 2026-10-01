extends Control
## Escena inicial (HU-005): comprueba el contenido y pasa al login.

@onready var _status: Label = %StatusLabel


func _ready() -> void:
	if not Content.loaded:
		_status.text = "Falta client/content/: ejecuta tools/sync_content.gd"
		return
	_status.text = "Contenido cargado"
	await get_tree().create_timer(0.3).timeout
	get_tree().change_scene_to_file("res://scenes/login/login.tscn")
