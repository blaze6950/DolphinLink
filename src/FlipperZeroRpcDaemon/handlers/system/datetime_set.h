/**
 * datetime_set.h — datetime_set command handler declaration
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"datetime_set",
 *               "year":<u32>,"month":<u32>,"day":<u32>,
 *               "hour":<u32>,"minute":<u32>,"second":<u32>}
 *   Response: {"id":N,"status":"ok"}
 *   Errors:   missing_datetime_fields
 *
 * Resources required: none.
 */

#pragma once

#include <stdint.h>

/**
 * Handle a "datetime_set" request.
 *
 * @param id   Request ID from the JSON envelope.
 * @param json Full JSON line; must contain year, month, day (all required).
 */
void datetime_set_handler(uint32_t id, const char* json);
