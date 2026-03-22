/**
 * gpio_read.h — gpio_read RPC handler declaration
 *
 * Command: gpio_read
 *
 * Wire format (request):
 *   {"c":12,"i":N,"p":<pin_enum>}
 *     p  — pin enum integer (see GpioPin enum)
 *
 * Wire format (response):
 *   {"t":0,"i":N,"p":{"lv":true}}
 *     lv — true = high, false = low
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
