class_name DropCatcher
extends Control
## Control a pantalla completa (deja pasar el ratón) que recibe los items soltados fuera de cualquier casilla (HU-056 CA2).

signal item_dropped_outside(item_id: String)
signal hotbar_slot_dropped_outside(slot: int)


func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_PASS


func _can_drop_data(_at: Vector2, data: Variant) -> bool:
	return data is Dictionary and ((data as Dictionary).has("itemId") or (data as Dictionary).has("hotbarSlot"))


func _drop_data(_at: Vector2, data: Variant) -> void:
	var d: Dictionary = data
	if d.has("hotbarSlot"):
		hotbar_slot_dropped_outside.emit(int(d["hotbarSlot"]))
	elif d.has("itemId"):
		item_dropped_outside.emit(str(d["itemId"]))
