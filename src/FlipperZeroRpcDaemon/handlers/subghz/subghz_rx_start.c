/**
 * subghz_rx_start.c — subghz_rx_start RPC handler implementation
 *
 * Opens a streaming Sub-GHz receive session.  Raw mark/space pairs are posted
 * to stream_event_queue by the SubGhzWorker pair callback.
 *
 * Wire format (stream event):
 *   {"t":1,"i":M,"p":{"lv":1,"du":9000}}
 *
 * Resources: RESOURCE_SUBGHZ (pre-acquired by the dispatcher)
 */

#include "subghz_rx_start.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_subghz.h>
#include <subghz/subghz_worker.h>
#include <subghz/devices/devices.h>
#include <subghz/devices/cc1101_int/cc1101_int_interconnect.h>
#include <inttypes.h>

static void subghz_rx_callback(void* ctx, bool level, uint32_t duration_us) {
    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    StreamEvent ev;
    ev.stream_id = stream_id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"lv\":%u,\"du\":%" PRIu32,
        level ? 1u : 0u,
        duration_us);
    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void subghz_teardown(size_t slot_idx) {
    SubGhzWorker* worker = active_streams[slot_idx].hw.subghz.worker;
    if(worker) {
        subghz_worker_stop(worker);
        subghz_worker_free(worker);
        active_streams[slot_idx].hw.subghz.worker = NULL;
    }
    furi_hal_subghz_sleep();
}

void subghz_rx_start_handler(uint32_t id, const char* json, size_t offset) {
    uint32_t freq = 433920000;
    JsonValue val;
    if(json_find(json, "fr", offset, &val)) { json_value_uint32(&val, &freq); }
    (void)offset;

    uint32_t stream_id = 0;
    int slot = stream_open(id, "subghz_rx_start", RESOURCE_SUBGHZ, &stream_id);
    if(slot < 0) return;

    furi_hal_subghz_reset();
    furi_hal_subghz_set_frequency_and_path(freq);
    furi_hal_subghz_rx();

    SubGhzWorker* worker = subghz_worker_alloc();
    subghz_worker_set_context(worker, (void*)(uintptr_t)stream_id);
    subghz_worker_set_pair_callback(worker, subghz_rx_callback);
    subghz_worker_start(worker);

    active_streams[slot].hw.subghz.worker = worker;
    active_streams[slot].teardown = subghz_teardown;

    stream_send_opened(id, stream_id, "subghz_rx_start");
    FURI_LOG_I("RPC", "SubGhz RX stream opened freq=%" PRIu32 " id=%" PRIu32, freq, stream_id);
}
