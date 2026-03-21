/**
 * configure.c — configure command handler implementation
 *
 * Propagates host-side configuration to the daemon.  Currently supports
 * heartbeat timing (TX idle interval and RX timeout).  The host sends this
 * command during session startup so the daemon aligns its keep-alive
 * behaviour with the host's HeartbeatTransport settings.
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"configure","heartbeat_ms":<u32>,"timeout_ms":<u32>}
 *   Response: {"t":0,"i":N,"p":{"heartbeat_ms":<u32>,"timeout_ms":<u32>}}
 *   Errors:   invalid_config
 *
 * Threading: main thread (FuriEventLoop).
 */

#include "configure.h"
#include "../../core/rpc_transport.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"
#include "../../core/rpc_cmd_log.h"

#include <furi.h>
#include <stdio.h>
#include <inttypes.h>

void configure_handler(uint32_t id, const char* json) {
    /* Read optional arguments.  If a field is absent the daemon keeps its
     * current value — the cursor variants allow partial presence. */
    uint32_t hb_ms = heartbeat_tx_idle_ms;
    uint32_t to_ms = heartbeat_rx_timeout_ms;

    const char* cursor = json;
    json_extract_uint32_at(json, &cursor, "heartbeat_ms", &hb_ms);
    json_extract_uint32_at(json, &cursor, "timeout_ms", &to_ms);

    if(!heartbeat_apply_config(hb_ms, to_ms)) {
        char log_entry[CMD_LOG_LINE_LEN];
        snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " cfg:invalid_config", id);
        rpc_send_error(id, "invalid_config", log_entry);
        return;
    }

    /* Echo back the effective values so the host can confirm acceptance. */
    char payload[64];
    snprintf(
        payload,
        sizeof(payload),
        "{\"heartbeat_ms\":%" PRIu32 ",\"timeout_ms\":%" PRIu32 "}",
        heartbeat_tx_idle_ms,
        heartbeat_rx_timeout_ms);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " cfg:ok", id);

    rpc_send_data_response(id, payload, log_entry);
}
