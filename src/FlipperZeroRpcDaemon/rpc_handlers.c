/**
 * rpc_handlers.c — RPC command handler implementations
 *
 * Each handler follows a consistent pattern:
 *   1. Validate arguments (if any).
 *   2. Check stream table capacity (stream_alloc_slot).
 *   3. Acquire resources (dispatcher already confirmed they are free).
 *   4. Initialise hardware and populate the stream slot.
 *   5. Send the stream-opened response via rpc_send_response().
 *
 * Streaming handlers store a teardown() function pointer in the slot.
 * stream_close_by_index() calls teardown() before clearing the slot.
 *
 * Threading notes:
 *   - IR and SubGhz worker callbacks fire on SDK-managed FreeRTOS threads —
 *     snprintf and furi_message_queue_put are both safe there.
 *   - NFC scanner callback fires on the NFC worker thread — same rules apply.
 *   - GPIO EXTI callback fires in interrupt context — ONLY furi_message_queue_put
 *     is safe; no snprintf, no logging.  JSON fragments are pre-composed into
 *     the slot at stream-open time and selected by pointer in the ISR.
 */

#include "rpc_handlers.h"
#include "rpc_response.h"
#include "rpc_stream.h"
#include "rpc_resource.h"
#include "rpc_json.h"
#include "rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_gpio.h>
#include <furi_hal_subghz.h>
#include <infrared_worker.h>
#include <infrared.h>
#include <subghz/subghz_worker.h>
#include <nfc/nfc.h>
#include <nfc/nfc_scanner.h>
#include <nfc/nfc_device.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * Shared helper — open a stream slot
 * ========================================================= */

/**
 * Allocate a stream slot and acquire resources.
 * On failure, sends the appropriate error response and returns -1.
 * On success, fills *stream_id_out and returns the slot index.
 *
 * The dispatcher has already verified resource_can_acquire(), so
 * resource_acquire() here will never fail.
 */
static int
    stream_open(uint32_t id, const char* cmd_name, ResourceMask res, uint32_t* stream_id_out) {
    int slot = stream_alloc_slot();
    if(slot < 0) {
        rpc_send_error(id, "stream_table_full", cmd_name);
        return -1;
    }

    resource_acquire(res);

    uint32_t stream_id = next_stream_id++;
    active_streams[slot].id = stream_id;
    active_streams[slot].resources = res;
    active_streams[slot].active = true;
    active_streams[slot].teardown = NULL; /* caller sets this */

    *stream_id_out = stream_id;
    return slot;
}

/** Send the stream-opened response and log it. */
static void stream_send_opened(uint32_t request_id, uint32_t stream_id, const char* cmd_name) {
    char resp[128];
    snprintf(
        resp, sizeof(resp), "{\"id\":%" PRIu32 ",\"stream\":%" PRIu32 "}\n", request_id, stream_id);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry,
        sizeof(log_entry),
        "#%" PRIu32 " %.14s->s:%" PRIu32,
        request_id,
        cmd_name,
        stream_id);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * ping
 * ========================================================= */

void ping_handler(uint32_t id, const char* json) {
    UNUSED(json);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"pong\":true}}\n",
        id);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " ping -> ok", id);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * ir_receive_start
 * ========================================================= */

static void ir_rx_callback(void* ctx, InfraredWorkerSignal* signal) {
    if(!infrared_worker_signal_is_decoded(signal)) return;

    const InfraredMessage* msg = infrared_worker_get_decoded_signal(signal);

    /* Find the stream id for this IR worker instance */
    uint32_t stream_id = 0;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active && active_streams[i].hw.ir.worker == ctx) {
            stream_id = active_streams[i].id;
            break;
        }
    }
    if(stream_id == 0) return;

    StreamEvent ev;
    ev.stream_id = stream_id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"protocol\":\"%s\",\"address\":%" PRIu32 ",\"command\":%" PRIu32 ",\"repeat\":%s",
        infrared_get_protocol_name(msg->protocol),
        (uint32_t)msg->address,
        (uint32_t)msg->command,
        msg->repeat ? "true" : "false");
    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void ir_teardown(size_t slot_idx) {
    InfraredWorker* worker = active_streams[slot_idx].hw.ir.worker;
    if(worker) {
        infrared_worker_rx_stop(worker);
        infrared_worker_free(worker);
        active_streams[slot_idx].hw.ir.worker = NULL;
    }
}

void ir_receive_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    uint32_t stream_id = 0;
    int slot = stream_open(id, "ir_receive_start", RESOURCE_IR, &stream_id);
    if(slot < 0) return;

    InfraredWorker* worker = infrared_worker_alloc();
    infrared_worker_rx_set_received_signal_callback(worker, ir_rx_callback, worker);
    infrared_worker_rx_start(worker);

    active_streams[slot].hw.ir.worker = worker;
    active_streams[slot].teardown = ir_teardown;

    stream_send_opened(id, stream_id, "ir_receive_start");
    FURI_LOG_I("RPC", "IR receive stream opened id=%" PRIu32, stream_id);
}

/* =========================================================
 * gpio_watch_start
 * ========================================================= */

/* Mapping from label string ("1"–"8") to gpio_ext_* pin symbol.
 * Physical GPIO header pin numbers on the Flipper Zero expansion connector. */
typedef struct {
    const char* label;
    const GpioPin* pin;
} GpioPinEntry;

static const GpioPinEntry gpio_pin_table[] = {
    {"1", &gpio_ext_pc0},
    {"2", &gpio_ext_pc1},
    {"3", &gpio_ext_pc3},
    {"4", &gpio_ext_pb2},
    {"5", &gpio_ext_pb3},
    {"6", &gpio_ext_pa4},
    {"7", &gpio_ext_pa6},
    {"8", &gpio_ext_pa7},
    {NULL, NULL},
};

static const GpioPin* gpio_pin_from_label(const char* label) {
    for(size_t i = 0; gpio_pin_table[i].label != NULL; i++) {
        if(strcmp(gpio_pin_table[i].label, label) == 0) {
            return gpio_pin_table[i].pin;
        }
    }
    return NULL;
}

/* ISR-context callback — no snprintf, no logging, only furi_message_queue_put. */
static void gpio_exti_callback(void* ctx) {
    RpcStream* slot = (RpcStream*)ctx;

    /* Read current level to determine which pre-composed fragment to use. */
    bool level = furi_hal_gpio_read(slot->hw.gpio.pin);

    StreamEvent ev;
    ev.stream_id = slot->id;
    /* Copy the pre-composed fragment (no snprintf in ISR). */
    const char* frag = level ? slot->hw.gpio.frag_high : slot->hw.gpio.frag_low;
    strncpy(ev.json_fragment, frag, STREAM_FRAG_MAX - 1);
    ev.json_fragment[STREAM_FRAG_MAX - 1] = '\0';

    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void gpio_teardown(size_t slot_idx) {
    const GpioPin* pin = active_streams[slot_idx].hw.gpio.pin;
    if(pin) {
        furi_hal_gpio_remove_int_callback(pin);
        active_streams[slot_idx].hw.gpio.pin = NULL;
    }
}

void gpio_watch_start_handler(uint32_t id, const char* json) {
    char label[8] = {0};
    if(!json_extract_string(json, "pin", label, sizeof(label))) {
        rpc_send_error(id, "missing_pin", "gpio_watch_start");
        return;
    }

    const GpioPin* pin = gpio_pin_from_label(label);
    if(!pin) {
        rpc_send_error(id, "invalid_pin", "gpio_watch_start");
        return;
    }

    uint32_t stream_id = 0;
    /* GPIO needs no exclusive resource — multiple GPIO streams are allowed. */
    int slot = stream_open(id, "gpio_watch_start", 0, &stream_id);
    if(slot < 0) return;

    /* Pre-compose both fragments at stream-open time (main thread, snprintf safe). */
    snprintf(
        active_streams[slot].hw.gpio.frag_high,
        GPIO_FRAG_MAX,
        "\"pin\":\"%s\",\"level\":true",
        label);
    snprintf(
        active_streams[slot].hw.gpio.frag_low,
        GPIO_FRAG_MAX,
        "\"pin\":\"%s\",\"level\":false",
        label);
    active_streams[slot].hw.gpio.pin = pin;
    active_streams[slot].teardown = gpio_teardown;

    /* Configure pin as input with pull-up, then attach EXTI interrupt. */
    furi_hal_gpio_init(pin, GpioModeInterruptRiseFall, GpioPullUp, GpioSpeedLow);
    furi_hal_gpio_add_int_callback(pin, gpio_exti_callback, &active_streams[slot]);

    stream_send_opened(id, stream_id, "gpio_watch_start");
    FURI_LOG_I("RPC", "GPIO watch stream opened pin=%s id=%" PRIu32, label, stream_id);
}

/* =========================================================
 * subghz_rx_start
 * ========================================================= */

/* SubGhzWorkerPairCallback: void(*)(void* context, bool level, uint32_t duration) */
static void subghz_rx_callback(void* ctx, bool level, uint32_t duration_us) {
    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    StreamEvent ev;
    ev.stream_id = stream_id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"level\":%s,\"duration_us\":%" PRIu32,
        level ? "true" : "false",
        duration_us);
    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void subghz_teardown(size_t slot_idx) {
    SubGhzWorker* worker = active_streams[slot_idx].hw.subghz.worker;
    if(worker) {
        subghz_worker_stop(worker);
        subghz_worker_free(worker);
        active_streams[slot_idx].hw.subghz.worker = NULL;
    }
    furi_hal_subghz_sleep();
}

void subghz_rx_start_handler(uint32_t id, const char* json) {
    /* Optional frequency argument; default 433.92 MHz */
    uint32_t freq = 433920000;
    json_extract_uint32(json, "freq", &freq);

    uint32_t stream_id = 0;
    int slot = stream_open(id, "subghz_rx_start", RESOURCE_SUBGHZ, &stream_id);
    if(slot < 0) return;

    /* Set up the radio: reset → frequency + path → RX mode.
     * furi_hal_subghz_init() runs at system boot and sets OOK defaults;
     * reset + set_frequency_and_path puts the CC1101 into RX at our freq. */
    furi_hal_subghz_reset();
    furi_hal_subghz_set_frequency_and_path(freq);
    furi_hal_subghz_rx();

    SubGhzWorker* worker = subghz_worker_alloc();
    subghz_worker_set_context(worker, (void*)(uintptr_t)stream_id);
    subghz_worker_set_pair_callback(worker, subghz_rx_callback);
    subghz_worker_start(worker);

    active_streams[slot].hw.subghz.worker = worker;
    active_streams[slot].teardown = subghz_teardown;

    stream_send_opened(id, stream_id, "subghz_rx_start");
    FURI_LOG_I("RPC", "SubGhz RX stream opened freq=%" PRIu32 " id=%" PRIu32, freq, stream_id);
}

/* =========================================================
 * nfc_scan_start
 * ========================================================= */

static void nfc_scanner_callback(NfcScannerEvent event, void* ctx) {
    if(event.type != NfcScannerEventTypeDetected) return;
    if(event.data.protocol_num == 0) return;

    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    StreamEvent ev;
    ev.stream_id = stream_id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"protocol\":\"%s\"",
        nfc_device_get_protocol_name(event.data.protocols[0]));
    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void nfc_teardown(size_t slot_idx) {
    NfcScanner* scanner = active_streams[slot_idx].hw.nfc.scanner;
    Nfc* nfc = active_streams[slot_idx].hw.nfc.nfc;
    if(scanner) {
        nfc_scanner_stop(scanner);
        nfc_scanner_free(scanner);
        active_streams[slot_idx].hw.nfc.scanner = NULL;
    }
    if(nfc) {
        nfc_free(nfc);
        active_streams[slot_idx].hw.nfc.nfc = NULL;
    }
}

void nfc_scan_start_handler(uint32_t id, const char* json) {
    UNUSED(json);

    uint32_t stream_id = 0;
    int slot = stream_open(id, "nfc_scan_start", RESOURCE_NFC, &stream_id);
    if(slot < 0) return;

    Nfc* nfc = nfc_alloc();
    NfcScanner* scanner = nfc_scanner_alloc(nfc);
    nfc_scanner_start(scanner, nfc_scanner_callback, (void*)(uintptr_t)stream_id);

    active_streams[slot].hw.nfc.nfc = nfc;
    active_streams[slot].hw.nfc.scanner = scanner;
    active_streams[slot].teardown = nfc_teardown;

    stream_send_opened(id, stream_id, "nfc_scan_start");
    FURI_LOG_I("RPC", "NFC scan stream opened id=%" PRIu32, stream_id);
}

/* =========================================================
 * stream_close
 * ========================================================= */

void stream_close_handler(uint32_t id, const char* json) {
    uint32_t stream_id = 0;
    if(!json_extract_uint32(json, "stream", &stream_id)) {
        rpc_send_error(id, "missing_stream_id", "stream_close");
        return;
    }

    int slot = stream_find_by_id(stream_id);
    if(slot < 0) {
        rpc_send_error(id, "stream_not_found", "stream_close");
        return;
    }

    stream_close_by_index((size_t)slot);
    FURI_LOG_I("RPC", "stream %" PRIu32 " closed", stream_id);

    rpc_send_ok(id, "stream_close");
}
