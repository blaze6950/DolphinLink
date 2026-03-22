/**
 * gpio_watch_start.c — gpio_watch_start RPC handler implementation
 *
 * Opens a streaming session that emits an event on every rising or falling
 * edge of the named GPIO pin.
 *
 * Wire format (request):
 *   {"c":16,"i":N,"p":<GpioPin int 1-8>}
 *
 * Wire format (stream opened):
 *   {"t":0,"i":N,"p":{"s":M}}
 *
 * Wire format (stream event — emitted on each edge):
 *   {"t":1,"i":M,"p":{"p":<int>,"lv":1}} or {"t":1,"i":M,"p":{"p":<int>,"lv":0}}
 *
 * Error codes:
 *   missing_pin       — "pin" field absent
 *   invalid_pin       — label not in pin table
 *   stream_table_full — no free stream slots
 *
 * ISR constraint
 * --------------
 * gpio_exti_callback runs in EXTI interrupt context.  It must never call
 * snprintf, FURI_LOG_*, furi_hal_cdc_send, or any blocking API.
 * All JSON fragments are pre-composed at stream-open time into the slot's
 * hw.gpio.frag_high / frag_low buffers; the ISR merely copies the correct
 * pre-composed string into the StreamEvent.
 */

#include "gpio_watch_start.h"
#include "gpio_pins.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <furi_hal_gpio.h>
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * EXTI callback — interrupt context only
 * ========================================================= */

/** Called on every GPIO edge.  ctx is the RpcStream slot pointer. */
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

/* =========================================================
 * Teardown — called from main thread when the stream is closed
 * ========================================================= */

static void gpio_teardown(size_t slot_idx) {
    const GpioPin* pin = active_streams[slot_idx].hw.gpio.pin;
    if(pin) {
        furi_hal_gpio_remove_int_callback(pin);
        active_streams[slot_idx].hw.gpio.pin = NULL;
    }
}

/* =========================================================
 * Handler
 * ========================================================= */

void gpio_watch_start_handler(uint32_t id, const char* json) {
    uint32_t pin_num = 0;
    if(!json_extract_uint32(json, "p", &pin_num) || pin_num < 1 || pin_num > 8) {
        rpc_send_error(id, "missing_pin", "gpio_watch_start");
        return;
    }

    /* Map integer wire value to label string ("1"–"8") */
    char label[4];
    snprintf(label, sizeof(label), "%" PRIu32, pin_num);

    const GpioPinEntry* entry = gpio_pin_entry_from_label(label);
    if(!entry) {
        rpc_send_error(id, "invalid_pin", "gpio_watch_start");
        return;
    }

    uint32_t stream_id = 0;
    int slot = stream_open(id, "gpio_watch_start", 0, &stream_id);
    if(slot < 0) return;

    /* V1: pin as integer, level as 1/0 */
    snprintf(
        active_streams[slot].hw.gpio.frag_high,
        GPIO_FRAG_MAX,
        "\"p\":%" PRIu32 ",\"lv\":1",
        pin_num);
    snprintf(
        active_streams[slot].hw.gpio.frag_low,
        GPIO_FRAG_MAX,
        "\"p\":%" PRIu32 ",\"lv\":0",
        pin_num);
    active_streams[slot].hw.gpio.pin = entry->pin;
    active_streams[slot].teardown = gpio_teardown;

    furi_hal_gpio_init(entry->pin, GpioModeInterruptRiseFall, GpioPullUp, GpioSpeedLow);
    furi_hal_gpio_add_int_callback(entry->pin, gpio_exti_callback, &active_streams[slot]);

    stream_send_opened(id, stream_id, "gpio_watch_start");
    FURI_LOG_I("RPC", "GPIO watch stream opened pin=%" PRIu32 " id=%" PRIu32, pin_num, stream_id);
}
