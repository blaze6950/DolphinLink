/**
 * gpio_write.h — gpio_write RPC handler declaration
 *
 * Command: gpio_write
 *
 * Wire format (request):
 *   {"c":13,"i":N,"p":<pin_enum>,"lv":1}
 *     p  — pin enum integer
 *     lv — 1 = drive high, 0 = drive low
 *
 * Wire format (response):
 *   {"t":0,"i":N}
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
