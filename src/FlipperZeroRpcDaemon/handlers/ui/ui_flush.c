/**
 * ui_flush.c — ui_flush RPC handler implementation
 *
 * Calls view_port_update() on the host's canvas ViewPort, causing the Flipper
 * GUI to invoke the draw callback which replays all buffered draw operations.
 * The op buffer is cleared after the update is requested.
 *
 * Wire format (request):
 *   {"c":45,"i":N}
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#include "ui_flush.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"

void ui_flush_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_flush");
        return;
    }

    /* Promote pending ops → active buffer (draw callback reads from there),
     * clear the pending buffer, then signal a GUI redraw.
     * Committing before view_port_update() guarantees the draw callback always
     * sees a stable snapshot — clearing after the update would race the GUI
     * thread and could wipe ops before the callback runs. */
    ui_canvas_ops_commit();
    view_port_update(g_canvas_session.viewport);

    rpc_send_ok(id, "ui_flush");
}
