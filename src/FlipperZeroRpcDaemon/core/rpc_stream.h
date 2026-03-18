/**
 * rpc_stream.h — Active stream table management
 *
 * Tracks up to MAX_STREAMS concurrent open streams.  Each stream owns a
 * ResourceMask and a hardware-specific teardown function.  Closing a stream
 * calls the teardown (which stops hardware and releases SDK objects) and then
 * releases the ResourceMask.
 *
 * StreamEvent / stream_event_queue / g_event_loop
 * ------------------------------------------------
 * Hardware callbacks (IR worker thread, SubGhz worker thread, NFC worker
 * thread, GPIO EXTI ISR) post a StreamEvent into stream_event_queue.  The
 * FuriEventLoop on the main thread drains that queue and calls cdc_send().
 *
 * ISR constraint: GPIO EXTI callbacks run in interrupt context — they may
 * only call furi_message_queue_put().  All JSON formatting must happen before
 * the ISR fires (pre-composed into the stream slot's hw.gpio.frag_* buffers).
 *
 * All functions other than the hardware callbacks must be called from the
 * main thread only.
 */

#pragma once

#include "rpc_resource.h"

#include <furi.h>
#include <infrared_worker.h>
#include <subghz/subghz_worker.h>
#include <nfc/nfc.h>
#include <nfc/nfc_scanner.h>
#include <furi_hal_gpio.h>
#include <lib/lfrfid/lfrfid_worker.h>
#include <lib/toolbox/protocols/protocol_dict.h>
#include <lib/ibutton/ibutton_worker.h>
#include <lib/ibutton/ibutton_protocols.h>
#include <stddef.h>
#include <stdbool.h>

/* -------------------------------------------------------------------------
 * Stream event — produced by hardware callbacks, consumed by event loop
 * ------------------------------------------------------------------------- */

#define STREAM_FRAG_MAX 128

/** Fixed-size message posted from any hardware callback into stream_event_queue. */
typedef struct {
    uint32_t stream_id;
    char json_fragment[STREAM_FRAG_MAX]; /**< Content inside {"event":{...},"stream":N} */
} StreamEvent;

/* -------------------------------------------------------------------------
 * Stream slot
 * ------------------------------------------------------------------------- */

#define MAX_STREAMS 8

/** Forward declaration so the typedef can be used in the struct itself. */
typedef struct RpcStream RpcStream;

/**
 * Teardown function for a stream.  Called by stream_close_by_index() with
 * the slot index before the slot is cleared.  Must stop hardware, free SDK
 * objects, and unsubscribe the event queue from the event loop.
 * Must be called from the main thread.
 */
typedef void (*StreamTeardown)(size_t slot_idx);

/** GPIO pre-composed fragment buffer length (includes "pin":"N","level":true/false). */
#define GPIO_FRAG_MAX 48

struct RpcStream {
    uint32_t id;
    ResourceMask resources;
    bool active;

    /** NULL for streams with no hardware teardown needed. */
    StreamTeardown teardown;

    /** Per-stream hardware state. */
    union {
        struct {
            InfraredWorker* worker;
        } ir;

        struct {
            SubGhzWorker* worker;
        } subghz;

        struct {
            Nfc* nfc;
            NfcScanner* scanner;
        } nfc;

        struct {
            const GpioPin* pin;
            /** Pre-composed fragments — selected by the ISR (no snprintf in ISR). */
            char frag_high[GPIO_FRAG_MAX]; /**< "pin":"N","level":true  */
            char frag_low[GPIO_FRAG_MAX]; /**< "pin":"N","level":false */
        } gpio;

        struct {
            LFRFIDWorker* worker;
            ProtocolDict* dict;
        } lfrfid;

        struct {
            iButtonWorker* worker;
            iButtonProtocols* protocols;
            iButtonKey* key;
        } ibutton;
    } hw;
};

/* -------------------------------------------------------------------------
 * Module-level state (storage provided by rpc_stream.c)
 * ------------------------------------------------------------------------- */

/** Active stream table. */
extern RpcStream active_streams[MAX_STREAMS];

/** Monotonically increasing stream ID counter. */
extern uint32_t next_stream_id;

/**
 * Single shared queue for all hardware-generated stream events.
 * Subscribed to the FuriEventLoop in flipper_zero_rpc_daemon.c.
 * Storage provided by rpc_stream.c; allocated/freed by rpc_stream_event_init/deinit().
 */
extern FuriMessageQueue* stream_event_queue;

/**
 * The application's FuriEventLoop.
 * Set by the entry point before furi_event_loop_run(); cleared after.
 * Used by stream teardown functions to unsubscribe the event queue.
 */
extern FuriEventLoop* g_event_loop;

/* -------------------------------------------------------------------------
 * Public API
 * ------------------------------------------------------------------------- */

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
 * Deactivate the stream at @p idx: call its teardown (if set), release
 * resources, and clear the slot.  Safe to call on an already-inactive slot.
 */
void stream_close_by_index(size_t idx);

/** Close all active streams. */
void stream_close_all(void);

/** Return the number of currently active streams. */
uint32_t stream_count_active(void);

/** Reset the stream table to empty (call during init). */
void stream_reset(void);

/**
 * Event-loop subscriber: stream_event_queue has data.
 * Reads StreamEvent items and emits {"event":{<fragment>},"stream":<id>}\n
 * over CDC.  Registered by the entry point.
 */
void on_stream_event(FuriEventLoopObject* object, void* ctx);
