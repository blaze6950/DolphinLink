/**
 * speaker_stop.c — RPC handler implementation for the "speaker_stop" command
 *
 * Stops the piezo speaker and releases RESOURCE_SPEAKER so it can be
 * re-acquired by a subsequent "speaker_start" call.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"speaker_stop"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Resources: none claimed by dispatcher, but handler releases RESOURCE_SPEAKER.
 * Thread: main (FuriEventLoop).
 */

#include "speaker_stop.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_resource.h"

#include <furi.h>
#include <furi_hal_speaker.h>

void speaker_stop_handler(uint32_t id, const char* json) {
    UNUSED(json);

    furi_hal_speaker_stop();
    furi_hal_speaker_release();
    resource_release(RESOURCE_SPEAKER);

    rpc_send_ok(id, "speaker_stop");
    FURI_LOG_I("RPC", "speaker_stop");
}
