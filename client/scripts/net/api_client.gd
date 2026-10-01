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


var token: String = ""
var token_expires_at: String = ""

var _base_url: String = ""


func _ready() -> void:
	_base_url = Settings.server_url()


func set_base_url(url: String) -> void:
	_base_url = url


func has_token() -> bool:
	return not token.is_empty()


func clear_token() -> void:
	token = ""
	token_expires_at = ""


## POST /api/auth/register
func register(username: String, password: String) -> ApiResult:
	return await _request(HTTPClient.METHOD_POST, "/api/auth/register", {"username": username, "password": password}, false)


## POST /api/auth/login → guarda el JWT.
func login(username: String, password: String) -> ApiResult:
	var r := await _request(HTTPClient.METHOD_POST, "/api/auth/login", {"username": username, "password": password}, false)
	if r.ok() and r.data is Dictionary:
		token = str((r.data as Dictionary).get("token", ""))
		token_expires_at = str((r.data as Dictionary).get("expiresAt", ""))
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
