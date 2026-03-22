/**
 * ui_screen_release.c — ui_screen_release RPC handler implementation
 *
 * Releases exclusive control of the Flipper screen (RESOURCE_GUI).
 * Removes the host's secondary ViewPort, closes the Gui record, and
 * re-enables the daemon's own status ViewPort.
 *
 * Wire format (request):
 *   {"c":41,"i":N}
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not currently held
 */

#include "ui_screen_release.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_cmd_log.h"

void ui_screen_release_handler(uint32_t id, const char* json) {
    UNUSED(json);

    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_screen_release");
        return;
    }

    /* Destroy the host canvas ViewPort and close the Gui record */
    ui_canvas_session_deinit();

    /* Restore the daemon's own status ViewPort */
    if(g_view_port) {
        view_port_enabled_set(g_view_port, true);
    }

    resource_release(RESOURCE_GUI);
    rpc_send_ok(id, "ui_screen_release");
}
