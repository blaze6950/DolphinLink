/**
 * configure.c — configure command handler implementation
 *
 * Propagates host-side configuration to the daemon.  Supports heartbeat
 * timing (TX idle interval and RX timeout) and an optional LED connection
 * indicator.  The host sends this command during session startup so the
 * daemon aligns its keep-alive behaviour and LED feedback with the host's
 * settings.
 *
 * Wire protocol:
 *   Request:  {"c":N,"i":N[,"hb":<u32>][,"to":<u32>][,"led":{"r":<u8>,"g":<u8>,"b":<u8>}]}
 *   Response: {"t":0,"i":N,"p":{"hb":<u32>,"to":<u32>[,"led":{"r":<u8>,"g":<u8>,"b":<u8>}]}}
 *   Errors:   invalid_config
 *
 * Threading: main thread (FuriEventLoop).
 */

#include "configure.h"
#include "../../core/rpc_transport.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"
#include "../../core/rpc_gui.h"

#include <furi.h>
#include <stdio.h>
#include <inttypes.h>
#include <string.h>

void configure_handler(uint32_t id, const char* json, size_t offset) {
    /* Read optional heartbeat arguments — absent fields keep current value. */
    uint32_t hb_ms = heartbeat_tx_idle_ms;
    uint32_t to_ms = heartbeat_rx_timeout_ms;

    JsonValue val;
    if(json_find(json, "hb", offset, &val)) { json_value_uint32(&val, &hb_ms); offset = val.offset; }
    if(json_find(json, "to", offset, &val)) { json_value_uint32(&val, &to_ms); offset = val.offset; }

    if(!heartbeat_apply_config(hb_ms, to_ms)) {
        char log_entry[CMD_LOG_LINE_LEN];
        snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " cfg:invalid_config", id);
        rpc_send_error(id, "invalid_config", log_entry);
        return;
    }

    /* Parse optional "led" object — {"r":<u8>,"g":<u8>,"b":<u8>}.
     * "r", "g", "b" are unique in this command's payload; search from
     * running offset. */
    uint32_t r = led_indicator_r, g = led_indicator_g, b = led_indicator_b;
    bool has_r = json_find(json, "r", offset, &val);
    if(has_r) { json_value_uint32(&val, &r); offset = val.offset; }
    bool has_g = json_find(json, "g", offset, &val);
    if(has_g) { json_value_uint32(&val, &g); offset = val.offset; }
    bool has_b = json_find(json, "b", offset, &val);
    if(has_b) { json_value_uint32(&val, &b); offset = val.offset; }
    (void)offset;

    if(has_r || has_g || has_b) {
        if(r > 255) r = 255;
        if(g > 255) g = 255;
        if(b > 255) b = 255;
        led_indicator_enabled = true;
        led_indicator_r = (uint8_t)r;
        led_indicator_g = (uint8_t)g;
        led_indicator_b = (uint8_t)b;
        led_indicator_apply(host_connected);
        FURI_LOG_I("RPC", "LED indicator: r=%" PRIu32 " g=%" PRIu32 " b=%" PRIu32, r, g, b);
    }

    /* Build response — echo effective values; include "led" only when configured. */
    char payload[128];
    if(led_indicator_enabled) {
        snprintf(
            payload,
            sizeof(payload),
            "{\"hb\":%" PRIu32 ",\"to\":%" PRIu32
            ",\"led\":{\"r\":%" PRIu8 ",\"g\":%" PRIu8 ",\"b\":%" PRIu8 "}}",
            heartbeat_tx_idle_ms,
            heartbeat_rx_timeout_ms,
            led_indicator_r,
            led_indicator_g,
            led_indicator_b);
    } else {
        snprintf(
            payload,
            sizeof(payload),
            "{\"hb\":%" PRIu32 ",\"to\":%" PRIu32 "}",
            heartbeat_tx_idle_ms,
            heartbeat_rx_timeout_ms);
    }

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " cfg:ok", id);

    rpc_send_data_response(id, payload, log_entry);
}
