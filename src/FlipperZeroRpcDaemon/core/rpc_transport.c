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
 */

#include "rpc_transport.h"

#include <furi_hal_usb_cdc.h>
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

void cdc_send(const char* json) {
    size_t remaining = strlen(json);
    const uint8_t* ptr = (const uint8_t*)json;

    /* Push all bytes into the stream buffer.  furi_stream_buffer_send() blocks
     * (FuriWaitForever) if the buffer is full, providing natural backpressure for
     * large responses without starving the event loop's own queue processing. */
    while(remaining > 0) {
        size_t sent = furi_stream_buffer_send(tx_stream, ptr, remaining, FuriWaitForever);
        ptr       += sent;
        remaining -= sent;
    }

    FURI_LOG_I("RPC", "TX: %s", json);
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
