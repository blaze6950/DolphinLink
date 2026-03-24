/**
 * daemon_stop.c — daemon_stop command handler implementation
 *
 * Sends an OK response and then stops the RPC event loop, triggering the
 * existing graceful teardown sequence: all streams are closed, hardware
 * resources are released, a {"disconnect":true} notification is sent, and
 * the USB configuration is restored.
 */

#include "daemon_stop.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_stream.h"

#include <furi.h>
#include <inttypes.h>

void daemon_stop_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

    /* Acknowledge before stopping so the host receives the response. */
    rpc_send_ok(id, "daemon_stop");

    /*
     * Signal the event loop to stop.  furi_event_loop_stop() returns
     * immediately; the loop exits at its next iteration, which triggers the
     * full teardown sequence in dolphin_link_rpc_daemon_app().
     */
    furi_event_loop_stop(g_event_loop);
}
