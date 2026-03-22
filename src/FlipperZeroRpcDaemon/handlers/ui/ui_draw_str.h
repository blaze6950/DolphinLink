/**
 * ui_draw_str.h — ui_draw_str RPC handler declaration
 *
 * Command: ui_draw_str  (request/response)
 *
 * Queues a draw-string operation into the canvas session op buffer.
 * The text is rendered at the next ui_flush call.
 *
 * Wire format (request):
 *   {"c":42,"i":N,"x":10,"y":20,"tx":"Hello","fn":1,"c":1}
 *
 *   fn: 0 = FontPrimary, 1 = FontSecondary (default), 2 = FontBigNumbers
 *   c:  0 = black (default), 1 = white
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 *   missing_text  — "text" field absent or empty
 */

#pragma once

#include <stdint.h>

void ui_draw_str_handler(uint32_t id, const char* json);
