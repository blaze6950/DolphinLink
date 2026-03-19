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
 *   rpc_base64     Base64 encode / decode helpers
 *   rpc_transport  USB CDC send + ISR RX framing → rx_queue
 *   rpc_cmd_log    On-screen command log ring buffer
 *   rpc_response   Response formatting helpers (dedup cdc_send + cmd_log_push)
 *   rpc_dispatch   Command registry + dispatcher
 *   rpc_handlers           ping, stream_close
 *   rpc_handlers_system    device_info, power_info, datetime_get/set, region_info,
 *                          frequency_is_allowed
 *   rpc_handlers_gpio      gpio_read, gpio_write, adc_read, gpio_set_5v,
 *                          gpio_watch_start
 *   rpc_handlers_ir        ir_tx, ir_tx_raw, ir_receive_start
 *   rpc_handlers_subghz    subghz_tx, subghz_get_rssi, subghz_rx_start
 *   rpc_handlers_nfc       nfc_scan_start
 *   rpc_handlers_notification  led_set, vibro, speaker_start, speaker_stop, backlight
 *   rpc_handlers_storage   storage_info, storage_list, storage_read, storage_write,
 *                          storage_mkdir, storage_remove, storage_stat
 *   rpc_handlers_rfid      lfrfid_read_start
 *   rpc_handlers_ibutton   ibutton_read_start
 *   rpc_gui        ViewPort, draw/input callbacks, setup/teardown
 */

#include "core/rpc_resource.h"
#include "core/rpc_stream.h"
#include "core/rpc_transport.h"
#include "core/rpc_cmd_log.h"
#include "core/rpc_dispatch.h"
#include "core/rpc_gui.h"

#include <furi.h>
#include <furi_hal_usb_cdc.h>
#include <furi_hal_usb.h>
#include <storage/storage.h>
#include <notification/notification.h>
#include <notification/notification_messages.h>

/* =========================================================
 * Global state storage
 * (extern declarations live in the respective headers)
 * ========================================================= */

ResourceMask active_resources = 0;
FuriMessageQueue* rx_queue = NULL;

/** Storage service handle — opened at startup, closed on exit. */
Storage* g_storage = NULL;

/** Notification service handle — opened at startup, closed on exit. */
NotificationApp* g_notification = NULL;

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
 * Event-loop subscriber: CDC control-line (DTR/RTS) changed
 * ========================================================= */

/**
 * Called on the main thread when the host toggles DTR/RTS (e.g. serial port
 * opened or closed, host application crashed, USB cable pulled).
 *
 * When DTR drops (host disconnected), close all active streams so that any
 * custom exit combos are cleared and the default Back+Short exit is restored.
 * The daemon itself keeps running — the next client connection can open new
 * streams without needing a Flipper reboot.
 */
static void on_ctrl_line_queue(FuriEventLoopObject* object, void* ctx) {
    UNUSED(object);
    UNUSED(ctx);
    CdcCtrlEvent ev;
    while(furi_message_queue_get(cdc_ctrl_queue, &ev, 0) == FuriStatusOk) {
        bool dtr = (ev.ctrl_lines & CdcCtrlLineDTR) != 0;
        FURI_LOG_I("RPC", "DTR %s", dtr ? "asserted" : "released");
        if(!dtr) {
            /* Host disconnected — tear down all open streams so hardware
             * resources and the exit-combo suppression are released. */
            stream_close_all();
            resource_reset();
        }
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

    /* --- Open shared service records --- */
    g_storage = furi_record_open(RECORD_STORAGE);
    g_notification = furi_record_open(RECORD_NOTIFICATION);

    /* --- Message queues --- */
    rx_queue = furi_message_queue_alloc(16, sizeof(RxLine));
    stream_event_queue = furi_message_queue_alloc(32, sizeof(StreamEvent));
    /* Capacity 2: one connect + one disconnect event is the maximum burst.
     * Must be allocated before furi_hal_cdc_set_callbacks() which fires the
     * callback immediately with the current ctrl-line state. */
    cdc_ctrl_queue = furi_message_queue_alloc(2, sizeof(CdcCtrlEvent));

    AppState app;
    app.event_loop = NULL;
    app.view_port = NULL;
    /* Capacity 8: each button tap generates ~3 events (press/short/release);
     * 8 slots comfortably absorbs two rapid taps without dropping events. */
    app.input_queue = furi_message_queue_alloc(8, sizeof(InputEvent));

    /* --- USB CDC setup --- */
    /* Save whatever USB config is active so we can restore it on exit */
    FuriHalUsbInterface* prev_usb = furi_hal_usb_get_config();
    furi_hal_usb_set_config(&usb_cdc_dual, NULL);

    /* Allocate TX thread + stream buffer + semaphore before registering
     * callbacks, so cdc_tx_callback() is safe to fire immediately. */
    cdc_transport_alloc();

    CdcCallbacks cdc_cb = {
        .tx_ep_callback = cdc_tx_callback,
        .rx_ep_callback = cdc_rx_callback,
        .state_callback = NULL,
        .ctrl_line_callback = cdc_ctrl_line_callback,
        .config_callback = NULL,
    };
    furi_hal_cdc_set_callbacks(RPC_CDC_IF, &cdc_cb, cdc_ctrl_queue);

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

    furi_event_loop_subscribe_message_queue(
        app.event_loop, cdc_ctrl_queue, FuriEventLoopEventIn, on_ctrl_line_queue, NULL);

    /* --- Run (blocks until Back is pressed) --- */
    furi_event_loop_run(app.event_loop);

    /* --- Cleanup --- */

    /* Close all streams first — teardowns may unsubscribe their own queues */
    stream_close_all();
    resource_reset();

    furi_event_loop_unsubscribe(app.event_loop, rx_queue);
    furi_event_loop_unsubscribe(app.event_loop, stream_event_queue);
    furi_event_loop_unsubscribe(app.event_loop, app.input_queue);
    furi_event_loop_unsubscribe(app.event_loop, cdc_ctrl_queue);

    g_event_loop = NULL;
    furi_event_loop_free(app.event_loop);

    rpc_gui_teardown(&app, gui);
    furi_record_close(RECORD_GUI);

    /* Close shared service records */
    furi_record_close(RECORD_STORAGE);
    g_storage = NULL;
    furi_record_close(RECORD_NOTIFICATION);
    g_notification = NULL;

    /* Detach CDC callbacks before tearing down the TX thread — this ensures
     * no further tx_ep_callback firings can race with cdc_transport_free(). */
    furi_hal_cdc_set_callbacks(RPC_CDC_IF, NULL, NULL);
    cdc_transport_free();
    furi_hal_usb_set_config(prev_usb, NULL);

    furi_message_queue_free(app.input_queue);
    furi_message_queue_free(stream_event_queue);
    stream_event_queue = NULL;
    furi_message_queue_free(rx_queue);
    rx_queue = NULL;
    furi_message_queue_free(cdc_ctrl_queue);
    cdc_ctrl_queue = NULL;

    FURI_LOG_I("RPC", "Flipper RPC Daemon stopped");
    return 0;
}
