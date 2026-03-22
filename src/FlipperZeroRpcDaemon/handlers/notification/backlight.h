/**
 * backlight.h — RPC handler declaration for the "backlight" command
 *
 * Sets the LCD backlight brightness to a value in the range 0–255.
 *
 * Wire format (request):
 *   {"c":29,"i":N,"lv":0-255}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle the "backlight" RPC command.
 *
 * @param id     Request ID from the JSON message.
 * @param json   Full JSON line (null-terminated) received from the host.
 * @param offset Byte offset past the already-parsed "c" and "i" fields.
 */
void backlight_handler(uint32_t id, const char* json, size_t offset);
