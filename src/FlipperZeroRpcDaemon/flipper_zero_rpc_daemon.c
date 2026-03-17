#include <furi.h>
#include <furi_hal_serial.h>
#include <gui/gui.h>
#include <gui/view.h>
#include <gui/view_dispatcher.h>
#include <string.h>
#include <stdio.h>

/* === РЕСУРСЫ (Resource Management) === */
typedef uint32_t ResourceMask;

#define RESOURCE_BLE    (1 << 0)
#define RESOURCE_SUBGHZ (1 << 1)
#define RESOURCE_IR     (1 << 2)

static ResourceMask active_resources = 0;

static bool can_acquire(ResourceMask mask) {
    return (active_resources & mask) == 0;
}

static void acquire(ResourceMask mask) {
    active_resources |= mask;
}

static void release(ResourceMask mask) {
    active_resources &= ~mask;
}

/* === СТРИМЫ === */
typedef struct {
    uint32_t id;
    ResourceMask resources;
    bool active;
} RpcStream;

#define MAX_STREAMS 8
static RpcStream active_streams[MAX_STREAMS];
static uint32_t next_stream_id = 1;

/* === РЕГИСТР КОМАНД (hardcoded — как будто сгенерировано из JSON) === */
typedef void (*RpcHandler)(uint32_t request_id, const char* args_json, FuriHalSerialHandle* serial);

typedef struct {
    const char* name;
    ResourceMask resources;
    bool is_stream;
    RpcHandler handler;
} RpcCommand;

static void ping_handler(uint32_t id, const char* args_json, FuriHalSerialHandle* serial);
static void
    ble_scan_start_handler(uint32_t id, const char* args_json, FuriHalSerialHandle* serial);
static void stream_close_handler(uint32_t id, const char* args_json, FuriHalSerialHandle* serial);

static const RpcCommand commands[] = {
    {"ping", 0, false, ping_handler},
    {"ble_scan_start", RESOURCE_BLE, true, ble_scan_start_handler},
    {"stream_close", 0, false, stream_close_handler},
    {NULL, 0, false, NULL} // конец таблицы
};

/* === СЕРИАЛЬНЫЙ ПОРТ И БУФЕР === */
static FuriHalSerialHandle* rpc_serial = NULL;
static char rx_buffer[512];
static size_t rx_pos = 0;

static void rpc_send_json(FuriHalSerialHandle* serial, const char* json) {
    if(serial) {
        furi_hal_serial_tx(serial, (uint8_t*)json, strlen(json));
    }
    FURI_LOG_I("RPC", "TX: %s", json);
}

/* === МИНИМАЛЬНЫЙ ПАРСЕР (token-based минимальный, без jsmn — для одного файла) === */
static void parse_and_dispatch(const char* json, FuriHalSerialHandle* serial) {
    uint32_t request_id = 0;
    char cmd[64] = {0};

    // Извлекаем id
    const char* id_pos = strstr(json, "\"id\":");
    if(id_pos) sscanf(id_pos + 5, "%u", &request_id);

    // Извлекаем cmd
    const char* cmd_pos = strstr(json, "\"cmd\":");
    if(cmd_pos) {
        const char* start = strchr(cmd_pos + 7, '"');
        if(start) {
            const char* end = strchr(start + 1, '"');
            if(end) strncpy(cmd, start + 1, end - start - 1);
        }
    }

    FURI_LOG_I("RPC", "Received cmd: %s (id=%u)", cmd, request_id);

    // Поиск в реестре
    for(size_t i = 0; commands[i].name != NULL; i++) {
        if(strcmp(commands[i].name, cmd) == 0) {
            // Проверка ресурсов
            if(commands[i].resources && !can_acquire(commands[i].resources)) {
                char err[128];
                snprintf(
                    err, sizeof(err), "{\"id\":%u,\"error\":\"resource_busy\"}\n", request_id);
                rpc_send_json(serial, err);
                return;
            }

            commands[i].handler(request_id, json, serial);
            return;
        }
    }

    // Неизвестная команда
    char err[128];
    snprintf(err, sizeof(err), "{\"id\":%u,\"error\":\"unknown_command\"}\n", request_id);
    rpc_send_json(serial, err);
}

/* === RX CALLBACK (вызывается на каждый байт) === */
static void rx_callback(FuriHalSerialHandle* handle, uint8_t data, void* ctx) {
    UNUSED(ctx);
    if(rx_pos >= sizeof(rx_buffer) - 1) rx_pos = 0; // переполнение — сброс

    rx_buffer[rx_pos++] = (char)data;

    // Команда заканчивается на \n (или просто } для JSON)
    if(data == '\n' || data == '}') {
        rx_buffer[rx_pos] = '\0';
        parse_and_dispatch(rx_buffer, handle);
        rx_pos = 0;
    }
}

/* === ОБРАБОТЧИКИ (Handlers) === */
static void ping_handler(uint32_t id, const char* args_json, FuriHalSerialHandle* serial) {
    UNUSED(args_json);
    char resp[128];
    snprintf(resp, sizeof(resp), "{\"id\":%u,\"status\":\"ok\",\"data\":{\"pong\":true}}\n", id);
    rpc_send_json(serial, resp);
}

static void
    ble_scan_start_handler(uint32_t id, const char* args_json, FuriHalSerialHandle* serial) {
    UNUSED(args_json);
    if(!can_acquire(RESOURCE_BLE)) return;

    acquire(RESOURCE_BLE);

    uint32_t stream_id = next_stream_id++;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(!active_streams[i].active) {
            active_streams[i].id = stream_id;
            active_streams[i].resources = RESOURCE_BLE;
            active_streams[i].active = true;
            break;
        }
    }

    char resp[128];
    snprintf(resp, sizeof(resp), "{\"id\":%u,\"stream\":%u}\n", id, stream_id);
    rpc_send_json(serial, resp);

    FURI_LOG_I("RPC", "BLE scan stream started: %u", stream_id);
    // Здесь в реальном коде запустился бы BLE scan + callback
    // Для демо — просто логируем
}

static void stream_close_handler(uint32_t id, const char* args_json, FuriHalSerialHandle* serial) {
    uint32_t stream_id = 0;
    const char* s_pos = strstr(args_json, "\"stream\":");
    if(s_pos) sscanf(s_pos + 9, "%u", &stream_id);

    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active && active_streams[i].id == stream_id) {
            release(active_streams[i].resources);
            active_streams[i].active = false;

            char resp[128];
            snprintf(resp, sizeof(resp), "{\"id\":%u,\"status\":\"ok\"}\n", id);
            rpc_send_json(serial, resp);

            FURI_LOG_I("RPC", "Stream %u closed", stream_id);
            return;
        }
    }

    char err[128];
    snprintf(err, sizeof(err), "{\"id\":%u,\"error\":\"stream_not_found\"}\n", id);
    rpc_send_json(serial, err);
}

/* === GUI (статус даемона) === */
static uint32_t stream_count = 0; // для отображения

static void draw_callback(Canvas* canvas, void* ctx) {
    UNUSED(ctx);
    canvas_set_font(canvas, FontPrimary);
    canvas_draw_str_aligned(canvas, 64, 20, AlignCenter, AlignTop, "RPC Daemon");
    canvas_set_font(canvas, FontSecondary);
    canvas_draw_str_aligned(canvas, 64, 40, AlignCenter, AlignTop, "JSON RPC running");
    canvas_draw_str_aligned(canvas, 64, 55, AlignCenter, AlignTop, "Connect via UART");
    char buf[32];
    snprintf(buf, sizeof(buf), "Active streams: %lu", stream_count);
    canvas_draw_str_aligned(canvas, 64, 70, AlignCenter, AlignTop, buf);
    canvas_draw_str_aligned(canvas, 64, 85, AlignCenter, AlignTop, "Press BACK to exit");
}

static bool input_callback(InputEvent* event, void* ctx) {
    ViewDispatcher* view_dispatcher = ctx;
    if(event->type == InputTypeShort && event->key == InputKeyBack) {
        view_dispatcher_stop(view_dispatcher);
        return true;
    }
    return false;
}

/* === ОБНОВЛЕНИЕ СЧЁТЧИКА СТРИМОВ (таймер) === */
static void update_stream_count_timer(void* ctx) {
    UNUSED(ctx);
    stream_count = 0;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active) stream_count++;
    }
}

/* === MAIN APP === */
int32_t flipper_zero_rpc_daemon_app(void* p) {
    UNUSED(p);

    FURI_LOG_I("RPC", "Flipper RPC Daemon started");

    // Инициализация стримы
    memset(active_streams, 0, sizeof(active_streams));

    // === SERIAL ===
    rpc_serial = furi_hal_serial_alloc(FuriHalSerialIdUsart, 115200); // или FuriHalSerialIdLpuart
    if(rpc_serial) {
        furi_hal_serial_set_rx_callback(rpc_serial, rx_callback, NULL);
        FURI_LOG_I("RPC", "Serial opened @ 115200");
    } else {
        FURI_LOG_E("RPC", "Failed to open serial!");
    }

    // === GUI ===
    Gui* gui = furi_record_open(RECORD_GUI);
    ViewDispatcher* view_dispatcher = view_dispatcher_alloc();
    View* view = view_alloc();

    view_set_context(view, view_dispatcher);
    view_set_draw_callback(view, draw_callback);
    view_set_input_callback(view, input_callback);

    view_dispatcher_attach_to_gui(view_dispatcher, gui, ViewDispatcherTypeFullscreen);
    view_dispatcher_add_view(view_dispatcher, 0, view);
    view_dispatcher_switch_to_view(view_dispatcher, 0);

    // Таймер обновления счётчика
    FuriTimer* timer = furi_timer_alloc(update_stream_count_timer, FuriTimerTypePeriodic, NULL);
    furi_timer_start(timer, 1000);

    // Запуск главного цикла
    view_dispatcher_run(view_dispatcher);

    // === CLEANUP ===
    furi_timer_free(timer);
    view_dispatcher_remove_view(view_dispatcher, 0);
    view_free(view);
    view_dispatcher_free(view_dispatcher);
    furi_record_close(RECORD_GUI);

    if(rpc_serial) {
        furi_hal_serial_set_rx_callback(rpc_serial, NULL, NULL);
        furi_hal_serial_free(rpc_serial);
    }

    FURI_LOG_I("RPC", "Daemon stopped");
    return 0;
}
