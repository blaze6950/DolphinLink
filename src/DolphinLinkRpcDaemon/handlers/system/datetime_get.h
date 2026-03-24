/**
 * datetime_get.h — datetime_get command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":7,"i":N}
 *   Response: {"t":0,"i":N,"p":{
 *               "yr":<u16>,"mo":<u8>,"dy":<u8>,
 *               "hr":<u8>,"mn":<u8>,"sc":<u8>,"wd":<u8>}}
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "datetime_get" request.
 *
 * @param id     Request ID from the JSON envelope.
 * @param json   Full JSON line (unused — no arguments).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void datetime_get_handler(uint32_t id, const char* json, size_t offset);
