/**
 * gpio_set_5v.h — gpio_set_5v RPC handler declaration
 *
 * Command: gpio_set_5v
 *
 * Wire format (request):
 *   {"c":15,"i":N,"en":1}
 *     en — 1 = enable the 5 V OTG rail, 0 = disable it
 *
 * Wire format (response):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   missing_enable — "enable" field absent from request
 *
 * Resources: none
 *
 * Note: enabling OTG when an external 5 V source is already present can
 * cause hardware damage.  The daemon does not check for this condition.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "gpio_set_5v" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line.
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void gpio_set_5v_handler(uint32_t id, const char* json, size_t offset);
