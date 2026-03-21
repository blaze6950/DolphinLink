/**
 * region_info.h — region_info command handler declaration
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"region_info"}
 *   Response: {"t":0,"i":N,"p":{
 *               "region":"<name>",
 *               "bands":[{"start":<u32>,"end":<u32>,"power_limit":<u8>},...]}}
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
