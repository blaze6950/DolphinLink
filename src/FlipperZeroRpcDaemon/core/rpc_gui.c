/**
 * rpc_gui.c — GUI layer implementation
 */

#include "rpc_gui.h"
#include "rpc_cmd_log.h"
#include "rpc_stream.h"
#include "rpc_resource.h"

#include <stdio.h>
#include <inttypes.h>

/* -------------------------------------------------------------------------
 * Draw callback — renders the full screen
 * ------------------------------------------------------------------------- */

static void draw_callback(Canvas* canvas, void* ctx) {
    UNUSED(ctx);

    /* Header */
    canvas_set_font(canvas, FontPrimary);
    canvas_draw_str(canvas, 2, 10, "FlipperZero.NET Daemon");

    /* Status bar — connection indicator prefix + stream count + resource mask */
    canvas_set_font(canvas, FontSecondary);
    char status[48];
    snprintf(
        status,
        sizeof(status),
        "%s S:%" PRIu32 "  Res:0x%02" PRIx32,
        host_connected ? "[C]" : "[ ]",
        stream_count_active(),
        active_resources);
    canvas_draw_str(canvas, 2, 21, status);

    /* Separator */
    canvas_draw_line(canvas, 0, 24, 128, 24);

    /* Command log — oldest-to-newest, bottom-aligned.
     * 4 lines at FontSecondary (~10 px each), starting at y=34. */
    canvas_set_font(canvas, FontSecondary);
    size_t lines_to_show = cmd_log_count < CMD_LOG_LINES ? cmd_log_count : CMD_LOG_LINES;
    /* If the buffer is full the oldest entry is at cmd_log_next (the slot
     * about to be overwritten); otherwise the oldest is at index 0. */
    size_t start = (cmd_log_count >= CMD_LOG_LINES) ? cmd_log_next : 0;

    for(size_t i = 0; i < lines_to_show; i++) {
        size_t idx = (start + i) % CMD_LOG_LINES;
        int y = 34 + (int)i * 10;
        canvas_draw_str(canvas, 2, y, cmd_log[idx]);
    }
}

/* -------------------------------------------------------------------------
 * Input callback — runs on the GUI thread
 * ------------------------------------------------------------------------- */

static void input_callback(InputEvent* event, void* ctx) {
    AppState* app = ctx;
    /* Forward every key event to the main thread via the input queue.
     * on_input_queue() decides which combo triggers exit — this keeps
     * the GUI thread ISR-safe (only furi_message_queue_put here) and
     * allows custom exit combos on any key, not just Back+Short. */
    furi_message_queue_put(app->input_queue, event, 0);
}

/* -------------------------------------------------------------------------
 * Event-loop subscriber: input_queue became readable
 * ------------------------------------------------------------------------- */

void on_input_queue(FuriEventLoopObject* object, void* ctx) {
    UNUSED(object);
    AppState* app = ctx;
    InputEvent event;
    while(furi_message_queue_get(app->input_queue, &event, 0) == FuriStatusOk) {
        /* Check whether any active input stream has a custom exit combo. */
        bool has_custom = false;
        for(size_t i = 0; i < MAX_STREAMS; i++) {
            if(active_streams[i].active && active_streams[i].is_input_stream &&
               active_streams[i].hw.input.has_exit_combo) {
                if(event.type == active_streams[i].hw.input.exit_type &&
                   event.key == active_streams[i].hw.input.exit_key) {
                    furi_event_loop_stop(app->event_loop);
                }
                has_custom = true;
            }
        }
        /* Fall back to the default Back+Short combo when no custom combo is set. */
        if(!has_custom && event.type == InputTypeShort && event.key == InputKeyBack) {
            furi_event_loop_stop(app->event_loop);
        }
    }
}

/* -------------------------------------------------------------------------
 * Public API
 * ------------------------------------------------------------------------- */

void rpc_gui_setup(AppState* app, Gui* gui) {
    app->view_port = view_port_alloc();
    view_port_draw_callback_set(app->view_port, draw_callback, NULL);
    view_port_input_callback_set(app->view_port, input_callback, app);
    gui_add_view_port(gui, app->view_port, GuiLayerFullscreen);
    /* Expose the ViewPort so cmd_log_push() can trigger redraws */
    g_view_port = app->view_port;
}

void rpc_gui_teardown(AppState* app, Gui* gui) {
    g_view_port = NULL;
    gui_remove_view_port(gui, app->view_port);
    view_port_free(app->view_port);
}
