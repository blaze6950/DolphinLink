/**
 * ui_flush.h — ui_flush RPC handler declaration
 *
 * Command: ui_flush  (request/response)
 *
 * Calls view_port_update() on the host's canvas ViewPort, causing the Flipper
 * GUI to invoke the draw callback and replay all buffered draw operations.
 * After flushing the op buffer is cleared.
 *
 * Wire format (request):
 *   {"c":45,"i":N}
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

void ui_flush_handler(uint32_t id, const char* json, size_t offset);
