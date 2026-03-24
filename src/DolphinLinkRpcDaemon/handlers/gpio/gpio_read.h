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
#include <stddef.h>

/**
 * Handle a "gpio_read" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line.
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void gpio_read_handler(uint32_t id, const char* json, size_t offset);
