/**
 * ui_screen_release.h — ui_screen_release RPC handler declaration
 *
 * Command: ui_screen_release  (request/response)
 *
 * Releases exclusive control of the Flipper screen (RESOURCE_GUI).
 * The host's secondary ViewPort is removed and the daemon's own status
 * ViewPort is restored.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ui_screen_release"}
 *
 * Wire format (response – ok):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not currently held (nothing to release)
 */

#pragma once

#include <stdint.h>

void ui_screen_release_handler(uint32_t id, const char* json);
