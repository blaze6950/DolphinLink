/**
 * rpc_stream.c — Active stream table management implementation
 *
 * Provides the shared stream table (active_streams[], next_stream_id,
 * stream_event_queue, g_event_loop) and the two helpers used by every
 * stream-command handler:
 *
 *   stream_open()        — allocate slot, acquire resource, assign ID
 *   stream_send_opened() — emit V2 stream-open response over CDC:
 *                          {"type":"response","id":N,"payload":{"stream":M}}\n
 *
 * These were previously copy-pasted as static functions into every handler
 * file.  They are now the single canonical implementations.
 */

#include "rpc_stream.h"
#include "rpc_transport.h"
#include "rpc_response.h"
#include "rpc_resource.h"
#include "rpc_cmd_log.h"

#include <string.h>
#include <stdio.h>
#include <inttypes.h>

/* -------------------------------------------------------------------------
 * Module-level state
 * ------------------------------------------------------------------------- */

RpcStream active_streams[MAX_STREAMS];
uint32_t next_stream_id = 1;
FuriMessageQueue* stream_event_queue = NULL;
FuriEventLoop* g_event_loop = NULL;

/* -------------------------------------------------------------------------
 * Stream table helpers
 * ------------------------------------------------------------------------- */

int stream_alloc_slot(void) {
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(!active_streams[i].active) return (int)i;
    }
    return -1;
}

int stream_find_by_id(uint32_t id) {
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active && active_streams[i].id == id) return (int)i;
    }
    return -1;
}

void stream_close_by_index(size_t idx) {
    if(idx >= MAX_STREAMS || !active_streams[idx].active) return;

    /* Call hardware-specific teardown first */
    if(active_streams[idx].teardown) {
        active_streams[idx].teardown(idx);
    }

    resource_release(active_streams[idx].resources);
    active_streams[idx].active = false;
}

void stream_close_all(void) {
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        stream_close_by_index(i);
    }
}

uint32_t stream_count_active(void) {
    uint32_t n = 0;
    for(size_t i = 0; i < MAX_STREAMS; i++) {
        if(active_streams[i].active) n++;
    }
    return n;
}

void stream_reset(void) {
    memset(active_streams, 0, sizeof(active_streams));
    next_stream_id = 1;
}

/* -------------------------------------------------------------------------
 * Stream event — event-loop callback
 * ------------------------------------------------------------------------- */

void on_stream_event(FuriEventLoopObject* object, void* ctx) {
    UNUSED(object);
    UNUSED(ctx);

    StreamEvent ev;
    while(furi_message_queue_get(stream_event_queue, &ev, 0) == FuriStatusOk) {
        /* Emit V2: {"type":"event","id":<stream_id>,"payload":{<fragment>}}\n */
        char buf[48 + STREAM_FRAG_MAX];
        snprintf(
            buf,
            sizeof(buf),
            "{\"type\":\"event\",\"id\":%" PRIu32 ",\"payload\":{%s}}\n",
            ev.stream_id,
            ev.json_fragment);
        cdc_send(buf);
    }
}

/* -------------------------------------------------------------------------
 * Shared stream-open helpers
 * ------------------------------------------------------------------------- */

int stream_open(uint32_t id, const char* cmd_name, ResourceMask res, uint32_t* stream_id_out) {
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
    /* Default: not an input stream.  input_listen_start_handler() sets this
     * to true after stream_open() returns.  on_input_queue() guards all
     * hw.input union reads behind this flag to prevent aliasing with the
     * hardware-pointer fields of non-input streams (NFC, GPIO, LFRFID, iButton). */
    active_streams[slot].is_input_stream = false;
    /* Zero the hw union so stale bytes from a previous occupant cannot
     * corrupt the new stream's hardware state. */
    memset(&active_streams[slot].hw, 0, sizeof(active_streams[slot].hw));
    *stream_id_out = stream_id;
    return slot;
}

void stream_send_opened(uint32_t request_id, uint32_t stream_id, const char* cmd_name) {
    /* V2 payload: {"stream":M} — rpc_send_data_response wraps in the type/id envelope */
    char payload[32];
    snprintf(payload, sizeof(payload), "{\"stream\":%" PRIu32 "}", stream_id);

    char log_entry[CMD_LOG_LINE_LEN];
    snprintf(
        log_entry,
        sizeof(log_entry),
        "#%" PRIu32 " %.14s->s:%" PRIu32,
        request_id,
        cmd_name,
        stream_id);
    rpc_send_data_response(request_id, payload, log_entry);
}
