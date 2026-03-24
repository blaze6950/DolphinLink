/**
 * frequency_is_allowed.h — frequency_is_allowed command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":10,"i":N,"fr":<u32>}
 *   Response: {"t":0,"i":N,"p":{"al":<bool>}}
 *   Errors:   missing_freq
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "frequency_is_allowed" request.
 *
 * @param id     Request ID from the JSON envelope.
 * @param json   Full JSON line; must contain "fr" (Hz, uint32).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void frequency_is_allowed_handler(uint32_t id, const char* json, size_t offset);
