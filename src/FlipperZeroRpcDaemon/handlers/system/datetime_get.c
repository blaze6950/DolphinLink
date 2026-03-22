/**
 * datetime_get.c — datetime_get command handler implementation
 *
 * Wire protocol:
 *   Request:  {"c":N,"i":N}
 *   Response: {"t":0,"i":N,"p":{
 *               "yr":<u16>,"mo":<u8>,"dy":<u8>,
 *               "hr":<u8>,"mn":<u8>,"sc":<u8>,"wd":<u8>}}
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
        "{\"yr\":%" PRIu16 ",\"mo\":%" PRIu8
        ",\"dy\":%" PRIu8 ",\"hr\":%" PRIu8 ",\"mn\":%" PRIu8 ",\"sc\":%" PRIu8 ",\"wd\":%" PRIu8 "}",
        dt.year,
        dt.month,
        dt.day,
        dt.hour,
        dt.minute,
        dt.second,
        dt.weekday);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " datetime_get -> ok", id);

    rpc_send_data_response(id, resp, log_entry);
}
