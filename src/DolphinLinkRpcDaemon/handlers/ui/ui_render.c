/**
 * ui_render.c — ui_render RPC handler implementation
 *
 * Promotes the current pending op buffer to the active buffer and triggers a
 * screen redraw, but unlike ui_flush it does NOT clear the pending buffer.
 * This allows incremental updates: each new draw command is sent and then
 * render is called — the Flipper displays all accumulated ops without the
 * caller needing to resend the entire scene each time.
 *
 * Wire format (request):
 *   {"c":46,"i":N}
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#include "ui_render.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"

void ui_render_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_render");
        return;
    }

    /* Promote pending ops → active buffer without clearing pending.
     * The draw callback will render all ops; new draw commands will append. */
    ui_canvas_ops_commit(false);
    view_port_update(g_canvas_session.viewport);

    rpc_send_ok(id, "ui_render");
}
