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
 *   {"c":41,"i":N}
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not currently held (nothing to release)
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

void ui_screen_release_handler(uint32_t id, const char* json, size_t offset);
