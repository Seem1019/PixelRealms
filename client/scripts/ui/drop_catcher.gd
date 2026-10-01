class_name DropCatcher
extends Control
## Control a pantalla completa (deja pasar el ratón) que recibe los items soltados fuera de cualquier casilla (HU-056 CA2).

signal item_dropped_outside(item_id: String)


func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_PASS


func _can_drop_data(_at: Vector2, data: Variant) -> bool:
	return data is Dictionary and (data as Dictionary).has("itemId")


func _drop_data(_at: Vector2, data: Variant) -> void:
	item_dropped_outside.emit(str((data as Dictionary).get("itemId", "")))
