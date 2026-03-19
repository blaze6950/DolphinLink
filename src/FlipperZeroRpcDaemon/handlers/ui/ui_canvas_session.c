/**
 * ui_canvas_session.c — canvas session state and draw-op buffer management
 */

#include "ui_canvas_session.h"

#include <furi.h>
#include <string.h>
#include <stdio.h>

/* -------------------------------------------------------------------------
 * Module-level singleton
 * ------------------------------------------------------------------------- */

UiCanvasSession g_canvas_session = {0};

/* -------------------------------------------------------------------------
 * Draw callback — replays buffered ops on the Canvas
 * ------------------------------------------------------------------------- */

static void canvas_draw_callback(Canvas* canvas, void* ctx) {
    UNUSED(ctx);
    UiCanvasSession* s = &g_canvas_session;

    canvas_clear(canvas);

    for(size_t i = 0; i < s->op_count; i++) {
        const UiDrawOp* op = &s->ops[i];
        switch(op->type) {
        case UI_OP_CLEAR:
            canvas_clear(canvas);
            break;
        case UI_OP_DRAW_STR: {
            Font font = (op->draw_str.font == 0)   ? FontPrimary :
                        (op->draw_str.font == 2)   ? FontBigNumbers :
                                                     FontSecondary;
            canvas_set_font(canvas, font);
            canvas_draw_str(canvas, op->draw_str.x, op->draw_str.y, op->draw_str.text);
            break;
        }
        case UI_OP_DRAW_RECT:
            if(op->draw_rect.filled) {
                canvas_draw_box(
                    canvas,
                    op->draw_rect.x,
                    op->draw_rect.y,
                    op->draw_rect.w,
                    op->draw_rect.h);
            } else {
                canvas_draw_frame(
                    canvas,
                    op->draw_rect.x,
                    op->draw_rect.y,
                    op->draw_rect.w,
                    op->draw_rect.h);
            }
            break;
        case UI_OP_DRAW_LINE:
            canvas_draw_line(
                canvas,
                op->draw_line.x1,
                op->draw_line.y1,
                op->draw_line.x2,
                op->draw_line.y2);
            break;
        }
    }
}

/* -------------------------------------------------------------------------
 * Public API
 * ------------------------------------------------------------------------- */

void ui_canvas_session_init(Gui* gui) {
    g_canvas_session.gui = gui;
    g_canvas_session.op_count = 0;

    g_canvas_session.viewport = view_port_alloc();
    view_port_draw_callback_set(g_canvas_session.viewport, canvas_draw_callback, NULL);
    gui_add_view_port(gui, g_canvas_session.viewport, GuiLayerFullscreen);
}

void ui_canvas_session_deinit(void) {
    if(g_canvas_session.viewport) {
        gui_remove_view_port(g_canvas_session.gui, g_canvas_session.viewport);
        view_port_free(g_canvas_session.viewport);
        g_canvas_session.viewport = NULL;
    }
    if(g_canvas_session.gui) {
        furi_record_close(RECORD_GUI);
    }
    g_canvas_session.gui = NULL;
    g_canvas_session.op_count = 0;
}

void ui_canvas_ops_clear(void) {
    g_canvas_session.op_count = 0;
}

void ui_canvas_op_push(const UiDrawOp* op) {
    if(g_canvas_session.op_count >= UI_OPS_MAX) return; /* buffer full — drop */
    g_canvas_session.ops[g_canvas_session.op_count++] = *op;
}
