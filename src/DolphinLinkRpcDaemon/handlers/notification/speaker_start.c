/**
 * speaker_start.c — RPC handler implementation for the "speaker_start" command
 *
 * Starts a continuous tone on the piezo speaker.  The dispatcher pre-checks
 * RESOURCE_SPEAKER availability before calling this handler.  The handler
 * acquires the resource, then calls the HAL-level acquire.  If the HAL-level
 * acquire fails (should not normally happen), the handler releases the resource
 * and returns a "resource_busy" error.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"fr":440,"vo":128}
 *     fr   — frequency in Hz (uint32; cast to float for the HAL)
 *     vo   — 0–255 mapped linearly to 0.0–1.0 HAL volume
 *
 * Wire format (response — success):
 *   {"t":0,"i":N}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"resource_busy"}  — HAL-level acquire failed
 *
 * Resources: RESOURCE_SPEAKER (dispatcher pre-checks; handler acquires and
 *            releases on success path via speaker_stop, or on error path here).
 * Thread: main (FuriEventLoop).
 */

#include "speaker_start.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_speaker.h>
#include <inttypes.h>

void speaker_start_handler(uint32_t id, const char* json, size_t offset) {
    JsonValue val;
    uint32_t freq = 440;
    uint32_t volume_raw = 128; /* 0–255 */

    if(json_find(json, "fr", offset, &val)) {
        json_value_uint32(&val, &freq);
        offset = val.offset;
    }
    if(json_find(json, "vo", offset, &val)) {
        json_value_uint32(&val, &volume_raw);
    }
    (void)offset;
    if(volume_raw > 255) volume_raw = 255;

    float volume = (float)volume_raw / 255.0f;

    resource_acquire(RESOURCE_SPEAKER);
    if(!furi_hal_speaker_acquire(1000)) {
        resource_release(RESOURCE_SPEAKER);
        rpc_send_error(id, "resource_busy", "speaker_start");
        return;
    }

    furi_hal_speaker_start((float)freq, volume);
    rpc_send_ok(id, "speaker_start");
    FURI_LOG_I("RPC", "speaker_start freq=%" PRIu32 " volume=%" PRIu32, freq, volume_raw);
}
