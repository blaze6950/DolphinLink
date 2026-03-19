/**
 * backlight.h — RPC handler declaration for the "backlight" command
 *
 * Sets the LCD backlight brightness to a value in the range 0–255.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"backlight","value":0-255}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "backlight" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void backlight_handler(uint32_t id, const char* json);
