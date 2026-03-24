/**
 * ui_draw_rect.h — ui_draw_rect RPC handler declaration
 *
 * Command: ui_draw_rect  (request/response)
 *
 * Queues a draw-rectangle operation into the canvas session op buffer.
 * The rectangle is rendered at the next ui_flush call.
 *
 * Wire format (request):
 *   {"c":43,"i":N,"x":0,"y":0,"w":128,"h":64,"c":0,"fi":0}
 *
 *   c:  0 = black (default), 1 = white
 *   fi: 1 = filled (canvas_draw_box), 0 = outline (canvas_draw_frame, default)
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

void ui_draw_rect_handler(uint32_t id, const char* json, size_t offset);
