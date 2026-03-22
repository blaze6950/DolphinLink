/**
 * ibutton_read_start.c — RPC handler implementation for the "ibutton_read_start" command
 *
 * Opens a streaming iButton key reader.  An iButtonWorker runs on its own
 * FreeRTOS task; its read callback fires on the worker thread and posts a
 * StreamEvent into stream_event_queue (safe: furi_message_queue_put is
 * worker-thread safe).
 *
 * Stream teardown stops the worker thread and frees protocol/key objects.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ibutton_read_start"}
 *
 * Wire format (response — stream opened):
 *   {"id":N,"stream":M}
 *
 * Wire format (stream events):
 *   {"t":1,"i":M,"p":{"ty":"<protocol_name>","d":"<hex>"}}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"stream_table_full"}  — no free stream slot
 *
 * Resources: RESOURCE_IBUTTON.
 * Thread: handler on main; callback on iButtonWorker thread.
 */

#include "ibutton_read_start.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <lib/ibutton/ibutton_worker.h>
#include <lib/ibutton/ibutton_protocols.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * iButtonWorker callback — fires on the worker thread
 * ctx is (void*)(uintptr_t)stream_id
 * ========================================================= */

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

        /* Get raw key data via editable data pointer */
        iButtonEditableData editable = {0};
        ibutton_protocols_get_editable_data(protocols, key, &editable);

        char hex[64] = {0};
        size_t hex_len = 0;
        if(editable.ptr && editable.size) {
            for(size_t i = 0; i < editable.size && hex_len + 2 < sizeof(hex); i++) {
                snprintf(hex + hex_len, sizeof(hex) - hex_len, "%02X", editable.ptr[i]);
                hex_len += 2;
            }
        }

        snprintf(
            ev.json_fragment,
            STREAM_FRAG_MAX,
            "\"ty\":\"%s\",\"d\":\"%s\"",
            proto_name ? proto_name : "unknown",
            hex);
    } else {
        snprintf(ev.json_fragment, STREAM_FRAG_MAX, "\"ty\":\"unknown\"");
    }

    furi_message_queue_put(stream_event_queue, &ev, 0);
}

/* =========================================================
 * Stream teardown — called from main thread on stream_close
 * ========================================================= */

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

/* =========================================================
 * Command handler
 * ========================================================= */

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
