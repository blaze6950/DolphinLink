/**
 * ui_screen_acquire.c — ui_screen_acquire RPC handler implementation
 *
 * Claims RESOURCE_GUI, hides the daemon's own ViewPort, and allocates a
 * secondary fullscreen ViewPort that the host can paint via ui_draw_* + ui_flush.
 *
 * Wire format (request):
 *   {"c":40,"i":N}
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI already held by another handler
 */

#include "ui_screen_acquire.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <gui/gui.h>

void ui_screen_acquire_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

    if(!resource_can_acquire(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_screen_acquire");
        return;
    }
    resource_acquire(RESOURCE_GUI);

    /* Hide the daemon's own status ViewPort so the host has the full screen */
    if(g_view_port) {
        view_port_enabled_set(g_view_port, false);
    }

    /* Open the Gui record and initialise the host canvas session */
    Gui* gui = furi_record_open(RECORD_GUI);
    ui_canvas_session_init(gui);

    rpc_send_ok(id, "ui_screen_acquire");
}
