/**
 * led_set.h — RPC handler declaration for the "led_set" command
 *
 * Sets a single RGB LED channel (red, green, or blue) to an intensity value
 * in the range 0–255.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"led_set","color":"red"|"green"|"blue","value":0-255}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_color"}   — "color" field absent or unrecognised
 *   {"id":N,"error":"invalid_color"}   — color string is not red/green/blue
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
