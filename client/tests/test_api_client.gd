extends GutTest
## HU-015 CA1: el JWT (15 min) se renueva solo mientras hay sesión, para volver a la selección sin pedir la contraseña.


func test_refresh_is_scheduled_before_the_token_expires() -> void:
	assert_lt(ApiClient.REFRESH_EVERY_SEC, 15.0 * 60.0, "renovar antes de los 15 min de vida del JWT")


func test_login_schedules_the_refresh_and_logout_stops_it() -> void:
	var api: ApiClient = add_child_autofree(ApiClient.new())
	assert_false(api.is_refreshing_scheduled(), "sin sesión no se renueva nada")
	api._set_token("jwt-de-prueba", "2026-10-02T12:15:00Z")
	assert_true(api.is_refreshing_scheduled())
	assert_eq(api.token, "jwt-de-prueba")
	api.clear_token()
	assert_false(api.is_refreshing_scheduled())
	assert_false(api.has_token())


func test_an_empty_token_does_not_schedule_a_refresh() -> void:
	var api: ApiClient = add_child_autofree(ApiClient.new())
	api._set_token("", "")
	assert_false(api.is_refreshing_scheduled())
