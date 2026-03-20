/**
 * daemon_info.c — daemon_info command handler implementation
 *
 * Returns a fixed descriptor block: daemon name, protocol version, and the
 * full list of command names registered in rpc_dispatch.c.  The host uses
 * this response for capability negotiation (detect whether a specific command
 * is supported before calling it).
 *
 * The command list is maintained manually here in parallel with the dispatch
 * table.  Keep them in sync when adding or removing commands.
 */

#include "daemon_info.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <stdio.h>
#include <inttypes.h>

/** Names of every command registered in rpc_dispatch.c, in the same order. */
static const char* const SUPPORTED_COMMANDS[] = {
    /* Core */
    "ping",
    "stream_close",
    /* System */
    "device_info",
    "daemon_info",
    "daemon_stop",
    "power_info",
    "datetime_get",
    "datetime_set",
    "region_info",
    "frequency_is_allowed",
    "reboot",
    /* GPIO */
    "gpio_read",
    "gpio_write",
    "adc_read",
    "gpio_set_5v",
    "gpio_watch_start",
    /* Infrared */
    "ir_tx",
    "ir_tx_raw",
    "ir_receive_start",
    /* Sub-GHz */
    "subghz_tx",
    "subghz_get_rssi",
    "subghz_rx_start",
    /* NFC */
    "nfc_scan_start",
    /* Notifications */
    "led_set",
    "led_set_rgb",
    "vibro",
    "speaker_start",
    "speaker_stop",
    "backlight",
    /* Storage */
    "storage_info",
    "storage_list",
    "storage_read",
    "storage_write",
    "storage_mkdir",
    "storage_remove",
    "storage_stat",
    /* LF RFID */
    "lfrfid_read_start",
    /* iButton */
    "ibutton_read_start",
    /* Input */
    "input_listen_start",
    /* UI canvas */
    "ui_screen_acquire",
    "ui_screen_release",
    "ui_draw_str",
    "ui_draw_rect",
    "ui_draw_line",
    "ui_flush",
};

#define SUPPORTED_COMMANDS_COUNT \
    (sizeof(SUPPORTED_COMMANDS) / sizeof(SUPPORTED_COMMANDS[0]))

void daemon_info_handler(uint32_t id, const char* json) {
    UNUSED(json);

    /*
     * Build the response in a single buffer.  Capacity breakdown:
     *   Header:        ~50 bytes
     *   commands array: SUPPORTED_COMMANDS_COUNT * ~30 bytes worst-case ≈ 1400
     *   Footer:         10 bytes
     *   Total budget:  1536 bytes (safe).
     */
    char resp[1536];
    int pos = 0;

    pos += snprintf(
        resp + pos,
        (int)sizeof(resp) - pos,
        "{\"id\":%" PRIu32
        ",\"status\":\"ok\",\"data\":"
        "{\"name\":\"flipper_zero_rpc_daemon\","
        "\"version\":%d,"
        "\"commands\":[",
        id,
        DAEMON_PROTOCOL_VERSION);

    for(size_t i = 0; i < SUPPORTED_COMMANDS_COUNT; i++) {
        bool last = (i == SUPPORTED_COMMANDS_COUNT - 1);
        pos += snprintf(
            resp + pos,
            (int)sizeof(resp) - pos,
            "\"%s\"%s",
            SUPPORTED_COMMANDS[i],
            last ? "" : ",");
    }

    pos += snprintf(resp + pos, (int)sizeof(resp) - pos, "]}}\n");
    UNUSED(pos);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry,
        sizeof(log_entry),
        "#%" PRIu32 " daemon_info -> ok (%zu cmds)",
        id,
        SUPPORTED_COMMANDS_COUNT);

    rpc_send_response(resp, log_entry);
}
