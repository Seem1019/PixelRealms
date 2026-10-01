class_name DebugOverlay
extends CanvasLayer
## Overlay de depuración (F3): RTT, estado de conexión y, desde HU-022, inputs pendientes / ackSeq / error de reconciliación.

var pending_inputs: int = 0
var ack_seq: int = 0
var reconcile_error_px: float = 0.0

@onready var _label: Label = %InfoLabel


func _ready() -> void:
	visible = false
	EventBus.rtt_updated.connect(func(_rtt: int) -> void: _refresh())
	EventBus.connection_changed.connect(func(_c: bool) -> void: _refresh())
	_refresh()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("debug_overlay"):
		visible = not visible
		_refresh()


func _refresh() -> void:
	if _label == null:
		return
	var rtt := "%d ms" % Net.rtt_ms if Net.rtt_ms >= 0 else "—"
	_label.text = "RTT %s · %s\ninputs %d · ack %d · err %.1f px" % [
		rtt, "conectado" if Net.is_connected else "sin conexión", pending_inputs, ack_seq, reconcile_error_px]
