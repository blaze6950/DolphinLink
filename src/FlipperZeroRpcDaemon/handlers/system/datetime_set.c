/**
 * datetime_set.c — datetime_set command handler implementation
 *
 * Wire protocol:
 *   Request:  {"c":N,"i":M,
 *               "yr":<u32>,"mo":<u32>,"dy":<u32>,
 *               "hr":<u32>,"mn":<u32>,"sc":<u32>}
 *   Response: {"t":0,"i":N}
 *   Errors:   missing_datetime_fields (year, month, or day == 0)
 *
 * Writes the supplied date/time to the hardware RTC.  The weekday field
 * is set to 1 (Monday placeholder) — it is not used by the RTC driver.
 *
 * Resources required: none.
 * Threading: main thread (FuriEventLoop).
 */

#include "datetime_set.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_rtc.h>

void datetime_set_handler(uint32_t id, const char* json, size_t offset) {
    uint32_t year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;
    JsonValue val;

    if(json_find(json, "yr", offset, &val)) { json_value_uint32(&val, &year);   offset = val.offset; }
    if(json_find(json, "mo", offset, &val)) { json_value_uint32(&val, &month);  offset = val.offset; }
    if(json_find(json, "dy", offset, &val)) { json_value_uint32(&val, &day);    offset = val.offset; }
    if(json_find(json, "hr", offset, &val)) { json_value_uint32(&val, &hour);   offset = val.offset; }
    if(json_find(json, "mn", offset, &val)) { json_value_uint32(&val, &minute); offset = val.offset; }
    if(json_find(json, "sc", offset, &val)) { json_value_uint32(&val, &second); offset = val.offset; }
    (void)offset;

    if(year == 0 || month == 0 || day == 0) {
        rpc_send_error(id, "missing_datetime_fields", "datetime_set");
        return;
    }

    DateTime dt = {
        .year = (uint16_t)year,
        .month = (uint8_t)month,
        .day = (uint8_t)day,
        .hour = (uint8_t)hour,
        .minute = (uint8_t)minute,
        .second = (uint8_t)second,
        .weekday = 1, /* not used by RTC */
    };
    furi_hal_rtc_set_datetime(&dt);

    rpc_send_ok(id, "datetime_set");
}
