/**
 * ui_draw_str.h — ui_draw_str RPC handler declaration
 *
 * Command: ui_draw_str  (request/response)
 *
 * Queues a draw-string operation into the canvas session op buffer.
 * The text is rendered at the next ui_flush call.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ui_draw_str","x":10,"y":20,"text":"Hello","font":1}
 *
 *   font: 0 = FontPrimary, 1 = FontSecondary (default), 2 = FontBigNumbers
 *
 * Wire format (response – ok):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 *   missing_text  — "text" field absent or empty
 */

#pragma once

#include <stdint.h>

void ui_draw_str_handler(uint32_t id, const char* json);
