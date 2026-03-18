/**
 * rpc_handlers_ibutton.c — iButton RPC handler implementations
 *
 * ibutton_read_start — streaming iButton key reader
 *
 * The iButtonWorker runs on its own FreeRTOS task.  Its callback fires on
 * that worker thread — furi_message_queue_put is safe there.
 *
 * Stream event payload:
 *   "type":"<protocol_name>","data":"<hex bytes>"
 *
 * JSON protocol:
 *   {"id":N,"cmd":"ibutton_read_start"}
 */

#include "rpc_handlers_ibutton.h"
#include "rpc_response.h"
#include "rpc_stream.h"
#include "rpc_resource.h"
#include "rpc_json.h"
#include "rpc_cmd_log.h"

#include <furi.h>
#include <lib/ibutton/ibutton_worker.h>
#include <lib/ibutton/ibutton_protocols.h>
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
 * ibutton_read_start
 * ========================================================= */

/**
 * iButtonWorker callback — fires on the worker thread.
 * ctx is (void*)(uintptr_t)stream_id.
 */
static void ibutton_worker_callback(void* ctx) {
    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    /* Find the slot to access protocols + key */
    iButtonProtocols* protocols = NULL;
    iButtonKey* key = NULL;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active && active_streams[i].id == stream_id) {
            protocols = active_streams[i].hw.ibutton.protocols;
            key = active_streams[i].hw.ibutton.key;
            break;
        }
    }

    StreamEvent ev;
    ev.stream_id = stream_id;

    if(protocols && key) {
        iButtonProtocolId proto_id = ibutton_key_get_protocol_id(key);
        const char* proto_name = ibutton_protocols_get_name(protocols, proto_id);

        /* Get raw key data */
        const uint8_t* data = ibutton_key_get_data(key);
        size_t data_size = ibutton_key_get_data_size(key);

        char hex[64] = {0};
        size_t hex_len = 0;
        for(size_t i = 0; i < data_size && hex_len + 2 < sizeof(hex); i++) {
            snprintf(hex + hex_len, sizeof(hex) - hex_len, "%02X", data[i]);
            hex_len += 2;
        }

        snprintf(
            ev.json_fragment,
            STREAM_FRAG_MAX,
            "\"type\":\"%s\",\"data\":\"%s\"",
            proto_name ? proto_name : "unknown",
            hex);
    } else {
        snprintf(ev.json_fragment, STREAM_FRAG_MAX, "\"type\":\"unknown\"");
    }

    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void ibutton_teardown(size_t slot_idx) {
    iButtonWorker* worker = active_streams[slot_idx].hw.ibutton.worker;
    iButtonProtocols* protocols = active_streams[slot_idx].hw.ibutton.protocols;
    iButtonKey* key = active_streams[slot_idx].hw.ibutton.key;

    if(worker) {
        ibutton_worker_stop(worker);
        ibutton_worker_stop_thread(worker);
        ibutton_worker_free(worker);
        active_streams[slot_idx].hw.ibutton.worker = NULL;
    }
    if(key) {
        ibutton_key_free(key);
        active_streams[slot_idx].hw.ibutton.key = NULL;
    }
    if(protocols) {
        ibutton_protocols_free(protocols);
        active_streams[slot_idx].hw.ibutton.protocols = NULL;
    }
}

void ibutton_read_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    uint32_t stream_id = 0;
    int slot = stream_open(id, "ibutton_read_start", RESOURCE_IBUTTON, &stream_id);
    if(slot < 0) return;

    iButtonProtocols* protocols = ibutton_protocols_alloc();
    iButtonKey* key = ibutton_key_alloc(ibutton_protocols_get_max_data_size(protocols));
    iButtonWorker* worker = ibutton_worker_alloc(protocols);

    ibutton_worker_start_thread(worker);
    ibutton_worker_read_set_callback(worker, ibutton_worker_callback, (void*)(uintptr_t)stream_id);
    ibutton_worker_read_start(worker, key);

    active_streams[slot].hw.ibutton.worker = worker;
    active_streams[slot].hw.ibutton.protocols = protocols;
    active_streams[slot].hw.ibutton.key = key;
    active_streams[slot].teardown = ibutton_teardown;

    stream_send_opened(id, stream_id, "ibutton_read_start");
    FURI_LOG_I("RPC", "iButton read stream opened id=%" PRIu32, stream_id);
}
