extends GutTest
## HU-006: el sobre {t, d} del cliente y la versión de protocolo coinciden con el servidor.


func test_protocol_version() -> void:
	assert_eq(Protocol.VERSION, 1)


func test_envelope_json_shape() -> void:
	var text := JSON.stringify({"t": "Ping", "d": {"clientTime": 123}})
	var parsed: Dictionary = JSON.parse_string(text)
	assert_eq(parsed["t"], "Ping")
	assert_eq(int((parsed["d"] as Dictionary)["clientTime"]), 123)


func test_req_id_increments() -> void:
	var a := Net.next_req_id()
	var b := Net.next_req_id()
	assert_eq(b, a + 1)
