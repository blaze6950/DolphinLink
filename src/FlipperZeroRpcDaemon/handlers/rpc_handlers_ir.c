/**
 * rpc_handlers_ir.c — Infrared RPC handler implementations
 *
 * ir_tx         — one-shot decoded IR TX (protocol name + address + command)
 * ir_tx_raw     — one-shot raw IR TX (timing array in microseconds)
 * ir_receive_start — streaming IR RX (migrated from rpc_handlers.c)
 *
 * TX handlers block until the transmission is complete using a semaphore
 * signalled by the InfraredWorker "message sent" callback.  Max blocking
 * time is bounded by the payload length; for a typical NEC frame this is
 * under 70 ms.
 *
 * Max IR_RAW_MAX timing pairs: limited to 512 to stay within the 8 KB stack.
 */

#include "rpc_handlers_ir.h"
#include "../core/rpc_response.h"
#include "../core/rpc_stream.h"
#include "../core/rpc_resource.h"
#include "../core/rpc_json.h"
#include "../core/rpc_cmd_log.h"

#include <furi.h>
#include <infrared_worker.h>
#include <infrared.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * Shared stream helpers (local copies — same pattern as gpio)
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
 * ir_tx — decoded signal TX
 * ========================================================= */

/** Semaphore signalled by the "message sent" callback so the main thread
 *  can block cheaply until TX is done. */
static FuriSemaphore* ir_tx_done_sem = NULL;

static void ir_tx_sent_callback(void* ctx) {
    UNUSED(ctx);
    if(ir_tx_done_sem) furi_semaphore_release(ir_tx_done_sem);
}

void ir_tx_handler(uint32_t id, const char* json) {
    char protocol_name[32] = {0};
    uint32_t address = 0, command = 0;

    if(!json_extract_string(json, "protocol", protocol_name, sizeof(protocol_name))) {
        rpc_send_error(id, "missing_protocol", "ir_tx");
        return;
    }
    json_extract_uint32(json, "address", &address);
    json_extract_uint32(json, "command", &command);

    InfraredProtocol protocol = infrared_get_protocol_by_name(protocol_name);
    if(protocol == InfraredProtocolUnknown) {
        rpc_send_error(id, "unknown_protocol", "ir_tx");
        return;
    }

    InfraredMessage msg = {
        .protocol = protocol,
        .address = address,
        .command = command,
        .repeat = false,
    };

    ir_tx_done_sem = furi_semaphore_alloc(1, 0);

    InfraredWorker* worker = infrared_worker_alloc();
    infrared_worker_set_decoded_signal(worker, &msg);
    infrared_worker_tx_set_get_signal_callback(
        worker, infrared_worker_tx_get_signal_steady_callback, NULL);
    infrared_worker_tx_set_signal_sent_callback(worker, ir_tx_sent_callback, NULL);
    infrared_worker_tx_start(worker);

    /* Block until the worker fires the sent callback (max ~200 ms) */
    furi_semaphore_acquire(ir_tx_done_sem, 500);

    infrared_worker_tx_stop(worker);
    infrared_worker_free(worker);
    furi_semaphore_free(ir_tx_done_sem);
    ir_tx_done_sem = NULL;

    rpc_send_ok(id, "ir_tx");
    FURI_LOG_I(
        "RPC",
        "IR TX done protocol=%s addr=%" PRIu32 " cmd=%" PRIu32,
        protocol_name,
        address,
        command);
}

/* =========================================================
 * ir_tx_raw — raw timing array TX
 * ========================================================= */

#define IR_RAW_MAX 512

static uint32_t ir_raw_timings[IR_RAW_MAX];
static size_t ir_raw_count = 0;
static size_t ir_raw_pos = 0;

static InfraredWorkerGetSignalResponse
    ir_raw_get_signal_callback(void* ctx, InfraredWorker* instance) {
    UNUSED(ctx);
    if(ir_raw_pos >= ir_raw_count) return InfraredWorkerGetSignalResponseStop;

    /* Feed all remaining timings as a single raw burst */
    infrared_worker_set_raw_signal(
        instance, ir_raw_timings + ir_raw_pos, ir_raw_count - ir_raw_pos, 38000, 0.33f);
    ir_raw_pos = ir_raw_count; /* consumed */
    return InfraredWorkerGetSignalResponseNew;
}

static FuriSemaphore* ir_raw_tx_done_sem = NULL;

static void ir_raw_tx_sent_callback(void* ctx) {
    UNUSED(ctx);
    if(ir_raw_tx_done_sem) furi_semaphore_release(ir_raw_tx_done_sem);
}

void ir_tx_raw_handler(uint32_t id, const char* json) {
    ir_raw_count = 0;
    ir_raw_pos = 0;

    if(!json_extract_uint32_array(json, "timings", ir_raw_timings, &ir_raw_count, IR_RAW_MAX)) {
        rpc_send_error(id, "missing_timings", "ir_tx_raw");
        return;
    }

    ir_raw_tx_done_sem = furi_semaphore_alloc(1, 0);

    InfraredWorker* worker = infrared_worker_alloc();
    infrared_worker_tx_set_get_signal_callback(worker, ir_raw_get_signal_callback, NULL);
    infrared_worker_tx_set_signal_sent_callback(worker, ir_raw_tx_sent_callback, NULL);
    infrared_worker_tx_start(worker);

    /* Max timeout: 512 pairs × ~1 ms each = ~512 ms */
    furi_semaphore_acquire(ir_raw_tx_done_sem, 1000);

    infrared_worker_tx_stop(worker);
    infrared_worker_free(worker);
    furi_semaphore_free(ir_raw_tx_done_sem);
    ir_raw_tx_done_sem = NULL;

    rpc_send_ok(id, "ir_tx_raw");
    FURI_LOG_I("RPC", "IR raw TX done count=%zu", ir_raw_count);
}

/* =========================================================
 * ir_receive_start (stream)
 * ========================================================= */

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
