/**
 * rpc_handlers_gpio.c — GPIO / ADC RPC handler implementations
 *
 * gpio_read / gpio_write — simple single-shot digital I/O
 * adc_read              — single sample from the ADC on a GPIO pin
 * gpio_set_5v           — enable / disable the 5 V header rail
 * gpio_watch_start      — streaming edge events (migrated from rpc_handlers.c)
 *
 * GPIO EXTI ISR constraint: gpio_exti_callback fires in interrupt context.
 * Only furi_message_queue_put() is safe there.  JSON fragments are
 * pre-composed at stream-open time.
 */

#include "rpc_handlers_gpio.h"
#include "rpc_response.h"
#include "rpc_stream.h"
#include "rpc_resource.h"
#include "rpc_json.h"
#include "rpc_cmd_log.h"

#include <furi.h>
#include <furi_hal_gpio.h>
#include <furi_hal_adc.h>
#include <furi_hal_power.h>
#include <stdio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * Shared pin table (also used by gpio_watch_start)
 * ========================================================= */

typedef struct {
    const char* label;
    const GpioPin* pin;
    FuriHalAdcChannel adc_channel; /**< FuriHalAdcChannelNone if not ADC-capable */
} GpioPinEntry;

static const GpioPinEntry gpio_pin_table[] = {
    /* label, pin,          adc_channel */
    {"1", &gpio_ext_pc0, FuriHalAdcChannel10},
    {"2", &gpio_ext_pc1, FuriHalAdcChannel11},
    {"3", &gpio_ext_pc3, FuriHalAdcChannel4},
    {"4", &gpio_ext_pb2, FuriHalAdcChannelNone},
    {"5", &gpio_ext_pb3, FuriHalAdcChannelNone},
    {"6", &gpio_ext_pa4, FuriHalAdcChannel9},
    {"7", &gpio_ext_pa6, FuriHalAdcChannel3},
    {"8", &gpio_ext_pa7, FuriHalAdcChannelNone},
    {NULL, NULL, FuriHalAdcChannelNone},
};

static const GpioPinEntry* gpio_pin_entry_from_label(const char* label) {
    for(size_t i = 0; gpio_pin_table[i].label != NULL; i++) {
        if(strcmp(gpio_pin_table[i].label, label) == 0) {
            return &gpio_pin_table[i];
        }
    }
    return NULL;
}

/* =========================================================
 * gpio_read
 * ========================================================= */

void gpio_read_handler(uint32_t id, const char* json) {
    char label[8] = {0};
    if(!json_extract_string(json, "pin", label, sizeof(label))) {
        rpc_send_error(id, "missing_pin", "gpio_read");
        return;
    }
    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry) {
        rpc_send_error(id, "invalid_pin", "gpio_read");
        return;
    }

    furi_hal_gpio_init(entry->pin, GpioModeInput, GpioPullUp, GpioSpeedLow);
    bool level = furi_hal_gpio_read(entry->pin);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"level\":%s}}\n",
        id,
        level ? "true" : "false");

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " gpio_read pin=%s -> %d", id, label, (int)level);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * gpio_write
 * ========================================================= */

void gpio_write_handler(uint32_t id, const char* json) {
    char label[8] = {0};
    if(!json_extract_string(json, "pin", label, sizeof(label))) {
        rpc_send_error(id, "missing_pin", "gpio_write");
        return;
    }
    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry) {
        rpc_send_error(id, "invalid_pin", "gpio_write");
        return;
    }

    bool level = false;
    if(!json_extract_bool(json, "level", &level)) {
        rpc_send_error(id, "missing_level", "gpio_write");
        return;
    }

    furi_hal_gpio_init(entry->pin, GpioModeOutputPushPull, GpioPullNo, GpioSpeedLow);
    furi_hal_gpio_write(entry->pin, level);

    rpc_send_ok(id, "gpio_write");
}

/* =========================================================
 * adc_read
 * ========================================================= */

void adc_read_handler(uint32_t id, const char* json) {
    char label[8] = {0};
    if(!json_extract_string(json, "pin", label, sizeof(label))) {
        rpc_send_error(id, "missing_pin", "adc_read");
        return;
    }
    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry || entry->adc_channel == FuriHalAdcChannelNone) {
        rpc_send_error(id, "invalid_pin", "adc_read");
        return;
    }

    FuriHalAdcHandle* adc = furi_hal_adc_acquire();
    furi_hal_adc_configure(adc);
    uint16_t raw = furi_hal_adc_read(adc, entry->adc_channel);
    float voltage = furi_hal_adc_convert_to_voltage(adc, raw);
    furi_hal_adc_release(adc);

    /* Encode voltage as millivolts integer to avoid %f */
    int32_t mv = (int32_t)(voltage * 1000.0f);

    char resp[128];
    snprintf(
        resp,
        sizeof(resp),
        "{\"id\":%" PRIu32 ",\"status\":\"ok\",\"data\":{\"raw\":%" PRIu16 ",\"mv\":%" PRIi32
        "}}\n",
        id,
        raw,
        mv);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " adc_read pin=%s -> %" PRIi32 "mv", id, label, mv);

    rpc_send_response(resp, log_entry);
}

/* =========================================================
 * gpio_set_5v
 * ========================================================= */

void gpio_set_5v_handler(uint32_t id, const char* json) {
    bool enable = false;
    if(!json_extract_bool(json, "enable", &enable)) {
        rpc_send_error(id, "missing_enable", "gpio_set_5v");
        return;
    }

    if(enable) {
        furi_hal_power_enable_otg();
    } else {
        furi_hal_power_disable_otg();
    }

    rpc_send_ok(id, "gpio_set_5v");
}

/* =========================================================
 * gpio_watch_start (stream)
 * ========================================================= */

/** GPIO pre-composed fragment buffer length. */
#define GPIO_FRAG_MAX 48

/* ISR-context callback — no snprintf, no logging. */
static void gpio_exti_callback(void* ctx) {
    RpcStream* slot = (RpcStream*)ctx;

    bool level = furi_hal_gpio_read(slot->hw.gpio.pin);

    StreamEvent ev;
    ev.stream_id = slot->id;
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

/** Allocate a stream slot, acquire resources, populate slot fields.
 *  Returns the slot index or -1 on failure (error already sent). */
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
    active_streams[slot].teardown = NULL;
    *stream_id_out = stream_id;
    return slot;
}

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

void gpio_watch_start_handler(uint32_t id, const char* json) {
    char label[8] = {0};
    if(!json_extract_string(json, "pin", label, sizeof(label))) {
        rpc_send_error(id, "missing_pin", "gpio_watch_start");
        return;
    }

    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry) {
        rpc_send_error(id, "invalid_pin", "gpio_watch_start");
        return;
    }

    uint32_t stream_id = 0;
    int slot = stream_open(id, "gpio_watch_start", 0, &stream_id);
    if(slot < 0) return;

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
    active_streams[slot].hw.gpio.pin = entry->pin;
    active_streams[slot].teardown = gpio_teardown;

    furi_hal_gpio_init(entry->pin, GpioModeInterruptRiseFall, GpioPullUp, GpioSpeedLow);
    furi_hal_gpio_add_int_callback(entry->pin, gpio_exti_callback, &active_streams[slot]);

    stream_send_opened(id, stream_id, "gpio_watch_start");
    FURI_LOG_I("RPC", "GPIO watch stream opened pin=%s id=%" PRIu32, label, stream_id);
}
