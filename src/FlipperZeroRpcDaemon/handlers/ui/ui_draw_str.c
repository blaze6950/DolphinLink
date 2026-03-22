/**
 * ui_draw_str.c — ui_draw_str RPC handler implementation
 *
 * Queues a draw-string operation into the canvas session op buffer.
 * The text is rendered on the next ui_flush call.
 *
 * Wire format (request):
 *   {"c":N,"i":M,"x":10,"y":20,"tx":"Hello","fn":1}
 *
 *   font: 0 = FontPrimary, 1 = FontSecondary (default), 2 = FontBigNumbers
 *
 * Wire format (response – ok):
 *   {"id":N,"status":"ok"}
 *
 * Error codes:
 *   resource_busy — RESOURCE_GUI is not held; call ui_screen_acquire first
 *   missing_text  — "text" field absent or empty
 */

#include "ui_draw_str.h"
#include "ui_canvas_session.h"

#include "../../core/rpc_resource.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <string.h>

void ui_draw_str_handler(uint32_t id, const char* json) {
    if(!resource_is_held(RESOURCE_GUI)) {
        rpc_send_error(id, "resource_busy", "ui_draw_str");
        return;
    }

    char text[UI_STR_MAX] = {0};
    if(!json_extract_string(json, "tx", text, sizeof(text)) || text[0] == '\0') {
        rpc_send_error(id, "missing_text", "ui_draw_str");
        return;
    }

    uint32_t x = 0, y = 0, font = 1;
    json_extract_uint32(json, "x", &x);
    json_extract_uint32(json, "y", &y);
    json_extract_uint32(json, "fn", &font);

    UiDrawOp op = {.type = UI_OP_DRAW_STR};
    op.draw_str.x = (uint8_t)x;
    op.draw_str.y = (uint8_t)y;
    op.draw_str.font = (uint8_t)(font > 2 ? 1 : font);
    strncpy(op.draw_str.text, text, UI_STR_MAX - 1);
    op.draw_str.text[UI_STR_MAX - 1] = '\0';

    ui_canvas_op_push(&op);
    rpc_send_ok(id, "ui_draw_str");
}
