/**
 * ir_tx_raw.c — ir_tx_raw RPC handler implementation
 *
 * Transmits a raw infrared timing array using the InfraredWorker.
 * Carrier frequency is fixed at 38 kHz with 33% duty cycle.
 * Blocks until the "signal sent" callback fires.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"tm":[9000,4500,560,...]}
 *
 * Wire format (response):
 *   {"id":N,"status":"ok"}
 *
 * Resources: RESOURCE_IR (pre-acquired by the dispatcher)
 */

#include "ir_tx_raw.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <infrared_worker.h>
#include <inttypes.h>

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

    if(!json_extract_uint32_array(json, "tm", ir_raw_timings, &ir_raw_count, IR_RAW_MAX)) {
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
