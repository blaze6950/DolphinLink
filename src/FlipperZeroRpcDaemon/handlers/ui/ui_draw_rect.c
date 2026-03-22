/**
 * ui_draw_rect.c — ui_draw_rect RPC handler implementation
 *
 * Queues a draw-rectangle operation into the canvas session op buffer.
 * The rectangle is rendered on the next ui_flush call.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"x":0,"y":0,"w":128,"h":64,"fi":0}
 *
 *   filled: true = canvas_draw_box (filled), false = canvas_draw_frame (outline, default)
 *
 * Wire format (response – ok):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#include "ui_draw_rect.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

void ui_draw_rect_handler(uint32_t id, const char* json) {
    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_draw_rect");
        return;
    }

    uint32_t x = 0, y = 0, w = 0, h = 0;
    bool filled = false;
    json_extract_uint32(json, "x", &x);
    json_extract_uint32(json, "y", &y);
    json_extract_uint32(json, "w", &w);
    json_extract_uint32(json, "h", &h);
    json_extract_bool(json, "fi", &filled);

    UiDrawOp op = {.type = UI_OP_DRAW_RECT};
    op.draw_rect.x = (uint8_t)x;
    op.draw_rect.y = (uint8_t)y;
    op.draw_rect.w = (uint8_t)w;
    op.draw_rect.h = (uint8_t)h;
    op.draw_rect.filled = filled;

    ui_canvas_op_push(&op);
    rpc_send_ok(id, "ui_draw_rect");
}
