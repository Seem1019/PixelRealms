extends Node
## Conexión WebSocket con el servidor (skill godot-client §Red). Envía intenciones, recibe sobres {t, d} y los despacha
## a `_handlers[t]`; emite `message_received(t, d)` y señales específicas. Reconexión con backoff 1-2-4-8 s (máx. 5).

signal message_received(type: String, data: Dictionary)
signal connected
signal disconnected(reason: String)
signal snapshot(data: Dictionary)
signal combat_events(data: Dictionary)

const BACKOFF_SEC: Array[float] = [1.0, 2.0, 4.0, 8.0, 8.0]
const MAX_ATTEMPTS := 5

var rtt_ms: int = -1
var simulated_latency_ms: int = 0
var is_connected: bool = false

var _peer: WebSocketPeer = WebSocketPeer.new()
var _handlers: Dictionary = {}  # String → Callable
var _url: String = ""
var _ticket: String = ""
var _was_open: bool = false
var _attempt: int = 0
var _reconnect_at_msec: int = -1
var _ping_timer: float = 0.0
var _next_req_id: int = 1
var _auto_reconnect: bool = false
var _delayed_out: Array[Dictionary] = []  # [{at, text}] con simulated_latency_ms
var _delayed_in: Array[Dictionary] = []  # [{at, text}]


func _ready() -> void:
	_handlers["Pong"] = _on_pong
	_handlers["Error"] = _on_error
	_handlers["Snapshot"] = func(d: Dictionary) -> void: snapshot.emit(d)
	_handlers["CombatEvents"] = func(d: Dictionary) -> void: combat_events.emit(d)


## Conecta a `url` (ws://host/ws). Con `ticket`, lo añade como ?ticket= (HU-014).
func connect_to(url: String, ticket: String = "") -> void:
	_url = url
	_ticket = ticket
	_attempt = 0
	_auto_reconnect = true
	_open()


func disconnect_from_server() -> void:
	_auto_reconnect = false
	if _peer.get_ready_state() == WebSocketPeer.STATE_OPEN:
		_peer.close(1000, "bye")


## Envía un sobre {t, d}. Devuelve false si no hay conexión.
func send(type: String, data: Dictionary = {}) -> bool:
	if _peer.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return false
	var text := JSON.stringify({"t": type, "d": data})
	if text.length() > Protocol.MAX_MESSAGE_BYTES:
		push_error("Mensaje %s demasiado grande (%d bytes)" % [type, text.length()])
		return false
	if simulated_latency_ms > 0:
		_delayed_out.append({"at": Time.get_ticks_msec() + simulated_latency_ms / 2, "text": text})
		return true
	var err := _peer.send_text(text)
	return err == OK


func next_req_id() -> int:
	_next_req_id += 1
	return _next_req_id


## Registra el handler de un tipo de mensaje del servidor (lo usan GameState y la UI).
func register_handler(type: String, handler: Callable) -> void:
	_handlers[type] = handler


func _process(delta: float) -> void:
	_peer.poll()
	var state := _peer.get_ready_state()
	match state:
		WebSocketPeer.STATE_OPEN:
			if not _was_open:
				_was_open = true
				is_connected = true
				_attempt = 0
				connected.emit()
				EventBus.connection_changed.emit(true)
			while _peer.get_available_packet_count() > 0:
				var text := _peer.get_packet().get_string_from_utf8()
				if simulated_latency_ms > 0:
					_delayed_in.append({"at": Time.get_ticks_msec() + simulated_latency_ms / 2, "text": text})
				else:
					_dispatch(text)
			_flush_delayed()
			_ping_timer += delta
			if _ping_timer >= Protocol.PING_INTERVAL_SEC:
				_ping_timer = 0.0
				send("Ping", {"clientTime": Time.get_ticks_msec()})
		WebSocketPeer.STATE_CLOSED:
			if _was_open or _reconnect_at_msec < 0:
				var reason := _peer.get_close_reason()
				_was_open = false
				is_connected = false
				disconnected.emit(reason)
				EventBus.connection_changed.emit(false)
				_schedule_reconnect()
			elif Time.get_ticks_msec() >= _reconnect_at_msec:
				_reconnect_at_msec = -1
				_open()
		_:
			pass


## Latencia simulada (HU-022 CA2): la mitad al enviar y la mitad al recibir.
func _flush_delayed() -> void:
	var now := Time.get_ticks_msec()
	while not _delayed_out.is_empty() and int(_delayed_out[0]["at"]) <= now:
		var item: Dictionary = _delayed_out.pop_front()
		_peer.send_text(str(item["text"]))
	while not _delayed_in.is_empty() and int(_delayed_in[0]["at"]) <= now:
		var item_in: Dictionary = _delayed_in.pop_front()
		_dispatch(str(item_in["text"]))


func _open() -> void:
	var url := _url if _ticket.is_empty() else "%s?ticket=%s" % [_url, _ticket]
	_peer = WebSocketPeer.new()
	_peer.inbound_buffer_size = 1 << 20
	var err := _peer.connect_to_url(url)
	if err != OK:
		push_warning("No se pudo iniciar la conexión a %s (código %d)" % [url, err])
		_schedule_reconnect()


func _schedule_reconnect() -> void:
	if not _auto_reconnect:
		_reconnect_at_msec = -1
		return
	if _attempt >= MAX_ATTEMPTS:
		_auto_reconnect = false
		_reconnect_at_msec = -1
		EventBus.ui_error.emit("disconnected", 0)
		return
	var wait := BACKOFF_SEC[mini(_attempt, BACKOFF_SEC.size() - 1)]
	_attempt += 1
	_reconnect_at_msec = Time.get_ticks_msec() + int(wait * 1000.0)


## Intento actual de reconexión (1..5) para "Reconectando… (intento 2/5)".
func reconnect_attempt() -> int:
	return _attempt


func _dispatch(text: String) -> void:
	var parsed: Variant = JSON.parse_string(text)
	if not (parsed is Dictionary):
		push_warning("Sobre inválido del servidor")
		return
	var envelope: Dictionary = parsed
	var type := str(envelope.get("t", ""))
	var data: Variant = envelope.get("d", {})
	var payload: Dictionary = data if data is Dictionary else {}
	message_received.emit(type, payload)
	if _handlers.has(type):
		var handler: Callable = _handlers[type]
		handler.call(payload)


func _on_pong(d: Dictionary) -> void:
	var sent := int(d.get("clientTime", 0))
	rtt_ms = Time.get_ticks_msec() - sent
	EventBus.rtt_updated.emit(rtt_ms)


func _on_error(d: Dictionary) -> void:
	EventBus.ui_error.emit(str(d.get("code", "")), int(d.get("reqId", 0)))
