/**
 * rpc_handlers_subghz.c — Sub-GHz RPC handler implementations
 *
 * subghz_tx         — one-shot raw OOK TX via async TX API
 * subghz_get_rssi   — brief RX window to sample RSSI, then sleep
 * subghz_rx_start   — streaming raw pair events (migrated from rpc_handlers.c)
 *
 * For subghz_tx the timing array is emitted as LevelDuration pairs from a
 * yielding callback.  The CC1101 is returned to sleep after transmission.
 *
 * For subghz_get_rssi the radio is powered up, tuned to freq, placed in RX
 * for ~5 ms, RSSI is sampled, then the radio is put back to sleep.
 * The full operation completes on the main thread with a furi_delay_ms().
 * RESOURCE_SUBGHZ is acquired only for the duration of the call.
 */

#include "rpc_handlers_subghz.h"
#include "rpc_response.h"
#include "rpc_stream.h"
#include "rpc_resource.h"
#include "rpc_json.h"
#include "rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_subghz.h>
#include <subghz/subghz_worker.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * Shared stream helpers
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
 * subghz_tx — one-shot raw OOK TX
 * ========================================================= */

#define SUBGHZ_TX_MAX 512

static uint32_t tx_timings[SUBGHZ_TX_MAX];
static size_t tx_count = 0;
static size_t tx_pos = 0;

/** Async TX yielding callback: returns the next LevelDuration or Stop. */
static LevelDuration subghz_tx_yield_callback(void* ctx) {
    UNUSED(ctx);
    if(tx_pos >= tx_count) return level_duration_reset();

    uint32_t duration = tx_timings[tx_pos];
    bool level = (tx_pos % 2 == 0); /* even index = mark, odd = space */
    tx_pos++;
    return level_duration_make(level, duration);
}

void subghz_tx_handler(uint32_t id, const char* json) {
    if(!resource_can_acquire(RESOURCE_SUBGHZ)) {
        rpc_send_error(id, "resource_busy", "subghz_tx");
        return;
    }

    tx_count = 0;
    tx_pos = 0;

    if(!json_extract_uint32_array(json, "timings", tx_timings, &tx_count, SUBGHZ_TX_MAX)) {
        rpc_send_error(id, "missing_timings", "subghz_tx");
        return;
    }

    uint32_t freq = 433920000;
    json_extract_uint32(json, "freq", &freq);

    resource_acquire(RESOURCE_SUBGHZ);

    furi_hal_subghz_reset();
    furi_hal_subghz_load_preset(FuriHalSubGhzPresetOok650Async);
    furi_hal_subghz_set_frequency_and_path(freq);
    furi_hal_subghz_start_async_tx(subghz_tx_yield_callback, NULL);

    /* Poll until TX completes (each timing is in µs; 512 × ~1000 µs = ~512 ms max) */
    while(!furi_hal_subghz_is_async_tx_complete()) {
        furi_delay_ms(1);
    }

    furi_hal_subghz_stop_async_tx();
    furi_hal_subghz_sleep();

    resource_release(RESOURCE_SUBGHZ);

    rpc_send_ok(id, "subghz_tx");
    FURI_LOG_I("RPC", "SubGhz TX done freq=%" PRIu32 " count=%zu", freq, tx_count);
}

/* =========================================================
 * subghz_get_rssi — momentary RSSI sample
 * ========================================================= */

void subghz_get_rssi_handler(uint32_t id, const char* json) {
    if(!resource_can_acquire(RESOURCE_SUBGHZ)) {
        rpc_send_error(id, "resource_busy", "subghz_get_rssi");
        return;
    }

    uint32_t freq = 433920000;
    json_extract_uint32(json, "freq", &freq);

    resource_acquire(RESOURCE_SUBGHZ);

    furi_hal_subghz_reset();
    furi_hal_subghz_set_frequency_and_path(freq);
    furi_hal_subghz_rx();
    furi_delay_ms(5); /* wait for AGC to settle */

    float rssi = furi_hal_subghz_get_rssi();

    furi_hal_subghz_sleep();
    resource_release(RESOURCE_SUBGHZ);

    /* Encode as integer tenths-of-dBm to avoid %f */
    int32_t rssi_10 = (int32_t)(rssi * 10.0f);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"rssi_dbm10\":%" PRIi32 "}}\n",
        id,
        rssi_10);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " subghz_rssi -> %" PRIi32, id, rssi_10);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * subghz_rx_start (stream)
 * ========================================================= */

static void subghz_rx_callback(void* ctx, bool level, uint32_t duration_us) {
    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    StreamEvent ev;
    ev.stream_id = stream_id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"level\":%s,\"duration_us\":%" PRIu32,
        level ? "true" : "false",
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

void subghz_rx_start_handler(uint32_t id, const char* json) {
    uint32_t freq = 433920000;
    json_extract_uint32(json, "freq", &freq);

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
