/**
 * ui_screen_acquire.h — ui_screen_acquire RPC handler declaration
 *
 * Command: ui_screen_acquire  (request/response)
 *
 * Claims exclusive control of the Flipper screen (RESOURCE_GUI).
 * The daemon's own status ViewPort is hidden; the host receives a secondary
 * ViewPort that it can paint via ui_draw_* + ui_flush.
 *
 * Wire format (request):
 *   {"c":40,"i":N}
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — another client already holds RESOURCE_GUI
 */

#pragma once

#include <stdint.h>

void ui_screen_acquire_handler(uint32_t id, const char* json);
