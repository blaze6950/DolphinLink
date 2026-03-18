/**
 * rpc_handlers.c — RPC command handler implementations
 *
 * Each handler follows a consistent pattern:
 *   1. Validate arguments (if any).
 *   2. Perform the operation.
 *   3. Send a response via rpc_response helpers.
 *
 * The response helpers (rpc_send_error / rpc_send_ok / rpc_send_response)
 * eliminate the boilerplate snprintf+cdc_send+cmd_log_push triples that
 * would otherwise be repeated in every handler.
 */

#include "rpc_handlers.h"
#include "rpc_response.h"
#include "rpc_stream.h"
#include "rpc_resource.h"
#include "rpc_json.h"
#include "rpc_cmd_log.h"

#include <furi.h>
#include <stdio.h>
#include <inttypes.h>

/* -------------------------------------------------------------------------
 * ping
 * ------------------------------------------------------------------------- */

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

/* -------------------------------------------------------------------------
 * ble_scan_start
 * ------------------------------------------------------------------------- */

void ble_scan_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    /* Find a free slot BEFORE acquiring resources */
    int slot = stream_alloc_slot();
    if(slot < 0) {
        rpc_send_error(id, "stream_table_full", "ble_scan_start");
        return;
    }

    /* Acquire resources (dispatcher already confirmed they are free) */
    resource_acquire(RESOURCE_BLE);

    uint32_t stream_id = next_stream_id++;
    active_streams[slot].id = stream_id;
    active_streams[slot].resources = RESOURCE_BLE;
    active_streams[slot].active = true;

    char resp[128];
    snprintf(resp, sizeof(resp), "{\"id\":%" PRIu32 ",\"stream\":%" PRIu32 "}\n", id, stream_id);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " ble_scan_start -> s:%" PRIu32, id, stream_id);

    rpc_send_response(resp, log_entry);

    FURI_LOG_I("RPC", "BLE scan stream opened id=%" PRIu32, stream_id);
    /*
     * In a real implementation, start BLE scanning here and emit events
     * like: {"event":{"addr":"AA:BB:CC:DD:EE:FF","rssi":-70},"stream":<id>}\n
     * for each discovered device.
     */
}

/* -------------------------------------------------------------------------
 * stream_close
 * ------------------------------------------------------------------------- */

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
