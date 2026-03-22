/**
 * lfrfid_read_start.c — RPC handler implementation for the "lfrfid_read_start" command
 *
 * Opens a streaming LF RFID tag reader.  An LFRFIDWorker runs on its own
 * FreeRTOS task; its read callback fires on the worker thread and posts a
 * StreamEvent into stream_event_queue (safe: furi_message_queue_put is
 * ISR / worker-thread safe).
 *
 * Stream teardown stops the worker thread and frees the protocol dictionary.
 *
 * Wire format (request):
 *   {"c":37,"i":N}
 *
 * Wire format (response — stream opened):
 *   {"t":0,"i":N,"p":{"s":M}}
 *
 * Wire format (stream events):
 *   {"t":1,"i":M,"p":{"ty":"<protocol_name>","d":"<hex>"}}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"stream_table_full"}  — no free stream slot
 *
 * Resources: RESOURCE_RFID.
 * Thread: handler on main; callback on LFRFIDWorker thread.
 */

#include "lfrfid_read_start.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <lib/lfrfid/lfrfid_worker.h>
#include <lib/lfrfid/protocols/lfrfid_protocols.h>
#include <lib/toolbox/protocols/protocol_dict.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * LFRFIDWorker read callback — fires on the worker thread
 * ctx is (void*)(uintptr_t)stream_id
 * ========================================================= */

static void lfrfid_read_callback(LFRFIDWorkerReadResult result, ProtocolId protocol, void* ctx) {
    if(result != LFRFIDWorkerReadDone) return;

    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    /* Find the slot to access the protocol dict */
    ProtocolDict* dict = NULL;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active && active_streams[i].id == stream_id) {
            dict = active_streams[i].hw.lfrfid.dict;
            break;
        }
    }

    StreamEvent ev;
    ev.stream_id = stream_id;

    if(dict && protocol != PROTOCOL_NO) {
        uint8_t data[16] = {0};
        size_t data_size = protocol_dict_get_data_size(dict, (size_t)protocol);
        if(data_size > sizeof(data)) data_size = sizeof(data);
        protocol_dict_get_data(dict, (size_t)protocol, data, data_size);

        /* Build hex string */
        char hex[64] = {0};
        size_t hex_len = 0;
        for(size_t i = 0; i < data_size && hex_len + 2 < sizeof(hex); i++) {
            snprintf(hex + hex_len, sizeof(hex) - hex_len, "%02X", data[i]);
            hex_len += 2;
        }

        const char* proto_name = protocol_dict_get_name(dict, (size_t)protocol);

        snprintf(
            ev.json_fragment,
            STREAM_FRAG_MAX,
            "\"ty\":\"%s\",\"d\":\"%s\"",
            proto_name ? proto_name : "unknown",
            hex);
    } else {
        snprintf(
            ev.json_fragment, STREAM_FRAG_MAX, "\"ty\":\"%" PRIu32 "\"", (uint32_t)protocol);
    }

    furi_message_queue_put(stream_event_queue, &ev, 0);
}

/* =========================================================
 * Stream teardown — called from main thread on stream_close
 * ========================================================= */

static void lfrfid_teardown(size_t slot_idx) {
    LFRFIDWorker* worker = active_streams[slot_idx].hw.lfrfid.worker;
    ProtocolDict* dict = active_streams[slot_idx].hw.lfrfid.dict;
    if(worker) {
        lfrfid_worker_stop(worker);
        lfrfid_worker_stop_thread(worker);
        lfrfid_worker_free(worker);
        active_streams[slot_idx].hw.lfrfid.worker = NULL;
    }
    if(dict) {
        protocol_dict_free(dict);
        active_streams[slot_idx].hw.lfrfid.dict = NULL;
    }
}

/* =========================================================
 * Command handler
 * ========================================================= */

void lfrfid_read_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    uint32_t stream_id = 0;
    int slot = stream_open(id, "lfrfid_read_start", RESOURCE_RFID, &stream_id);
    if(slot < 0) return;

    ProtocolDict* dict = protocol_dict_alloc(lfrfid_protocols, LFRFIDProtocolMax);
    LFRFIDWorker* worker = lfrfid_worker_alloc(dict);
    lfrfid_worker_start_thread(worker);
    lfrfid_worker_read_start(
        worker, LFRFIDWorkerReadTypeAuto, lfrfid_read_callback, (void*)(uintptr_t)stream_id);

    active_streams[slot].hw.lfrfid.worker = worker;
    active_streams[slot].hw.lfrfid.dict = dict;
    active_streams[slot].teardown = lfrfid_teardown;

    stream_send_opened(id, stream_id, "lfrfid_read_start");
    FURI_LOG_I("RPC", "LFRFID read stream opened id=%" PRIu32, stream_id);
}
