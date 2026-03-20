/**
 * datetime_get.c — datetime_get command handler implementation
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"datetime_get"}
 *   Response: {"type":"response","id":N,"payload":{
 *               "year":<u16>,"month":<u8>,"day":<u8>,
 *               "hour":<u8>,"minute":<u8>,"second":<u8>}}
 *
 * Reads the current RTC date and time via furi_hal_rtc_get_datetime().
 *
 * Resources required: none.
 * Threading: main thread (FuriEventLoop).
 */

#include "datetime_get.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_rtc.h>
#include <stdio.h>
#include <inttypes.h>

void datetime_get_handler(uint32_t id, const char* json) {
    UNUSED(json);

    DateTime dt;
    furi_hal_rtc_get_datetime(&dt);

    char resp[256];
    snprintf(
        resp,
        sizeof(resp),
        "{\"year\":%" PRIu16 ",\"month\":%" PRIu8
        ",\"day\":%" PRIu8 ",\"hour\":%" PRIu8 ",\"minute\":%" PRIu8 ",\"second\":%" PRIu8 "}",
        dt.year,
        dt.month,
        dt.day,
        dt.hour,
        dt.minute,
        dt.second);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " datetime_get -> ok", id);

    rpc_send_data_response(id, resp, log_entry);
}
