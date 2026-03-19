/**
 * ui_flush.c — ui_flush RPC handler implementation
 *
 * Calls view_port_update() on the host's canvas ViewPort, causing the Flipper
 * GUI to invoke the draw callback which replays all buffered draw operations.
 * The op buffer is cleared after the update is requested.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ui_flush"}
 *
 * Wire format (response – ok):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#include "ui_flush.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"

void ui_flush_handler(uint32_t id, const char* json) {
    UNUSED(json);

    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_flush");
        return;
    }

    /* Trigger a redraw — the draw callback will replay g_canvas_session.ops */
    view_port_update(g_canvas_session.viewport);

    /* Clear the op buffer so the next flush starts clean */
    ui_canvas_ops_clear();

    rpc_send_ok(id, "ui_flush");
}
