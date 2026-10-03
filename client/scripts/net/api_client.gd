class_name ApiClient
extends Node
## Cliente REST (HU-010/011/012/013/014) con HTTPRequest y `await`. Devuelve ApiResult; nunca lanza.
## URL base desde Settings.server_url(). El JWT se guarda aquí (en memoria) tras login.


class ApiResult:
	var status: int = 0
	var data: Variant = null
	var error_code: String = ""
	var message: String = ""
	var field_errors: Array = []

	func ok() -> bool:
		return status >= 200 and status < 300


## El JWT dura 15 min y una partida dura más: se renueva antes (HU-015 CA1) para volver a la selección de personaje o pedir
## un ticket de reconexión (HU-025) sin pedir la contraseña.
const REFRESH_EVERY_SEC := 600.0
## Si la renovación falla por la red se reintenta pronto: esperar otros 10 min dejaría caducar el token.
const REFRESH_RETRY_SEC := 45.0

var token: String = ""
var token_expires_at: String = ""

var _base_url: String = ""
var _refresh_timer: Timer
## Sube en cada login y cierre de sesión: una renovación que vuelve tarde de una sesión ya cerrada no la resucita.
var _session: int = 0


func _ready() -> void:
	_base_url = Settings.server_url()
	_refresh_timer = Timer.new()
	_refresh_timer.wait_time = REFRESH_EVERY_SEC
	_refresh_timer.timeout.connect(refresh_token)
	add_child(_refresh_timer)


func set_base_url(url: String) -> void:
	_base_url = url


func has_token() -> bool:
	return not token.is_empty()


func clear_token() -> void:
	token = ""
	token_expires_at = ""
	_session += 1
	if _refresh_timer != null:
		_refresh_timer.stop()


func is_refreshing_scheduled() -> bool:
	return _refresh_timer != null and not _refresh_timer.is_stopped()


func _set_token(new_token: String, expires_at: String) -> void:
	token = new_token
	token_expires_at = expires_at
	if _refresh_timer != null and not token.is_empty():
		_refresh_timer.start(REFRESH_EVERY_SEC)


## POST /api/auth/refresh → JWT nuevo con la sesión actual. Si ya no vale (401), _request limpia el token.
func refresh_token() -> ApiResult:
	var session := _session
	var r := await _request(HTTPClient.METHOD_POST, "/api/auth/refresh", null, true)
	if session != _session:
		return r  # se cerró sesión (o se volvió a entrar) mientras tanto: esta respuesta ya no vale
	if r.ok() and r.data is Dictionary:
		_set_token(str((r.data as Dictionary).get("token", "")), str((r.data as Dictionary).get("expiresAt", "")))
	elif r.error_code == "network" and has_token():
		_refresh_timer.start(REFRESH_RETRY_SEC)
	return r


## POST /api/auth/register
func register(username: String, password: String) -> ApiResult:
	return await _request(HTTPClient.METHOD_POST, "/api/auth/register", {"username": username, "password": password}, false)


## POST /api/auth/login → guarda el JWT.
func login(username: String, password: String) -> ApiResult:
	var r := await _request(HTTPClient.METHOD_POST, "/api/auth/login", {"username": username, "password": password}, false)
	if r.ok() and r.data is Dictionary:
		_session += 1
		_set_token(str((r.data as Dictionary).get("token", "")), str((r.data as Dictionary).get("expiresAt", "")))
	return r


## GET /api/characters
func list_characters() -> ApiResult:
	return await _request(HTTPClient.METHOD_GET, "/api/characters", null, true)


## POST /api/characters
func create_character(character_name: String, class_id: String) -> ApiResult:
	return await _request(HTTPClient.METHOD_POST, "/api/characters", {"name": character_name, "classId": class_id}, true)


## DELETE /api/characters/{id}
func delete_character(character_id: String) -> ApiResult:
	return await _request(HTTPClient.METHOD_DELETE, "/api/characters/%s" % character_id, null, true)


## POST /api/game/ticket
func game_ticket(character_id: String) -> ApiResult:
	return await _request(HTTPClient.METHOD_POST, "/api/game/ticket", {"characterId": character_id}, true)


func _request(method: HTTPClient.Method, path: String, body: Variant, auth: bool) -> ApiResult:
	var result := ApiResult.new()
	var http := HTTPRequest.new()
	add_child(http)
	var headers: PackedStringArray = ["Content-Type: application/json", "Accept: application/json"]
	if auth:
		headers.append("Authorization: Bearer %s" % token)
	var payload := "" if body == null else JSON.stringify(body)
	var err := http.request(_base_url + path, headers, method, payload)
	if err != OK:
		http.queue_free()
		result.error_code = "network"
		result.message = "No se pudo conectar con el servidor"
		return result
	var response: Array = await http.request_completed
	http.queue_free()
	var http_result: int = response[0]
	result.status = int(response[1])
	if http_result != HTTPRequest.RESULT_SUCCESS:
		result.error_code = "network"
		result.message = "No se pudo conectar con el servidor"
		return result
	var text: String = (response[3] as PackedByteArray).get_string_from_utf8()
	if not text.is_empty():
		result.data = JSON.parse_string(text)
	if not result.ok():
		_fill_error(result)
	if result.status == 401 and auth:
		clear_token()
		EventBus.ui_error.emit("session_expired", 0)
	return result


func _fill_error(result: ApiResult) -> void:
	if result.data is Dictionary:
		var d: Dictionary = result.data
		result.error_code = str(d.get("code", ""))
		result.message = str(d.get("message", ""))
		if d.has("errors"):
			result.field_errors = d["errors"]
			if result.error_code.is_empty():
				result.error_code = "validation"
	if result.error_code.is_empty():
		result.error_code = "http_%d" % result.status
	if result.message.is_empty():
		result.message = ApiMessages.text_for(result.error_code)
