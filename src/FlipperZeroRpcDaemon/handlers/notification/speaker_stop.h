/**
 * speaker_stop.h — RPC handler declaration for the "speaker_stop" command
 *
 * Stops the piezo speaker and releases RESOURCE_SPEAKER so the next
 * "speaker_start" can acquire it.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"speaker_stop"}
 *
 * Wire format (response — success):
 *   {"id":N,"status":"ok"}
 *
 * Resources: none claimed by the dispatcher (0), but the handler calls
 * resource_release(RESOURCE_SPEAKER) to release the previously held lock.
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "speaker_stop" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void speaker_stop_handler(uint32_t id, const char* json);
