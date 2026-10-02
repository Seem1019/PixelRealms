extends GutTest
## HU-097: historial del chat — se puede subir, no salta al final al llegar mensajes y avisa de los nuevos.

var _chat: ChatPanel


func before_each() -> void:
	_chat = ChatPanel.new()
	add_child_autofree(_chat)
	await get_tree().process_frame


func _fill(n: int) -> void:
	for i: int in n:
		_chat.add_message("say", "Bob", "mensaje %d" % i)
	await get_tree().process_frame
	await get_tree().process_frame


func test_keeps_two_hundred_lines() -> void:
	await _fill(ChatPanel.MAX_LINES + 15)
	assert_eq(_chat._lines.size(), ChatPanel.MAX_LINES)
	assert_eq(_chat._log.get_paragraph_count(), ChatPanel.MAX_LINES, "el registro también descarta las viejas")
	assert_string_contains(_chat._lines[0], "mensaje 15")


func test_follows_the_newest_message_while_at_the_bottom() -> void:
	await _fill(30)
	assert_true(_chat.is_at_bottom())
	assert_false(_chat._new_badge.visible)


func test_scrolling_up_shows_older_messages_and_new_ones_do_not_jump() -> void:
	await _fill(30)
	_chat.scroll_page(-1)
	var bar: VScrollBar = _chat._log.get_v_scroll_bar()
	var reading: float = bar.value
	assert_false(_chat.is_at_bottom(), "Re Pág sube en el historial")
	_chat.add_message("say", "Bob", "otro")
	await get_tree().process_frame
	await get_tree().process_frame
	assert_eq(bar.value, reading, "no salta al final mientras lees")
	assert_true(_chat._new_badge.visible, "aviso de mensajes nuevos")
	_chat.scroll_to_end()
	await get_tree().process_frame
	assert_true(_chat.is_at_bottom())
	assert_false(_chat._new_badge.visible)


func test_does_not_fade_while_reading_history() -> void:
	await _fill(30)
	_chat.scroll_page(-1)
	var later := Time.get_ticks_msec() + int((ChatPanel.FADE_DELAY_SEC + ChatPanel.FADE_SEC) * 1000) + 100
	_chat.update_fade(later)
	assert_eq(_chat.modulate.a, 1.0)
