/**
 * datetime_get.h — datetime_get command handler declaration
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"datetime_get"}
 *   Response: {"t":0,"i":N,"p":{
 *               "year":<u16>,"month":<u8>,"day":<u8>,
 *               "hour":<u8>,"minute":<u8>,"second":<u8>}}
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "datetime_get" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line (unused — no arguments).
 */
void datetime_get_handler(uint32_t id, const char* json);
