/**
 * frequency_is_allowed.c — frequency_is_allowed command handler implementation
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"frequency_is_allowed","freq":<u32>}
 *   Response: {"id":N,"status":"ok","data":{"allowed":<bool>}}
 *   Errors:   missing_freq
 *
 * Checks whether the given frequency (in Hz) is permitted under the active
 * regional regulatory settings via furi_hal_region_is_frequency_allowed().
 *
 * Resources required: none.
 * Threading: main thread (FuriEventLoop).
 */

#include "frequency_is_allowed.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_region.h>
#include <stdio.h>
#include <inttypes.h>

void frequency_is_allowed_handler(uint32_t id, const char* json) {
    uint32_t freq = 0;
    if(!json_extract_uint32(json, "freq", &freq)) {
        rpc_send_error(id, "missing_freq", "frequency_is_allowed");
        return;
    }

    bool allowed = furi_hal_region_is_frequency_allowed(freq);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"allowed\":%s}}\n",
        id,
        allowed ? "true" : "false");

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " freq_allowed -> %s", id, allowed ? "y" : "n");

    rpc_send_response(resp, log_entry);
}
