/**
 * led_set_rgb.h — RPC handler declaration for the "led_set_rgb" command
 *
 * Sets all three RGB LED channels atomically in a single call.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"led_set_rgb","red":0-255,"green":0-255,"blue":0-255}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
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
