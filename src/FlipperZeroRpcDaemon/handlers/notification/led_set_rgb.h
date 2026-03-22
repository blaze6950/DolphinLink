/**
 * led_set_rgb.h — RPC handler declaration for the "led_set_rgb" command
 *
 * Sets all three RGB LED channels atomically in a single call.
 *
 * Wire format (request):
 *   {"c":25,"i":N,"r":0-255,"g":0-255,"b":0-255}
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Resources: none (0).
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "led_set_rgb" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void led_set_rgb_handler(uint32_t id, const char* json);
