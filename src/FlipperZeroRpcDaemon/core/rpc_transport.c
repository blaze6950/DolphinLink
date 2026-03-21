/**
 * rpc_transport.c — USB CDC transport layer implementation
 *
 * TX pipeline:
 *   cdc_send() enqueues bytes into tx_stream (FuriStreamBuffer, 512 B).
 *   The TX thread "RpcTx" drains tx_stream in ≤64-byte chunks, pacing each
 *   chunk through tx_semaphore so that furi_hal_cdc_send() is never called
 *   while the USB endpoint is still busy.  cdc_tx_callback() (USB ISR) releases
 *   the semaphore once the endpoint becomes idle.  A lazy ZLP is sent whenever
 *   the previous chunk was exactly 64 bytes and the stream subsequently empties,
 *   ensuring the host CDC driver sees a short-packet end-of-transfer.
 *
 * RX pipeline:
 *   cdc_rx_callback() (USB ISR) pulls bytes via furi_hal_cdc_receive(), builds
 *   lines, and posts complete '\n'-terminated lines to rx_queue.
 *   on_rx_queue() (main thread) skips empty keep-alive frames and dispatches
 *   the rest to rpc_dispatch().
 *
 * Heartbeat / keep-alive:
 *   Implemented here as a transport-layer concern via a FuriEventLoopTimer.
 *   TX side: sends a bare '\n' keep-alive when outbound traffic has been idle
 *   for ≥ HEARTBEAT_TX_IDLE_MS.
 *   RX side: declares the host gone when no inbound data has arrived for
 *   ≥ HEARTBEAT_RX_TIMEOUT_MS; tears down all streams and resources.
 *   Both last_tx_ticks and last_rx_ticks are read/written on the main thread
 *   only — no synchronisation is required.
 */

#include "rpc_transport.h"
#include "rpc_stream.h"
#include "rpc_resource.h"
#include "rpc_gui.h"

#include <furi_hal_usb_cdc.h>
#include <inttypes.h>
#include <string.h>

/* =========================================================
 * TX state
 * ========================================================= */

/* Outbound byte stream: main thread writes, TX thread reads.
 * trigger_level=1 so the TX thread wakes as soon as any byte is available. */
#define TX_BUF_SIZE  512
#define TX_STACK_SIZE 512

static FuriStreamBuffer* tx_stream   = NULL;
static FuriSemaphore*    tx_semaphore = NULL;
static FuriThread*       tx_thread   = NULL;
static volatile bool     tx_running  = false;

/**
 * Tick count of the last cdc_send() call.  Written on the main thread,
 * read by the heartbeat timer callback (also main thread) — no synchronisation
 * needed.  0 means no message has been sent yet.
 */
uint32_t last_tx_ticks = 0;

/**
 * Tick count of the last line received from the host.
 * Updated in on_rx_queue() on every dispatch.  Read by the heartbeat timer
 * callback (also main thread) — no synchronisation needed.
 * Initialised to 0; set to furi_get_tick() on the first received line.
 */
uint32_t last_rx_ticks = 0;

/* =========================================================
 * TX thread worker
 * ========================================================= */

static int32_t tx_worker(void* ctx) {
    UNUSED(ctx);

    uint8_t chunk[CDC_DATA_SZ];
    uint32_t prev_len = 0; /* length of the last chunk sent */

    while(tx_running) {
        /* Block up to 100 ms waiting for data (short enough to notice shutdown). */
        size_t received = furi_stream_buffer_receive(tx_stream, chunk, sizeof(chunk), 100);

        if(received == 0) {
            /* No data this iteration. */
            if(prev_len == CDC_DATA_SZ) {
                /* Previous chunk was a full 64-byte packet — send a ZLP so the
                 * host CDC driver sees a short packet and flushes its buffer. */
                furi_semaphore_acquire(tx_semaphore, 500);
                furi_hal_cdc_send(RPC_CDC_IF, NULL, 0);
                prev_len = 0;
            }
            continue;
        }

        /* Wait for the USB TX endpoint to become idle (released by tx_ep_callback).
         * 500 ms timeout guards against USB disconnect: if the host unplugs, the
         * callback never fires.  On timeout we release the semaphore ourselves to
         * reset state and keep the loop alive. */
        FuriStatus status = furi_semaphore_acquire(tx_semaphore, 500);
        if(status != FuriStatusOk) {
            /* USB disconnect or stall — discard this chunk and reset state. */
            furi_semaphore_release(tx_semaphore);
            prev_len = 0;
            continue;
        }

        furi_hal_cdc_send(RPC_CDC_IF, chunk, (uint16_t)received);
        prev_len = received;
    }

    return 0;
}

/* =========================================================
 * TX lifecycle
 * ========================================================= */

void cdc_transport_alloc(void) {
    /* Stream buffer: 512 bytes capacity, wake TX thread on any byte */
    tx_stream = furi_stream_buffer_alloc(TX_BUF_SIZE, 1);

    /* Semaphore: max_count=1, initial_count=1 (first send proceeds immediately) */
    tx_semaphore = furi_semaphore_alloc(1, 1);

    tx_running = true;
    tx_thread  = furi_thread_alloc_ex("RpcTx", TX_STACK_SIZE, tx_worker, NULL);
    furi_thread_start(tx_thread);
}

void cdc_transport_free(void) {
    /* Signal the TX thread to exit and wait for it */
    tx_running = false;
    furi_thread_join(tx_thread);
    furi_thread_free(tx_thread);
    tx_thread = NULL;

    furi_semaphore_free(tx_semaphore);
    tx_semaphore = NULL;

    furi_stream_buffer_free(tx_stream);
    tx_stream = NULL;
}

/* =========================================================
 * cdc_send — called from the main thread
 * ========================================================= */

void cdc_send(const char* data) {
    size_t remaining = strlen(data);
    const uint8_t* ptr = (const uint8_t*)data;

    /* Push all bytes into the stream buffer.  furi_stream_buffer_send() blocks
     * (FuriWaitForever) if the buffer is full, providing natural backpressure for
     * large responses without starving the event loop's own queue processing. */
    while(remaining > 0) {
        size_t sent = furi_stream_buffer_send(tx_stream, ptr, remaining, FuriWaitForever);
        ptr       += sent;
        remaining -= sent;
    }

    /* Record the tick at which this message was fully enqueued.  The heartbeat
     * timer reads this on the same thread (main thread) — no synchronisation needed. */
    last_tx_ticks = furi_get_tick();

    FURI_LOG_I("RPC", "TX: %s", data);
}

/* =========================================================
 * cdc_tx_callback — USB ISR, called when TX endpoint becomes idle
 * ========================================================= */

void cdc_tx_callback(void* ctx) {
    UNUSED(ctx);
    /* ISR-safe: furi_semaphore_release() uses FuriWaitForever=0 internally
     * for the ISR variant and never blocks. */
    furi_semaphore_release(tx_semaphore);
}

/* =========================================================
 * RX path — USB ISR accumulation
 * ========================================================= */

/* ISR-level accumulation buffer (no re-entrancy needed — the USB driver
 * serialises CDC RX callbacks on a single interface). */
static char     isr_buf[RX_LINE_MAX];
static uint16_t isr_pos = 0;

/* Called from USB interrupt context when data is available on the RX endpoint.
 * Signature: void(*)(void*) — data is pulled via furi_hal_cdc_receive(). */
void cdc_rx_callback(void* ctx) {
    UNUSED(ctx);

    /* CDC_DATA_SZ = 64 bytes per USB full-speed bulk packet */
    uint8_t buf[64];
    int32_t size = furi_hal_cdc_receive(RPC_CDC_IF, buf, sizeof(buf));
    if(size <= 0) return;

    for(int32_t i = 0; i < size; i++) {
        char c = (char)buf[i];

        if(isr_pos >= RX_LINE_MAX - 1) {
            /* Line too long — discard and reset */
            isr_pos = 0;
        }

        isr_buf[isr_pos++] = c;

        if(c == '\n') {
            isr_buf[isr_pos] = '\0';
            static RxLine line;
            line.len = isr_pos;
            memcpy(line.data, isr_buf, isr_pos + 1); /* include NUL */
            /* Non-blocking put; drop on overflow rather than stall ISR */
            furi_message_queue_put(rx_queue, &line, 0);
            isr_pos = 0;
        }
    }
}

/* =========================================================
 * CDC control-line (DTR/RTS) change notification
 * ========================================================= */

FuriMessageQueue* cdc_ctrl_queue = NULL;

/* Called from USB interrupt context when the host changes DTR/RTS.
 * context is cdc_ctrl_queue (passed via furi_hal_cdc_set_callbacks).
 * Only ISR-safe operations: furi_message_queue_put with timeout 0. */
void cdc_ctrl_line_callback(void* context, CdcCtrlLine ctrl_lines) {
    FuriMessageQueue* q = (FuriMessageQueue*)context;
    if(!q) return;
    CdcCtrlEvent ev = {.ctrl_lines = (uint8_t)ctrl_lines};
    /* Non-blocking; drop if the queue is full (capacity 2 is enough for
     * connect + disconnect without loss under normal conditions). */
    furi_message_queue_put(q, &ev, 0);
}

/* =========================================================
 * Heartbeat / keep-alive timer
 *
 * This is a pure transport-layer concern.  The timer fires periodically
 * and performs two independent checks:
 *
 *   TX side — if no message has been sent for ≥ heartbeat_tx_idle_ms,
 *   emit a bare '\n' keep-alive frame.  The host's HeartbeatTransport
 *   intercepts this empty line, updates its lastSeen timestamp, and does
 *   not forward it to the RPC layer.
 *
 *   RX side — if no message has been received for ≥ heartbeat_rx_timeout_ms,
 *   declare the host gone and tear down streams + resources.
 *
 * No RPC commands, no ping/pong, no acknowledgements.
 * ========================================================= */

/* Compile-time defaults for the runtime-configurable timing values.
 * These are the values used when no configure command has been sent and
 * after every host disconnect (heartbeat_reset_config). */
#define HEARTBEAT_TX_IDLE_MS_DEFAULT     3000U
#define HEARTBEAT_RX_TIMEOUT_MS_DEFAULT 10000U

/** Timer fires every 3 s (matches the default TX idle threshold). */
#define HEARTBEAT_TIMER_PERIOD_MS 3000U

/* Minimum values accepted by heartbeat_apply_config(). */
#define HEARTBEAT_TX_IDLE_MS_MIN   500U
#define HEARTBEAT_RX_TIMEOUT_MS_MIN 2000U

/**
 * Runtime-configurable heartbeat timing variables.
 * Both live on the main thread; no synchronisation required.
 * Declared extern in rpc_transport.h for access by configure_handler.
 */
uint32_t heartbeat_tx_idle_ms    = HEARTBEAT_TX_IDLE_MS_DEFAULT;
uint32_t heartbeat_rx_timeout_ms = HEARTBEAT_RX_TIMEOUT_MS_DEFAULT;

bool heartbeat_apply_config(uint32_t hb_ms, uint32_t to_ms) {
    if(hb_ms < HEARTBEAT_TX_IDLE_MS_MIN) return false;
    if(to_ms < HEARTBEAT_RX_TIMEOUT_MS_MIN) return false;
    if(to_ms <= hb_ms) return false;

    heartbeat_tx_idle_ms    = hb_ms;
    heartbeat_rx_timeout_ms = to_ms;

    FURI_LOG_I(
        "RPC",
        "Heartbeat config: tx_idle=%" PRIu32 " ms, rx_timeout=%" PRIu32 " ms",
        hb_ms,
        to_ms);
    return true;
}

void heartbeat_reset_config(void) {
    heartbeat_tx_idle_ms    = HEARTBEAT_TX_IDLE_MS_DEFAULT;
    heartbeat_rx_timeout_ms = HEARTBEAT_RX_TIMEOUT_MS_DEFAULT;
    FURI_LOG_I("RPC", "Heartbeat config reset to defaults");
}

/** Context bundled for the timer callback. */
typedef struct {
    ViewPort* view_port;
} HeartbeatCtx;

/**
 * Fires every HEARTBEAT_TIMER_PERIOD_MS while the host is connected.
 *
 * TX side: if no message has been sent for ≥ heartbeat_tx_idle_ms, emit a
 *   bare '\n' keep-alive frame.  This gives the host a liveness signal even
 *   when the daemon is idle (no stream events, no responses).  The payload is
 *   a single newline — the minimum NDJSON frame — with no RPC meaning.
 *
 * RX side: if no message has been received for ≥ heartbeat_rx_timeout_ms,
 *   assume the host has silently disappeared (cable pulled, app crashed,
 *   machine slept).  Tear down all streams + resources exactly as if DTR
 *   had dropped, and update the GUI status bar.
 */
static void on_heartbeat_timer(void* ctx) {
    HeartbeatCtx* hctx = ctx;

    if(!host_connected) return;

    uint32_t now = furi_get_tick();

    /* ---- RX watchdog: has the host gone silent? ---- */
    if(last_rx_ticks != 0 &&
       (now - last_rx_ticks) >= furi_ms_to_ticks(heartbeat_rx_timeout_ms)) {
        FURI_LOG_W(
            "RPC",
            "Heartbeat: no RX for %" PRIu32 " ms — host gone",
            heartbeat_rx_timeout_ms);
        host_connected = false;
        /* Reset timing to defaults so the next session starts clean. */
        heartbeat_reset_config();
        stream_close_all();
        resource_reset();
        if(hctx->view_port) {
            view_port_update(hctx->view_port);
        }
        /* Timer stays allocated; it will be stopped/started on the next DTR edge. */
        return;
    }

    /* ---- TX heartbeat: send keep-alive if outbound channel is idle ---- */
    /* Payload is a bare '\n' — minimum NDJSON frame (1 byte on the wire).
     * The host's HeartbeatTransport consumes it without passing it to the
     * RPC layer above, updating its lastSeen timestamp as proof-of-life. */
    if(last_tx_ticks == 0 ||
       (now - last_tx_ticks) >= furi_ms_to_ticks(heartbeat_tx_idle_ms)) {
        cdc_send("\n");
    }
}

/* ---- Static storage for one HeartbeatCtx per timer ---- */
/* The context must outlive the timer. Allocated statically to avoid a
 * heap allocation for a single long-lived object. */
static HeartbeatCtx g_heartbeat_ctx;

FuriEventLoopTimer* heartbeat_timer_alloc(FuriEventLoop* loop, ViewPort* view_port) {
    g_heartbeat_ctx.view_port = view_port;

    return furi_event_loop_timer_alloc(
        loop,
        on_heartbeat_timer,
        FuriEventLoopTimerTypePeriodic,
        &g_heartbeat_ctx);
}

void heartbeat_timer_start(FuriEventLoopTimer* timer) {
    furi_event_loop_timer_start(timer, furi_ms_to_ticks(HEARTBEAT_TIMER_PERIOD_MS));
}

void heartbeat_timer_stop(FuriEventLoopTimer* timer) {
    furi_event_loop_timer_stop(timer);
}

void heartbeat_timer_free(FuriEventLoopTimer* timer) {
    furi_event_loop_timer_free(timer);
}
