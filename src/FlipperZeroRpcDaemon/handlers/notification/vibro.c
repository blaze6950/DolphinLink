/**
 * vibro.c — RPC handler implementation for the "vibro" command
 *
 * Enables or disables the Flipper Zero vibration motor.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"vibro","enable":true|false}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"missing_enable"}  — "enable" field absent
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
    if(!json_extract_bool(json, "enable", &enable)) {
        rpc_send_error(id, "missing_enable", "vibro");
        return;
    }

    furi_hal_vibro_on(enable);
    rpc_send_ok(id, "vibro");
    FURI_LOG_I("RPC", "vibro enable=%d", (int)enable);
}
