/**
 * ibutton_read_start.h — RPC handler declaration for the "ibutton_read_start" command
 *
 * Opens a streaming iButton key reader.  The iButtonWorker runs on its own
 * FreeRTOS task and posts StreamEvents to the shared stream_event_queue when
 * a key is detected.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"ibutton_read_start"}
 *
 * Wire format (response — stream opened):
 *   {"t":0,"i":N,"p":{"stream":M}}
 *
 * Wire format (stream events):
 *   {"t":1,"i":M,"p":{"type":"<protocol_name>","data":"<hex>"}}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"stream_table_full"}  — no free stream slot
 *
 * Resources: RESOURCE_IBUTTON.
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "ibutton_read_start" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void ibutton_read_start_handler(uint32_t id, const char* json);
