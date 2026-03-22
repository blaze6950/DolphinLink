/**
 * led_set.h — RPC handler declaration for the "led_set" command
 *
 * Sets a single RGB LED channel (red, green, or blue) to an intensity value
 * in the range 0–255.
 *
 * Wire format (request):
 *   {"c":24,"i":N,"l":<led_enum>,"r":0-255,"g":0-255,"b":0-255}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_color"}   — "l" field absent or unrecognised
 *   {"t":0,"i":N,"e":"invalid_color"}   — led enum value not recognised
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "led_set" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void led_set_handler(uint32_t id, const char* json);
