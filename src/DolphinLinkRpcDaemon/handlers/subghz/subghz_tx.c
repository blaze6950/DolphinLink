/**
 * subghz_tx.c — subghz_tx RPC handler implementation
 *
 * Transmits a raw OOK timing array on the internal CC1101 using the
 * async TX API.  The handler polls until TX completes, then sleeps the radio.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"tm":[9000,4500,...],"fr":433920000}
 *
 * Wire format (response):
 *   {"t":0,"i":N}
 *
 * Resources: RESOURCE_SUBGHZ (dispatcher pre-checks; handler acquires before
 *            TX and releases after)
 */

#include "subghz_tx.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_subghz.h>
#include <subghz/subghz_worker.h>
#include <subghz/devices/devices.h>
#include <subghz/devices/cc1101_int/cc1101_int_interconnect.h>
#include <inttypes.h>

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

void subghz_tx_handler(uint32_t id, const char* json, size_t offset) {
    tx_count = 0;
    tx_pos = 0;

    JsonValue val;
    if(!json_find(json, "tm", offset, &val)) {
        rpc_send_error(id, "missing_timings", "subghz_tx");
        return;
    }
    json_value_uint32_array(&val, tx_timings, &tx_count, SUBGHZ_TX_MAX);
    offset = val.offset;

    uint32_t freq = 433920000;
    if(json_find(json, "fr", offset, &val)) { json_value_uint32(&val, &freq); }
    (void)offset;

    resource_acquire(RESOURCE_SUBGHZ);

    const SubGhzDevice* dev = subghz_devices_get_by_name(SUBGHZ_DEVICE_CC1101_INT_NAME);
    subghz_devices_reset(dev);
    subghz_devices_load_preset(dev, FuriHalSubGhzPresetOok650Async, NULL);
    subghz_devices_set_frequency(dev, freq);
    furi_hal_subghz_start_async_tx(subghz_tx_yield_callback, NULL);

    /* Poll until TX completes (each timing is in µs; 512 × ~1000 µs = ~512 ms max) */
    while(!furi_hal_subghz_is_async_tx_complete()) {
        furi_delay_ms(1);
    }

    furi_hal_subghz_stop_async_tx();
    subghz_devices_sleep(dev);

    resource_release(RESOURCE_SUBGHZ);

    rpc_send_ok(id, "subghz_tx");
    FURI_LOG_I("RPC", "SubGhz TX done freq=%" PRIu32 " count=%zu", freq, tx_count);
}
