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
 *   {"t":0,"i":N}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 */

#include "ui_draw_rect.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

void ui_draw_rect_handler(uint32_t id, const char* json, size_t offset) {
    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_draw_rect");
        return;
    }

    JsonValue val;
    uint32_t x = 0, y = 0, w = 0, h = 0;
    bool filled = false;
    if(json_find(json, "x", offset, &val)) {
        json_value_uint32(&val, &x);
        offset = val.offset;
    }
    if(json_find(json, "y", offset, &val)) {
        json_value_uint32(&val, &y);
        offset = val.offset;
    }
    if(json_find(json, "w", offset, &val)) {
        json_value_uint32(&val, &w);
        offset = val.offset;
    }
    if(json_find(json, "h", offset, &val)) {
        json_value_uint32(&val, &h);
        offset = val.offset;
    }
    if(json_find(json, "fi", offset, &val)) {
        json_value_bool(&val, &filled);
    }
    (void)offset;

    UiDrawOp op = {.type = UI_OP_DRAW_RECT};
    op.draw_rect.x = (uint8_t)x;
    op.draw_rect.y = (uint8_t)y;
    op.draw_rect.w = (uint8_t)w;
    op.draw_rect.h = (uint8_t)h;
    op.draw_rect.filled = filled;

    ui_canvas_op_push(&op);
    rpc_send_ok(id, "ui_draw_rect");
}
