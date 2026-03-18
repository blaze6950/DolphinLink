/**
 * rpc_transport.c — USB CDC transport layer implementation
 */

#include "rpc_transport.h"

#include <furi_hal_usb_cdc.h>
#include <string.h>

/* ISR-level accumulation buffer (no re-entrancy needed — the USB driver
 * serialises CDC RX callbacks on a single interface). */
static char isr_buf[RX_LINE_MAX];
static uint16_t isr_pos = 0;

void cdc_send(const char* json) {
    size_t len = strlen(json);
    furi_hal_cdc_send(RPC_CDC_IF, (uint8_t*)json, (uint32_t)len);
    FURI_LOG_I("RPC", "TX: %s", json);
}

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
            RxLine line;
            line.len = isr_pos;
            memcpy(line.data, isr_buf, isr_pos + 1); /* include NUL */
            /* Non-blocking put; drop on overflow rather than stall ISR */
            furi_message_queue_put(rx_queue, &line, 0);
            isr_pos = 0;
        }
    }
}
