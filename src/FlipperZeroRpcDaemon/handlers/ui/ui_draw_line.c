/**
 * ui_draw_line.c — ui_draw_line RPC handler implementation
 *
 * Queues a draw-line operation into the canvas session op buffer.
 * The line is rendered on the next ui_flush call.
 *
 * Wire format (request):
 *   {"c":44,"i":N,"x1":0,"y1":0,"x2":127,"y2":63}
 *
 * Wire format (response – ok):
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#include "ui_draw_line.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

void ui_draw_line_handler(uint32_t id, const char* json, size_t offset) {
    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_draw_line");
        return;
    }

    JsonValue val;
    uint32_t x1 = 0, y1 = 0, x2 = 0, y2 = 0;
    if(json_find(json, "x1", offset, &val)) {
        json_value_uint32(&val, &x1);
        offset = val.offset;
    }
    if(json_find(json, "y1", offset, &val)) {
        json_value_uint32(&val, &y1);
        offset = val.offset;
    }
    if(json_find(json, "x2", offset, &val)) {
        json_value_uint32(&val, &x2);
        offset = val.offset;
    }
    if(json_find(json, "y2", offset, &val)) {
        json_value_uint32(&val, &y2);
    }
    (void)offset;

    UiDrawOp op = {.type = UI_OP_DRAW_LINE};
    op.draw_line.x1 = (uint8_t)x1;
    op.draw_line.y1 = (uint8_t)y1;
    op.draw_line.x2 = (uint8_t)x2;
    op.draw_line.y2 = (uint8_t)y2;

    ui_canvas_op_push(&op);
    rpc_send_ok(id, "ui_draw_line");
}
