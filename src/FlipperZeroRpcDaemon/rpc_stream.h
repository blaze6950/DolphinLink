/**
 * rpc_stream.h — Active stream table management
 *
 * Tracks up to MAX_STREAMS concurrent open streams.  Each stream owns a
 * ResourceMask; closing a stream releases its resources.
 *
 * All functions must be called from the main thread only.
 */

#pragma once

#include "rpc_resource.h"

#include <stddef.h>
#include <stdbool.h>

#define MAX_STREAMS 8

typedef struct {
    uint32_t id;
    ResourceMask resources;
    bool active;
} RpcStream;

/* Stream table and ID counter — storage provided by rpc_stream.c */
extern RpcStream active_streams[MAX_STREAMS];
extern uint32_t next_stream_id;

/**
 * Find the first inactive slot.
 * Returns the slot index [0, MAX_STREAMS), or -1 if the table is full.
 * Does NOT acquire resources.
 */
int stream_alloc_slot(void);

/**
 * Find the slot for a stream with the given ID.
 * Returns the slot index [0, MAX_STREAMS), or -1 if not found.
 */
int stream_find_by_id(uint32_t id);

/**
 * Deactivate the stream at @p idx and release its resources.
 * Safe to call with an already-inactive slot (no-op).
 */
void stream_close_by_index(size_t idx);

/** Close all active streams and release their resources. */
void stream_close_all(void);

/** Return the number of currently active streams. */
uint32_t stream_count_active(void);

/** Reset the stream table to empty (call during init). */
void stream_reset(void);
