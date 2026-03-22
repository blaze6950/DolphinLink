/**
 * reboot.c — reboot command handler implementation
 *
 * Sends an OK response and then calls furi_hal_power_reset(), which performs
 * an immediate hardware-level MCU reset (equivalent to pressing the physical
 * reset button).  furi_hal_power_reset() never returns; the USB connection
 * drops almost immediately after the response is flushed.
 */

#include "reboot.h"
#include "../../core/rpc_response.h"

#include <furi.h>
#include <furi_hal_power.h>
#include <inttypes.h>

void reboot_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

    /* Acknowledge before resetting so the host receives the response. */
    rpc_send_ok(id, "reboot");

    /* Hard MCU reset — does not return. */
    furi_hal_power_reset();
}
