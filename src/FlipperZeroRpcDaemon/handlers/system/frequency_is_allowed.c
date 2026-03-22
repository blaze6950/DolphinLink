/**
 * frequency_is_allowed.c — frequency_is_allowed command handler implementation
 *
 * Wire protocol:
 *   Request:  {"c":N,"i":M,"fr":<u32>}
 *   Response: {"t":0,"i":N,"p":{"al":1|0}}
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

void frequency_is_allowed_handler(uint32_t id, const char* json, size_t offset) {
    uint32_t freq = 0;
    JsonValue val;
    if(!json_find(json, "fr", offset, &val)) {
        rpc_send_error(id, "missing_freq", "frequency_is_allowed");
        return;
    }
    json_value_uint32(&val, &freq);

    bool allowed = furi_hal_region_is_frequency_allowed(freq);

    char resp[32];
    snprintf(resp, sizeof(resp), "{\"al\":%u}", allowed ? 1u : 0u);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " freq_allowed -> %s", id, allowed ? "y" : "n");

    rpc_send_data_response(id, resp, log_entry);
}
