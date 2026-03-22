/**
 * rpc_response.c — RPC response formatting helpers implementation (Wire Format V3)
 */

#include "rpc_response.h"
#include "rpc_metrics.h"
#include "rpc_transport.h"
#include "rpc_cmd_log.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void rpc_send_error(uint32_t id, const char* error_code, const char* cmd_name) {
    if(metrics_enabled) g_metrics.t_handler_done = furi_get_tick();
    char buf[160]; /* 128 base + 32 headroom for _m fragment */
    size_t pos = (size_t)snprintf(
        buf,
        sizeof(buf),
        "{\"t\":0,\"i\":%" PRIu32 ",\"e\":\"%s\"",
        id,
        error_code);

    if(metrics_enabled && pos < sizeof(buf)) {
        pos = metrics_append(buf, sizeof(buf), pos);
    }

    /* Close the envelope */
    if(pos + 3 <= sizeof(buf)) {
        buf[pos]     = '}';
        buf[pos + 1] = '\n';
        buf[pos + 2] = '\0';
    } else {
        buf[sizeof(buf) - 3] = '}';
        buf[sizeof(buf) - 2] = '\n';
        buf[sizeof(buf) - 1] = '\0';
    }

    cdc_send(buf);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry, sizeof(log_entry), "#%" PRIu32 " %.12s -> err:%.10s", id, cmd_name, error_code);
    cmd_log_push(log_entry);
}

void rpc_send_ok(uint32_t id, const char* cmd_name) {
    if(metrics_enabled) g_metrics.t_handler_done = furi_get_tick();
    char buf[160]; /* 128 base + 32 headroom for _m fragment */
    size_t pos =
        (size_t)snprintf(buf, sizeof(buf), "{\"t\":0,\"i\":%" PRIu32 "", id);

    if(metrics_enabled && pos < sizeof(buf)) {
        pos = metrics_append(buf, sizeof(buf), pos);
    }

    /* Close the envelope */
    if(pos + 3 <= sizeof(buf)) {
        buf[pos]     = '}';
        buf[pos + 1] = '\n';
        buf[pos + 2] = '\0';
    } else {
        buf[sizeof(buf) - 3] = '}';
        buf[sizeof(buf) - 2] = '\n';
        buf[sizeof(buf) - 1] = '\0';
    }

    cdc_send(buf);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(log_entry, sizeof(log_entry), "#%" PRIu32 " %.20s -> ok", id, cmd_name);
    cmd_log_push(log_entry);
}

void rpc_send_data_response(uint32_t id, const char* payload_json, const char* log_entry) {
    if(metrics_enabled) g_metrics.t_handler_done = furi_get_tick();
    /* Header: {"t":0,"i":<id>,"p":  + payload + }   (no \n yet)
     * Max header overhead: ~20 bytes + PRIu32 (10 digits) = ~30 bytes
     * Metrics fragment: ,"_m":{...} ≤ 72 bytes ("_m" + 5 x PRIu32 + labels)
     * Extra budget: 32 bytes for _m + closing } + trailing \n */
    size_t payload_len = strlen(payload_json);
    size_t buf_size = payload_len + 64 + 32; /* +32 for metrics + closing brace */

    /* Use stack buffer for small payloads; heap for large ones. */
    char stack_buf[288]; /* 256 base + 32 metrics headroom */
    char* buf = NULL;
    char* heap_buf = NULL;

    if(buf_size <= sizeof(stack_buf)) {
        buf = stack_buf;
    } else {
        heap_buf = malloc(buf_size);
        if(!heap_buf) {
            /* Out of memory — send a generic error instead of silently dropping */
            rpc_send_error(id, "out_of_memory", "rpc_send_data_response");
            return;
        }
        buf = heap_buf;
    }

    size_t pos = (size_t)snprintf(
        buf,
        buf_size,
        "{\"t\":0,\"i\":%" PRIu32 ",\"p\":%s",
        id,
        payload_json);

    if(metrics_enabled && pos < buf_size) {
        pos = metrics_append(buf, buf_size, pos);
    }

    /* Close the envelope */
    if(pos + 3 <= buf_size) {
        buf[pos]     = '}';
        buf[pos + 1] = '\n';
        buf[pos + 2] = '\0';
    } else {
        buf[buf_size - 3] = '}';
        buf[buf_size - 2] = '\n';
        buf[buf_size - 1] = '\0';
    }

    cdc_send(buf);
    cmd_log_push(log_entry);

    if(heap_buf) free(heap_buf);
}
