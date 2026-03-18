/**
 * rpc_stream.c — Active stream table management implementation
 */

#include "rpc_stream.h"
#include "rpc_transport.h"

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
        /* Emit: {"event":{<fragment>},"stream":<id>}\n */
        char buf[32 + STREAM_FRAG_MAX];
        snprintf(
            buf,
            sizeof(buf),
            "{\"event\":{%s},\"stream\":%" PRIu32 "}\n",
            ev.json_fragment,
            ev.stream_id);
        cdc_send(buf);
    }
}
