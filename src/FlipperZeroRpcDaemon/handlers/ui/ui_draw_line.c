/**
 * ui_draw_line.c — ui_draw_line RPC handler implementation
 *
 * Queues a draw-line operation into the canvas session op buffer.
 * The line is rendered on the next ui_flush call.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ui_draw_line","x1":0,"y1":0,"x2":127,"y2":63}
 *
 * Wire format (response – ok):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#include "ui_draw_line.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

void ui_draw_line_handler(uint32_t id, const char* json) {
    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_draw_line");
        return;
    }

    uint32_t x1 = 0, y1 = 0, x2 = 0, y2 = 0;
    json_extract_uint32(json, "x1", &x1);
    json_extract_uint32(json, "y1", &y1);
    json_extract_uint32(json, "x2", &x2);
    json_extract_uint32(json, "y2", &y2);

    UiDrawOp op = {.type = UI_OP_DRAW_LINE};
    op.draw_line.x1 = (uint8_t)x1;
    op.draw_line.y1 = (uint8_t)y1;
    op.draw_line.x2 = (uint8_t)x2;
    op.draw_line.y2 = (uint8_t)y2;

    ui_canvas_op_push(&op);
    rpc_send_ok(id, "ui_draw_line");
}
