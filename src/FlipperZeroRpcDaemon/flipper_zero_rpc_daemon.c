/*
 * Flipper Zero RPC Daemon
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
 *   {"id":<uint>,"stream":<uint>}          <- stream opened
 *   {"id":<uint>,"event":{...},"stream":<uint>}  <- stream event
 *
 * Protocol (response – error):
 *   {"id":<uint>,"error":"<code>"}
 */

#include <furi.h>
#include <furi_hal_usb_cdc.h>
#include <furi_hal_usb.h>
#include <gui/gui.h>
#include <gui/view_port.h>
#include <string.h>
#include <stdio.h>
#include <inttypes.h>

/* =========================================================
 * Resource management
 * ========================================================= */

typedef uint32_t ResourceMask;

#define RESOURCE_BLE    (1u << 0)
#define RESOURCE_SUBGHZ (1u << 1)
#define RESOURCE_IR     (1u << 2)

static ResourceMask active_resources = 0;

static bool resource_can_acquire(ResourceMask mask) {
    return (active_resources & mask) == 0;
}

static void resource_acquire(ResourceMask mask) {
    active_resources |= mask;
}

static void resource_release(ResourceMask mask) {
    active_resources &= ~mask;
}

/* =========================================================
 * Stream table
 * ========================================================= */

typedef struct {
    uint32_t id;
    ResourceMask resources;
    bool active;
} RpcStream;

#define MAX_STREAMS 8
static RpcStream active_streams[MAX_STREAMS];
static uint32_t next_stream_id = 1;

/* Returns slot index, or -1 if table is full. Does NOT acquire resources. */
static int stream_alloc_slot(void) {
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(!active_streams[i].active) return (int)i;
    }
    return -1;
}

static void stream_close_by_index(size_t idx) {
    if(idx < MAX_STREAMS && active_streams[idx].active) {
        resource_release(active_streams[idx].resources);
        active_streams[idx].active = false;
    }
}

/* =========================================================
 * USB CDC transport
 * ========================================================= */

/* CDC interface number (0 = first CDC device in usb_cdc_single config) */
#define RPC_CDC_IF 0

/* Outbound send: called from main thread only */
static void cdc_send(const char* json) {
    size_t len = strlen(json);
    furi_hal_cdc_send(RPC_CDC_IF, (uint8_t*)json, (uint32_t)len);
    FURI_LOG_I("RPC", "TX: %s", json);
}

/* =========================================================
 * RX line buffer — written from ISR, consumed on main thread
 * We store complete '\n'-terminated lines in a message queue.
 * Maximum line length is bounded so the queue element is fixed-size.
 * ========================================================= */

#define RX_LINE_MAX 256

typedef struct {
    char data[RX_LINE_MAX];
    uint16_t len;
} RxLine;

static FuriMessageQueue* rx_queue = NULL;

/* ISR-level accumulation buffer (single CDC interface, no re-entrancy needed
 * because the CDC RX callback is serialised by the USB driver). */
static char isr_buf[RX_LINE_MAX];
static uint16_t isr_pos = 0;

/* Called from USB interrupt context when data is available on the RX endpoint.
 * Signature must be void(*)(void*) — data is pulled via furi_hal_cdc_receive(). */
static void cdc_rx_callback(void* ctx) {
    UNUSED(ctx);

    /* CDC_DATA_SZ = 64 bytes per USB full-speed bulk packet */
    uint8_t buf[64];
    int32_t size = furi_hal_cdc_receive(RPC_CDC_IF, buf, sizeof(buf));
    if(size <= 0) return;

    for(int32_t i = 0; i < size; i++) {
        char c = (char)buf[i];

        if(isr_pos >= RX_LINE_MAX - 1) {
            /* Line too long — discard and reset */
            isr_pos = 0;
        }

        isr_buf[isr_pos++] = c;

        if(c == '\n') {
            isr_buf[isr_pos] = '\0';
            RxLine line;
            line.len = isr_pos;
            memcpy(line.data, isr_buf, isr_pos + 1); /* include NUL */
            /* Non-blocking put; drop on overflow rather than stall ISR */
            furi_message_queue_put(rx_queue, &line, 0);
            isr_pos = 0;
        }
    }
}

/* =========================================================
 * Minimal JSON helpers
 * =========================================================
 *
 * All helpers operate on a NUL-terminated string.
 * They return true on success and fill the output buffer / value.
 *
 * json_extract_string(json, key, out, out_size)
 *   Finds  "key":"value"  and copies value into out.
 *
 * json_extract_uint32(json, key, out)
 *   Finds  "key":NNN  and stores the integer in *out.
 * ========================================================= */

static bool json_extract_string(const char* json, const char* key, char* out, size_t out_size) {
    /* Build search token:  "key":  */
    char token[72];
    snprintf(token, sizeof(token), "\"%s\":", key);

    const char* pos = strstr(json, token);
    if(!pos) return false;

    pos += strlen(token);

    /* Skip whitespace */
    while(*pos == ' ' || *pos == '\t') pos++;

    if(*pos != '"') return false;
    pos++; /* skip opening quote */

    size_t i = 0;
    while(*pos && *pos != '"' && i < out_size - 1) {
        /* Handle simple escape sequences */
        if(*pos == '\\' && *(pos + 1)) {
            pos++;
            switch(*pos) {
            case '"':
                out[i++] = '"';
                break;
            case '\\':
                out[i++] = '\\';
                break;
            case 'n':
                out[i++] = '\n';
                break;
            case 'r':
                out[i++] = '\r';
                break;
            case 't':
                out[i++] = '\t';
                break;
            default:
                out[i++] = *pos;
                break;
            }
        } else {
            out[i++] = *pos;
        }
        pos++;
    }
    out[i] = '\0';
    return (i > 0 || *pos == '"'); /* empty string is valid */
}

static bool json_extract_uint32(const char* json, const char* key, uint32_t* out) {
    char token[72];
    snprintf(token, sizeof(token), "\"%s\":", key);

    const char* pos = strstr(json, token);
    if(!pos) return false;

    pos += strlen(token);

    /* Skip whitespace */
    while(*pos == ' ' || *pos == '\t') pos++;

    /* Must start with a digit */
    if(*pos < '0' || *pos > '9') return false;

    uint32_t val = 0;
    while(*pos >= '0' && *pos <= '9') {
        val = val * 10 + (uint32_t)(*pos - '0');
        pos++;
    }
    *out = val;
    return true;
}

/* =========================================================
 * Command registry
 * ========================================================= */

typedef void (*RpcHandler)(uint32_t request_id, const char* json);

typedef struct {
    const char* name;
    ResourceMask resources;
    bool is_stream;
    RpcHandler handler;
} RpcCommand;

/* Forward declarations */
static void ping_handler(uint32_t id, const char* json);
static void ble_scan_start_handler(uint32_t id, const char* json);
static void stream_close_handler(uint32_t id, const char* json);

static const RpcCommand commands[] = {
    {"ping", 0, false, ping_handler},
    {"ble_scan_start", RESOURCE_BLE, true, ble_scan_start_handler},
    {"stream_close", 0, false, stream_close_handler},
    {NULL, 0, false, NULL},
};

/* =========================================================
 * Dispatcher — runs on main thread
 * ========================================================= */

static void rpc_dispatch(const char* json) {
    uint32_t request_id = 0;
    char cmd[64] = {0};

    json_extract_uint32(json, "id", &request_id);

    if(!json_extract_string(json, "cmd", cmd, sizeof(cmd))) {
        /* Malformed — no cmd field */
        char err[128];
        snprintf(
            err,
            sizeof(err),
            "{\"id\":%" PRIu32 ",\"error\":\"missing_cmd\"}\n",
            request_id);
        cdc_send(err);
        return;
    }

    FURI_LOG_I("RPC", "cmd=%s id=%" PRIu32, cmd, request_id);

    for(size_t i = 0; commands[i].name != NULL; i++) {
        if(strcmp(commands[i].name, cmd) == 0) {
            /* Resource pre-check in dispatcher */
            if(commands[i].resources && !resource_can_acquire(commands[i].resources)) {
                char err[128];
                snprintf(
                    err,
                    sizeof(err),
                    "{\"id\":%" PRIu32 ",\"error\":\"resource_busy\"}\n",
                    request_id);
                cdc_send(err);
                return;
            }
            commands[i].handler(request_id, json);
            return;
        }
    }

    /* Unknown command */
    char err[128];
    snprintf(
        err, sizeof(err), "{\"id\":%" PRIu32 ",\"error\":\"unknown_command\"}\n", request_id);
    cdc_send(err);
}

/* =========================================================
 * Handlers
 * ========================================================= */

static void ping_handler(uint32_t id, const char* json) {
    UNUSED(json);
    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"pong\":true}}\n",
        id);
    cdc_send(resp);
}

static void ble_scan_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    /* Find a free slot BEFORE acquiring resources */
    int slot = stream_alloc_slot();
    if(slot < 0) {
        char err[128];
        snprintf(
            err,
            sizeof(err),
            "{\"id\":%" PRIu32 ",\"error\":\"stream_table_full\"}\n",
            id);
        cdc_send(err);
        return;
    }

    /* Acquire resources (dispatcher already confirmed they are free) */
    resource_acquire(RESOURCE_BLE);

    uint32_t stream_id = next_stream_id++;
    active_streams[slot].id = stream_id;
    active_streams[slot].resources = RESOURCE_BLE;
    active_streams[slot].active = true;

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"stream\":%" PRIu32 "}\n",
        id,
        stream_id);
    cdc_send(resp);

    FURI_LOG_I("RPC", "BLE scan stream opened id=%" PRIu32, stream_id);
    /*
     * In a real implementation, start BLE scanning here and emit events
     * like: {"event":{"addr":"AA:BB:CC:DD:EE:FF","rssi":-70},"stream":<id>}\n
     * for each discovered device.
     */
}

static void stream_close_handler(uint32_t id, const char* json) {
    uint32_t stream_id = 0;
    if(!json_extract_uint32(json, "stream", &stream_id)) {
        char err[128];
        snprintf(
            err,
            sizeof(err),
            "{\"id\":%" PRIu32 ",\"error\":\"missing_stream_id\"}\n",
            id);
        cdc_send(err);
        return;
    }

    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active && active_streams[i].id == stream_id) {
            stream_close_by_index(i);

            char resp[128];
            snprintf(resp, sizeof(resp), "{\"id\":%" PRIu32 ",\"status\":\"ok\"}\n", id);
            cdc_send(resp);

            FURI_LOG_I("RPC", "stream %" PRIu32 " closed", stream_id);
            return;
        }
    }

    char err[128];
    snprintf(
        err,
        sizeof(err),
        "{\"id\":%" PRIu32 ",\"error\":\"stream_not_found\"}\n",
        id);
    cdc_send(err);
}

/* =========================================================
 * GUI — ViewPort (compatible with FuriEventLoop)
 * ========================================================= */

/* Count active streams for the status display */
static uint32_t count_active_streams(void) {
    uint32_t n = 0;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active) n++;
    }
    return n;
}

static void draw_callback(Canvas* canvas, void* ctx) {
    UNUSED(ctx);
    canvas_set_font(canvas, FontPrimary);
    canvas_draw_str_aligned(canvas, 64, 10, AlignCenter, AlignTop, "RPC Daemon");

    canvas_set_font(canvas, FontSecondary);
    canvas_draw_str_aligned(canvas, 64, 28, AlignCenter, AlignTop, "USB CDC JSON-RPC");

    char buf[40];
    snprintf(buf, sizeof(buf), "Active streams: %" PRIu32, count_active_streams());
    canvas_draw_str_aligned(canvas, 64, 42, AlignCenter, AlignTop, buf);

    snprintf(buf, sizeof(buf), "Resources: 0x%02" PRIx32, active_resources);
    canvas_draw_str_aligned(canvas, 64, 54, AlignCenter, AlignTop, buf);

    canvas_draw_str_aligned(canvas, 64, 64, AlignCenter, AlignTop, "BACK to exit");
}

/* Custom event IDs for the FuriEventLoop */
#define EVT_INPUT_BACK 1u

typedef struct {
    FuriEventLoop* event_loop;
    ViewPort* view_port;
    FuriMessageQueue* input_queue;
} AppState;

static void input_callback(InputEvent* event, void* ctx) {
    AppState* app = ctx;
    /* We only care about BACK — post to input queue to decouple from GUI thread */
    if(event->type == InputTypeShort && event->key == InputKeyBack) {
        furi_message_queue_put(app->input_queue, event, 0);
    }
}

/* FuriEventLoop subscriber: input_queue became readable.
 * Signature: void(*)(FuriEventLoopObject*, void*) */
static void on_input_queue(FuriEventLoopObject* object, void* ctx) {
    UNUSED(object);
    AppState* app = ctx;
    InputEvent event;
    while(furi_message_queue_get(app->input_queue, &event, 0) == FuriStatusOk) {
        if(event.type == InputTypeShort && event.key == InputKeyBack) {
            furi_event_loop_stop(app->event_loop);
        }
    }
}

/* FuriEventLoop subscriber: rx_queue has a line ready.
 * Signature: void(*)(FuriEventLoopObject*, void*) */
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

    memset(active_streams, 0, sizeof(active_streams));
    active_resources = 0;
    next_stream_id = 1;

    /* --- Message queues --- */
    rx_queue = furi_message_queue_alloc(16, sizeof(RxLine));

    AppState app;
    app.input_queue = furi_message_queue_alloc(4, sizeof(InputEvent));

    /* --- USB CDC setup --- */
    /* Save whatever USB config is active so we can restore it on exit */
    FuriHalUsbInterface* prev_usb = furi_hal_usb_get_config();
    furi_hal_usb_set_config(&usb_cdc_single, NULL);

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
    app.view_port = view_port_alloc();
    view_port_draw_callback_set(app.view_port, draw_callback, NULL);
    view_port_input_callback_set(app.view_port, input_callback, &app);
    gui_add_view_port(gui, app.view_port, GuiLayerFullscreen);

    /* --- Event loop --- */
    app.event_loop = furi_event_loop_alloc();

    furi_event_loop_subscribe_message_queue(
        app.event_loop,
        rx_queue,
        FuriEventLoopEventIn,
        on_rx_queue,
        NULL);

    furi_event_loop_subscribe_message_queue(
        app.event_loop,
        app.input_queue,
        FuriEventLoopEventIn,
        on_input_queue,
        &app);

    /* --- Run --- */
    furi_event_loop_run(app.event_loop);

    /* --- Cleanup --- */
    furi_event_loop_unsubscribe(app.event_loop, rx_queue);
    furi_event_loop_unsubscribe(app.event_loop, app.input_queue);
    furi_event_loop_free(app.event_loop);

    gui_remove_view_port(gui, app.view_port);
    view_port_free(app.view_port);
    furi_record_close(RECORD_GUI);

    /* Detach CDC callbacks before switching USB back */
    furi_hal_cdc_set_callbacks(RPC_CDC_IF, NULL, NULL);
    furi_hal_usb_set_config(prev_usb, NULL);

    furi_message_queue_free(app.input_queue);
    furi_message_queue_free(rx_queue);
    rx_queue = NULL;

    /* Close all streams / release all resources */
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active) stream_close_by_index(i);
    }

    FURI_LOG_I("RPC", "Flipper RPC Daemon stopped");
    return 0;
}
