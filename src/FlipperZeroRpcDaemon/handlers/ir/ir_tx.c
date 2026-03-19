/**
 * ir_tx.c — ir_tx RPC handler implementation
 *
 * Transmits a single decoded infrared frame using the InfraredWorker.
 * Blocks until the worker fires the "message sent" callback.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ir_tx","protocol":"NEC","address":0,"command":0}
 *
 * Wire format (response):
 *   {"id":N,"status":"ok"}
 *
 * Resources: RESOURCE_IR (pre-acquired by the dispatcher)
 */

#include "ir_tx.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <infrared_worker.h>
#include <infrared.h>
#include <inttypes.h>

/** Semaphore signalled by the "message sent" callback. */
static FuriSemaphore* ir_tx_done_sem = NULL;

static void ir_tx_sent_callback(void* ctx) {
    UNUSED(ctx);
    if(ir_tx_done_sem) furi_semaphore_release(ir_tx_done_sem);
}

void ir_tx_handler(uint32_t id, const char* json) {
    char protocol_name[32] = {0};
    uint32_t address = 0, command = 0;

    const char* cursor = json;
    if(!json_extract_string_at(json, &cursor, "protocol", protocol_name, sizeof(protocol_name))) {
        rpc_send_error(id, "missing_protocol", "ir_tx");
        return;
    }
    json_extract_uint32_at(json, &cursor, "address", &address);
    json_extract_uint32_at(json, &cursor, "command", &command);

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

    /* Block until the worker fires the sent callback (max ~500 ms) */
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
