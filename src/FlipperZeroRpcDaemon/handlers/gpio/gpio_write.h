/**
 * gpio_write.h — gpio_write RPC handler declaration
 *
 * Command: gpio_write
 *
 * Wire format (request):
 *   {"id":N,"cmd":"gpio_write","pin":"1","level":true}
 *     pin   — external connector pin label ("1"–"8")
 *     level — true = drive high, false = drive low
 *
 * Wire format (response):
 *   {"id":N,"status":"ok"}
 *
 * Resources: none
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "gpio_write" request.
 *
 * Initialises the named pin as a push-pull output and drives it to the
 * requested level.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void gpio_write_handler(uint32_t id, const char* json);
