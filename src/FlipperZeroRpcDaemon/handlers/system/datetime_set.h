/**
 * datetime_set.h — datetime_set command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":8,"i":N,
 *               "yr":<u32>,"mo":<u32>,"dy":<u32>,
 *               "hr":<u32>,"mn":<u32>,"sc":<u32>,"wd":<u32>}
 *   Response: {"t":0,"i":N}
 *   Errors:   missing_datetime_fields
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "datetime_set" request.
 *
 * @param id     Request ID from the JSON envelope.
 * @param json   Full JSON line; must contain year, month, day (all required).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void datetime_set_handler(uint32_t id, const char* json, size_t offset);
