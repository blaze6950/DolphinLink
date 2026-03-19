/**
 * power_info.h — power_info command handler declaration
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"power_info"}
 *   Response: {"id":N,"status":"ok","data":{
 *               "charge":<u8>,
 *               "charging":<bool>,
 *               "voltage_mv":<i32>,
 *               "current_ma":<i32>}}
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
