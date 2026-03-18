/*
 * Flipper Zero RPC Daemon — entry point
 *
 * Transport : USB CDC (appears as COMx on the host)
 * Framing   : NDJSON — one JSON object per line, terminated with '\n'
 * Threading : CDC ISR → FuriMessageQueue → FuriEventLoop (main thread)
 * GUI       : ViewPort rendered through Gui record (compatible with FuriEventLoop)
 *
 * Protocol (request):
 *   {"id":<uint>,"cmd":"<name>"[,"stream":<uint>][,...args...]}
 *
 * Protocol (response – ok):
 *   {"id":<uint>,"status":"ok"[,"data":{...}]}
 *   {"id":<uint>,"stream":<uint>}               <- stream opened
 *   {"id":<uint>,"event":{...},"stream":<uint>} <- stream event
 *
 * Protocol (response – error):
 *   {"id":<uint>,"error":"<code>"}
 *
 * Module layout:
 *   rpc_resource   Hardware resource bitmask (BLE, SubGHz, IR, NFC, …)
 *   rpc_stream     Active stream table, StreamEvent queue, g_event_loop
 *   rpc_json       Pure JSON extraction helpers
 *   rpc_transport  USB CDC send + ISR RX framing → rx_queue
 *   rpc_cmd_log    On-screen command log ring buffer
 *   rpc_response   Response formatting helpers (dedup cdc_send + cmd_log_push)
 *   rpc_dispatch   Command registry + dispatcher
 *   rpc_handlers   ping, ir_receive_start, gpio_watch_start,
 *                  subghz_rx_start, nfc_scan_start, stream_close
 *   rpc_gui        ViewPort, draw/input callbacks, setup/teardown
 */

#include "rpc_resource.h"
#include "rpc_stream.h"
#include "rpc_transport.h"
#include "rpc_cmd_log.h"
#include "rpc_dispatch.h"
#include "rpc_gui.h"

#include <furi.h>
#include <furi_hal_usb_cdc.h>
#include <furi_hal_usb.h>

/* =========================================================
 * Global state storage
 * (extern declarations live in the respective headers)
 * ========================================================= */

ResourceMask active_resources = 0;
FuriMessageQueue* rx_queue = NULL;

/* =========================================================
 * Event-loop subscriber: rx_queue has a line ready
 * ========================================================= */

static void on_rx_queue(FuriEventLoopObject* object, void* ctx) {
    UNUSED(object);
    UNUSED(ctx);
    RxLine line;
    while(furi_message_queue_get(rx_queue, &line, 0) == FuriStatusOk) {
        rpc_dispatch(line.data);
    }
}

/* =========================================================
 * Entry point
 * ========================================================= */

int32_t flipper_zero_rpc_daemon_app(void* p) {
    UNUSED(p);
    FURI_LOG_I("RPC", "Flipper RPC Daemon starting");

    /* --- Init module state --- */
    resource_reset();
    stream_reset();
    cmd_log_reset();

    /* --- Message queues --- */
    rx_queue = furi_message_queue_alloc(16, sizeof(RxLine));
    stream_event_queue = furi_message_queue_alloc(32, sizeof(StreamEvent));

    AppState app;
    app.event_loop = NULL;
    app.view_port = NULL;
    app.input_queue = furi_message_queue_alloc(4, sizeof(InputEvent));

    /* --- USB CDC setup --- */
    /* Save whatever USB config is active so we can restore it on exit */
    FuriHalUsbInterface* prev_usb = furi_hal_usb_get_config();
    furi_hal_usb_set_config(&usb_cdc_dual, NULL);

    CdcCallbacks cdc_cb = {
        .rx_ep_callback = cdc_rx_callback,
        .state_callback = NULL,
        .ctrl_line_callback = NULL,
        .config_callback = NULL,
    };
    furi_hal_cdc_set_callbacks(RPC_CDC_IF, &cdc_cb, NULL);

    FURI_LOG_I("RPC", "USB CDC ready");

    /* --- GUI --- */
    Gui* gui = furi_record_open(RECORD_GUI);
    rpc_gui_setup(&app, gui);

    /* --- Event loop --- */
    app.event_loop = furi_event_loop_alloc();
    g_event_loop = app.event_loop;

    furi_event_loop_subscribe_message_queue(
        app.event_loop, rx_queue, FuriEventLoopEventIn, on_rx_queue, NULL);

    furi_event_loop_subscribe_message_queue(
        app.event_loop, stream_event_queue, FuriEventLoopEventIn, on_stream_event, NULL);

    furi_event_loop_subscribe_message_queue(
        app.event_loop, app.input_queue, FuriEventLoopEventIn, on_input_queue, &app);

    /* --- Run (blocks until Back is pressed) --- */
    furi_event_loop_run(app.event_loop);

    /* --- Cleanup --- */

    /* Close all streams first — teardowns may unsubscribe their own queues */
    stream_close_all();
    resource_reset();

    furi_event_loop_unsubscribe(app.event_loop, rx_queue);
    furi_event_loop_unsubscribe(app.event_loop, stream_event_queue);
    furi_event_loop_unsubscribe(app.event_loop, app.input_queue);

    g_event_loop = NULL;
    furi_event_loop_free(app.event_loop);

    rpc_gui_teardown(&app, gui);
    furi_record_close(RECORD_GUI);

    /* Detach CDC callbacks before switching USB back */
    furi_hal_cdc_set_callbacks(RPC_CDC_IF, NULL, NULL);
    furi_hal_usb_set_config(prev_usb, NULL);

    furi_message_queue_free(app.input_queue);
    furi_message_queue_free(stream_event_queue);
    stream_event_queue = NULL;
    furi_message_queue_free(rx_queue);
    rx_queue = NULL;

    FURI_LOG_I("RPC", "Flipper RPC Daemon stopped");
    return 0;
}
