/**
 * ir_receive_start.c — ir_receive_start RPC handler implementation
 *
 * Opens a streaming IR receive session.  Decoded frames are posted to
 * stream_event_queue by the InfraredWorker receive callback.
 *
 * Wire format (stream event):
 *   {"event":{"protocol":"NEC","address":0,"command":0,"repeat":false},"stream":M}
 *
 * Resources: RESOURCE_IR (pre-acquired by the dispatcher)
 */

#include "ir_receive_start.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <infrared_worker.h>
#include <infrared.h>
#include <inttypes.h>

static void ir_rx_callback(void* ctx, InfraredWorkerSignal* signal) {
    if(!infrared_worker_signal_is_decoded(signal)) return;

    const InfraredMessage* msg = infrared_worker_get_decoded_signal(signal);

    uint32_t stream_id = 0;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active && active_streams[i].hw.ir.worker == ctx) {
            stream_id = active_streams[i].id;
            break;
        }
    }
    if(stream_id == 0) return;

    StreamEvent ev;
    ev.stream_id = stream_id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"protocol\":\"%s\",\"address\":%" PRIu32 ",\"command\":%" PRIu32 ",\"repeat\":%s",
        infrared_get_protocol_name(msg->protocol),
        (uint32_t)msg->address,
        (uint32_t)msg->command,
        msg->repeat ? "true" : "false");
    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void ir_teardown(size_t slot_idx) {
    InfraredWorker* worker = active_streams[slot_idx].hw.ir.worker;
    if(worker) {
        infrared_worker_rx_stop(worker);
        infrared_worker_free(worker);
        active_streams[slot_idx].hw.ir.worker = NULL;
    }
}

void ir_receive_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    uint32_t stream_id = 0;
    int slot = stream_open(id, "ir_receive_start", RESOURCE_IR, &stream_id);
    if(slot < 0) return;

    InfraredWorker* worker = infrared_worker_alloc();
    infrared_worker_rx_set_received_signal_callback(worker, ir_rx_callback, worker);
    infrared_worker_rx_start(worker);

    active_streams[slot].hw.ir.worker = worker;
    active_streams[slot].teardown = ir_teardown;

    stream_send_opened(id, stream_id, "ir_receive_start");
    FURI_LOG_I("RPC", "IR receive stream opened id=%" PRIu32, stream_id);
}
