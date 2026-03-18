/**
 * rpc_handlers.c — ping and stream_close handler implementations
 *
 * All other handlers have been migrated to subsystem-specific files.
 * See rpc_handlers.h for the full list.
 */

#include "rpc_handlers.h"
#include "../core/rpc_response.h"
#include "../core/rpc_stream.h"
#include "../core/rpc_json.h"
#include "../core/rpc_cmd_log.h"

#include <furi.h>
#include <stdio.h>
#include <inttypes.h>

/* =========================================================
 * ping
 * ========================================================= */

void ping_handler(uint32_t id, const char* json) {
    UNUSED(json);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"pong\":true}}\n",
        id);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " ping -> ok", id);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * stream_close
 * ========================================================= */

void stream_close_handler(uint32_t id, const char* json) {
    uint32_t stream_id = 0;
    if(!json_extract_uint32(json, "stream", &stream_id)) {
        rpc_send_error(id, "missing_stream_id", "stream_close");
        return;
    }

    int slot = stream_find_by_id(stream_id);
    if(slot < 0) {
        rpc_send_error(id, "stream_not_found", "stream_close");
        return;
    }

    stream_close_by_index((size_t)slot);
    FURI_LOG_I("RPC", "stream %" PRIu32 " closed", stream_id);

    rpc_send_ok(id, "stream_close");
}
