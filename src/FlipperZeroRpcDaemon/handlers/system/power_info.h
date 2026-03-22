/**
 * power_info.h — power_info command handler declaration
 *
 * Wire protocol:
 *   Request:  {"c":6,"i":N}
 *   Response: {"t":0,"i":N,"p":{
 *               "ch":<u8>,
 *               "cg":<bool>,
 *               "mv":<i32>,
 *               "ma":<i32>}}
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "power_info" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line (unused — no arguments).
 */
void power_info_handler(uint32_t id, const char* json);
