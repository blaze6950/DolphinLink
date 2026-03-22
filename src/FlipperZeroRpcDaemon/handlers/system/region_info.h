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

/**
 * Handle a "region_info" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line (unused — no arguments).
 */
void region_info_handler(uint32_t id, const char* json);
