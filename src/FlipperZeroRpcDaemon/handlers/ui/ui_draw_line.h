/**
 * ui_draw_line.h — ui_draw_line RPC handler declaration
 *
 * Command: ui_draw_line  (request/response)
 *
 * Queues a draw-line operation into the canvas session op buffer.
 * The line is rendered at the next ui_flush call.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ui_draw_line","x1":0,"y1":0,"x2":127,"y2":63}
 *
 * Wire format (response – ok):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#pragma once

#include <stdint.h>

void ui_draw_line_handler(uint32_t id, const char* json);
