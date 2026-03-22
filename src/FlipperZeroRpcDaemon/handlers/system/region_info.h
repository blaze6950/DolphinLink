/**
 * region_info.h — region_info command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":9,"i":N}
 *   Response: {"t":0,"i":N,"p":{"rg":"<name>"}}
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle a "region_info" request.
 *
 * @param id     Request ID from the JSON envelope.
 * @param json   Full JSON line (unused — no arguments).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void region_info_handler(uint32_t id, const char* json, size_t offset);
