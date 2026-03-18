/**
 * rpc_handlers_nfc.c — NFC RPC handler implementations
 *
 * nfc_scan_start — streaming NFC protocol scanner (migrated from rpc_handlers.c)
 *
 * The NFC scanner callback fires on the NFC worker thread.
 * furi_message_queue_put is safe to call there.
 */

#include "rpc_handlers_nfc.h"
#include "../core/rpc_response.h"
#include "../core/rpc_stream.h"
#include "../core/rpc_resource.h"
#include "../core/rpc_json.h"
#include "../core/rpc_cmd_log.h"

#include <furi.h>
#include <nfc/nfc.h>
#include <nfc/nfc_scanner.h>
#include <nfc/nfc_device.h>
#include <stdio.h>
#include <inttypes.h>

/* =========================================================
 * Shared stream helpers (local copies — same pattern as other handlers)
 * ========================================================= */

static int
    stream_open(uint32_t id, const char* cmd_name, ResourceMask res, uint32_t* stream_id_out) {
    int slot = stream_alloc_slot();
    if(slot < 0) {
        rpc_send_error(id, "stream_table_full", cmd_name);
        return -1;
    }
    resource_acquire(res);
    uint32_t stream_id = next_stream_id++;
    active_streams[slot].id = stream_id;
    active_streams[slot].resources = res;
    active_streams[slot].active = true;
    active_streams[slot].teardown = NULL;
    *stream_id_out = stream_id;
    return slot;
}

static void stream_send_opened(uint32_t request_id, uint32_t stream_id, const char* cmd_name) {
    char resp[128];
    snprintf(
        resp, sizeof(resp), "{\"id\":%" PRIu32 ",\"stream\":%" PRIu32 "}\n", request_id, stream_id);
    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry,
        sizeof(log_entry),
        "#%" PRIu32 " %.14s->s:%" PRIu32,
        request_id,
        cmd_name,
        stream_id);
    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * nfc_scan_start
 * ========================================================= */

static void nfc_scanner_callback(NfcScannerEvent event, void* ctx) {
    if(event.type != NfcScannerEventTypeDetected) return;
    if(event.data.protocol_num == 0) return;

    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    StreamEvent ev;
    ev.stream_id = stream_id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"protocol\":\"%s\"",
        nfc_device_get_protocol_name(event.data.protocols[0]));
    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void nfc_teardown(size_t slot_idx) {
    NfcScanner* scanner = active_streams[slot_idx].hw.nfc.scanner;
    Nfc* nfc = active_streams[slot_idx].hw.nfc.nfc;
    if(scanner) {
        nfc_scanner_stop(scanner);
        nfc_scanner_free(scanner);
        active_streams[slot_idx].hw.nfc.scanner = NULL;
    }
    if(nfc) {
        nfc_free(nfc);
        active_streams[slot_idx].hw.nfc.nfc = NULL;
    }
}

void nfc_scan_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    uint32_t stream_id = 0;
    int slot = stream_open(id, "nfc_scan_start", RESOURCE_NFC, &stream_id);
    if(slot < 0) return;

    Nfc* nfc = nfc_alloc();
    NfcScanner* scanner = nfc_scanner_alloc(nfc);
    nfc_scanner_start(scanner, nfc_scanner_callback, (void*)(uintptr_t)stream_id);

    active_streams[slot].hw.nfc.nfc = nfc;
    active_streams[slot].hw.nfc.scanner = scanner;
    active_streams[slot].teardown = nfc_teardown;

    stream_send_opened(id, stream_id, "nfc_scan_start");
    FURI_LOG_I("RPC", "NFC scan stream opened id=%" PRIu32, stream_id);
}
