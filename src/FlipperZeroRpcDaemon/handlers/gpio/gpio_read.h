/**
 * gpio_read.h — gpio_read RPC handler declaration
 *
 * Command: gpio_read
 *
 * Wire format (request):
 *   {"id":N,"cmd":"gpio_read","pin":"1"}
 *     pin  — external connector pin label ("1"–"8")
 *
 * Wire format (response):
 *   {"id":N,"status":"ok","data":{"level":true}}
 *     level — true = high, false = low
 *
 * Resources: none
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "gpio_read" request.
 *
 * Initialises the named pin as a pull-up input and reads its digital level.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line.
 */
void gpio_read_handler(uint32_t id, const char* json);
