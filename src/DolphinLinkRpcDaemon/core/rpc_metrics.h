/**
 * rpc_metrics.h — Per-request timing metrics
 *
 * When metrics_enabled is true (set via the "dx" field in the configure
 * command), the dispatcher and response helpers cooperate to measure elapsed
 * time for each phase of a request and append a "_m" object to every "t":0
 * response envelope:
 *
 *   {"t":0,"i":<id>[,"p":<payload>],"_m":{"pr":<u32>,"dp":<u32>,"ex":<u32>,"sr":<u32>,"tt":<u32>}}
 *
 * Phase breakdown:
 *   pr  parse    — JSON scanning to extract "c" (cmd_id) and "i" (request_id)
 *   dp  dispatch — bounds-check, COMMAND_NAMES lookup, resource pre-check
 *   ex  execute  — handler invocation (arg parsing + hardware work + payload build)
 *   sr  serialize— response envelope formatting in rpc_send_*() helpers
 *   tt  total    — entry to rpc_dispatch() through cdc_send() (end-to-end)
 *
 * All values are in milliseconds (furi_get_tick() resolution).
 *
 * When metrics_enabled is false (default) the struct is not written and
 * furi_get_tick() is never called — zero overhead on the hot path.
 *
 * Threading: all access is on the main (event-loop) thread only.  No locking
 * is needed because the dispatcher is strictly single-threaded.
 */

#pragma once

#include <furi.h>
#include <stdbool.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <inttypes.h>

/** True when the host has requested per-request timing in responses. */
extern bool metrics_enabled;

/**
 * Timestamps captured by rpc_dispatch() for the current request.
 * Written once per request (before the handler is invoked) and read once
 * (inside the response helper that sends the reply).
 */
typedef struct {
    uint32_t t_start;        /**< furi_get_tick() at entry to rpc_dispatch()    */
    uint32_t t_parsed;       /**< furi_get_tick() after "c" and "i" are parsed  */
    uint32_t t_dispatched;   /**< furi_get_tick() after resource check, before handler */
    uint32_t t_handler_done; /**< furi_get_tick() at entry to rpc_send_*() — just before
                              *   serialization; captures end of handler execution     */
} RpcMetrics;

/** Single per-daemon-instance metrics snapshot, filled by rpc_dispatch(). */
extern RpcMetrics g_metrics;

/**
 * Append a "," + "_m" timing object to an existing response buffer.
 *
 * Call this inside a response helper AFTER the base envelope has been
 * snprintf'd but BEFORE the trailing "}\n" has been appended.
 *
 * The serialize phase duration (sr) is measured as the elapsed time between
 * the moment the handler returned (g_metrics.t_handler_done) and the moment
 * this function is called — i.e. the time spent inside rpc_send_*() up to
 * the point the metrics are stamped.
 *
 * @param buf      Buffer containing the partial response (not yet closed).
 * @param buf_size Total allocated size of buf.
 * @param pos      Current write position (number of valid bytes already in buf).
 * @return         New write position after appending the metrics fragment,
 *                 or pos unchanged if the fragment would not fit.
 */
static inline size_t metrics_append(char* buf, size_t buf_size, size_t pos) {
    uint32_t t_sr = furi_get_tick(); /* stamp serialize-phase end */
    uint32_t pr = g_metrics.t_parsed - g_metrics.t_start;
    uint32_t dp = g_metrics.t_dispatched - g_metrics.t_parsed;
    uint32_t ex = g_metrics.t_handler_done - g_metrics.t_dispatched;
    uint32_t sr = t_sr - g_metrics.t_handler_done;
    uint32_t tt = t_sr - g_metrics.t_start;
    int n = snprintf(
        buf + pos,
        buf_size - pos,
        ",\"_m\":{\"pr\":%" PRIu32 ",\"dp\":%" PRIu32 ",\"ex\":%" PRIu32
        ",\"sr\":%" PRIu32 ",\"tt\":%" PRIu32 "}",
        pr,
        dp,
        ex,
        sr,
        tt);
    if(n > 0 && (size_t)n < buf_size - pos) {
        pos += (size_t)n;
    }
    return pos;
}
