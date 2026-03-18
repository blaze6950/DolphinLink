/**
 * rpc_handlers_rfid.c — LF RFID RPC handler implementations
 *
 * lfrfid_read_start — streaming LF RFID tag reader
 *
 * The LFRFIDWorker runs on its own FreeRTOS task.  Its read callback fires
 * on that worker thread — furi_message_queue_put is safe there.
 *
 * Stream event payload:
 *   "type":"<protocol_name>","data":"<hex bytes>"
 *
 * JSON protocol:
 *   {"id":N,"cmd":"lfrfid_read_start"}
 */

#include "rpc_handlers_rfid.h"
#include "rpc_response.h"
#include "rpc_stream.h"
#include "rpc_resource.h"
#include "rpc_json.h"
#include "rpc_cmd_log.h"

#include <furi.h>
#include <lib/lfrfid/lfrfid_worker.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * Shared stream helpers (local copies)
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
 * lfrfid_read_start
 * ========================================================= */

/**
 * LFRFIDWorker read callback — fires on the worker thread.
 * ctx is (void*)(uintptr_t)stream_id.
 */
static void lfrfid_read_callback(LFRFIDWorkerReadResult result, ProtocolId protocol, void* ctx) {
    if(result != LFRFIDWorkerReadDone) return;

    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    /* Find the worker to access the key data */
    LFRFIDWorker* worker = NULL;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active && active_streams[i].id == stream_id) {
            worker = active_streams[i].hw.lfrfid.worker;
            break;
        }
    }

    StreamEvent ev;
    ev.stream_id = stream_id;

    if(worker) {
        /* Get raw key data bytes from the worker */
        uint8_t data[16] = {0};
        size_t data_size = lfrfid_worker_read_raw_for_bit_count(worker, data, sizeof(data));

        /* Build hex string */
        char hex[64] = {0};
        size_t hex_len = 0;
        for(size_t i = 0; i < data_size && hex_len + 2 < sizeof(hex); i++) {
            snprintf(hex + hex_len, sizeof(hex) - hex_len, "%02X", data[i]);
            hex_len += 2;
        }

        snprintf(
            ev.json_fragment,
            STREAM_FRAG_MAX,
            "\"type\":\"%" PRIu32 "\",\"data\":\"%s\"",
            (uint32_t)protocol,
            hex);
    } else {
        snprintf(
            ev.json_fragment,
            STREAM_FRAG_MAX,
            "\"type\":\"%" PRIu32 "\"",
            (uint32_t)protocol);
    }

    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void lfrfid_teardown(size_t slot_idx) {
    LFRFIDWorker* worker = active_streams[slot_idx].hw.lfrfid.worker;
    if(worker) {
        lfrfid_worker_stop(worker);
        lfrfid_worker_free(worker);
        active_streams[slot_idx].hw.lfrfid.worker = NULL;
    }
}

void lfrfid_read_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    uint32_t stream_id = 0;
    int slot = stream_open(id, "lfrfid_read_start", RESOURCE_RFID, &stream_id);
    if(slot < 0) return;

    LFRFIDWorker* worker = lfrfid_worker_alloc(NULL);
    lfrfid_worker_start_thread(worker);
    lfrfid_worker_read_start(
        worker, LFRFIDWorkerReadTypeAuto, lfrfid_read_callback, (void*)(uintptr_t)stream_id);

    active_streams[slot].hw.lfrfid.worker = worker;
    active_streams[slot].teardown = lfrfid_teardown;

    stream_send_opened(id, stream_id, "lfrfid_read_start");
    FURI_LOG_I("RPC", "LFRFID read stream opened id=%" PRIu32, stream_id);
}
