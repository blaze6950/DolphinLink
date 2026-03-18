/**
 * rpc_gui.h — GUI layer (ViewPort, input, draw callback)
 *
 * Manages the Flipper Zero screen for the RPC daemon.  Uses a ViewPort
 * registered on the fullscreen GUI layer — compatible with FuriEventLoop
 * (unlike ViewDispatcher which owns its own event loop).
 *
 * Usage in the entry point:
 *   AppState app;
 *   Gui* gui = furi_record_open(RECORD_GUI);
 *   rpc_gui_setup(&app, gui);
 *   // ... run event loop ...
 *   rpc_gui_teardown(&app, gui);
 *   furi_record_close(RECORD_GUI);
 */

#pragma once

#include <furi.h>
#include <gui/gui.h>
#include <gui/view_port.h>

/** All GUI + event-loop state bundled for easy passing by pointer. */
typedef struct {
    FuriEventLoop* event_loop;
    ViewPort* view_port;
    FuriMessageQueue* input_queue;
} AppState;

/**
 * Allocate the ViewPort, register draw/input callbacks, add it to the GUI,
 * and set g_view_port so that cmd_log_push() can trigger redraws.
 */
void rpc_gui_setup(AppState* app, Gui* gui);

/**
 * Remove the ViewPort from the GUI, clear g_view_port, and free the ViewPort.
 */
void rpc_gui_teardown(AppState* app, Gui* gui);

/* Event-loop subscriber: input_queue became readable.
 * Stops the event loop when the Back button is short-pressed.
 * Signature required by furi_event_loop_subscribe_message_queue(). */
void on_input_queue(FuriEventLoopObject* object, void* ctx);
