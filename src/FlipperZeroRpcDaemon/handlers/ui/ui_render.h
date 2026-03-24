/**
 * ui_render.h — ui_render RPC handler declaration
 *
 * Command: ui_render  (request/response)
 *
 * Like ui_flush but does NOT clear the pending op buffer after updating.
 * The current pending ops are promoted to the active buffer and the screen is
 * redrawn, but the pending buffer retains all ops so subsequent draw commands
 * append to the existing scene.  This avoids re-sending every shape on each
 * incremental update:
 *
 *   draw_line → render   (2 RTTs, line appears)
 *   draw_rect → render   (2 RTTs, line + rect appear)
 *   draw_str  → render   (2 RTTs, all three appear)
 *
 * Use ui_flush when you want to clear the buffer and start a fresh frame.
 *
 * Wire format (request):
 *   {"c":46,"i":N}
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

void ui_render_handler(uint32_t id, const char* json, size_t offset);
