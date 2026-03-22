/**
 * vibro.c — RPC handler implementation for the "vibro" command
 *
 * Enables or disables the Flipper Zero vibration motor.
 *
 * Wire format (request):
 *   {"c":26,"i":N,"en":0|1}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"missing_enable"}  — "en" field absent
 *
 * Resources: none (0).
 * Thread: main (FuriEventLoop).
 */

#include "vibro.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_vibro.h>
#include <stdbool.h>

void vibro_handler(uint32_t id, const char* json) {
    bool enable = false;
    if(!json_extract_bool(json, "en", &enable)) {
        rpc_send_error(id, "missing_enable", "vibro");
        return;
    }

    furi_hal_vibro_on(enable);
    rpc_send_ok(id, "vibro");
    FURI_LOG_I("RPC", "vibro enable=%d", (int)enable);
}
