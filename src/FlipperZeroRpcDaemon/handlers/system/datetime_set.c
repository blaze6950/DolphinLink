/**
 * datetime_set.c — datetime_set command handler implementation
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"datetime_set",
 *               "year":<u32>,"month":<u32>,"day":<u32>,
 *               "hour":<u32>,"minute":<u32>,"second":<u32>}
 *   Response: {"id":N,"status":"ok"}
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

void datetime_set_handler(uint32_t id, const char* json) {
    uint32_t year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;

    json_extract_uint32(json, "year", &year);
    json_extract_uint32(json, "month", &month);
    json_extract_uint32(json, "day", &day);
    json_extract_uint32(json, "hour", &hour);
    json_extract_uint32(json, "minute", &minute);
    json_extract_uint32(json, "second", &second);

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
