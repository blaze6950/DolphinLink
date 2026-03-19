/**
 * speaker_start.c — RPC handler implementation for the "speaker_start" command
 *
 * Starts a continuous tone on the piezo speaker.  The dispatcher has already
 * acquired RESOURCE_SPEAKER before this handler is called.  If the HAL-level
 * acquire fails (should not normally happen), the handler releases the resource
 * and returns a "resource_busy" error.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"speaker_start","freq":440,"volume":128}
 *     freq   — frequency in Hz (uint32; cast to float for the HAL)
 *     volume — 0–255 mapped linearly to 0.0–1.0 HAL volume
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"resource_busy"}  — HAL-level acquire failed
 *
 * Resources: RESOURCE_SPEAKER (acquired by dispatcher before call).
 * Thread: main (FuriEventLoop).
 */

#include "speaker_start.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_speaker.h>
#include <inttypes.h>

void speaker_start_handler(uint32_t id, const char* json) {
    uint32_t freq = 440;
    uint32_t volume_raw = 128; /* 0–255 */

    json_extract_uint32(json, "freq", &freq);
    json_extract_uint32(json, "volume", &volume_raw);
    if(volume_raw > 255) volume_raw = 255;

    float volume = (float)volume_raw / 255.0f;

    /* resource_acquire already called by dispatcher */
    if(!furi_hal_speaker_acquire(1000)) {
        rpc_send_error(id, "resource_busy", "speaker_start");
        resource_release(RESOURCE_SPEAKER);
        return;
    }

    furi_hal_speaker_start((float)freq, volume);
    rpc_send_ok(id, "speaker_start");
    FURI_LOG_I("RPC", "speaker_start freq=%" PRIu32 " volume=%" PRIu32, freq, volume_raw);
}
