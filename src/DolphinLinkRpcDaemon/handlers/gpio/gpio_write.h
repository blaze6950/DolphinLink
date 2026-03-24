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
#include <stddef.h>

/**
 * Handle a "gpio_write" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line.
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void gpio_write_handler(uint32_t id, const char* json, size_t offset);
