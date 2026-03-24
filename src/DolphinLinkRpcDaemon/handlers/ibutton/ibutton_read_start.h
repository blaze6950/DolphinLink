/**
 * ibutton_read_start.h — RPC handler declaration for the "ibutton_read_start" command
 *
 * Opens a streaming iButton key reader.  The iButtonWorker runs on its own
 * FreeRTOS task and posts StreamEvents to the shared stream_event_queue when
 * a key is detected.
 *
 * Wire format (request):
 *   {"c":38,"i":N}
 *
 * Wire format (response — stream opened):
 *   {"t":0,"i":N,"p":{"s":M}}
 *
 * Wire format (stream events):
 *   {"t":1,"i":M,"p":{"ty":"<protocol_name>","d":"<hex>"}}
 *
 * Wire format (response — error):
 *   {"t":0,"i":N,"e":"stream_table_full"}  — no free stream slot
 *
 * Resources: RESOURCE_IBUTTON.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle the "ibutton_read_start" RPC command.
 *
 * @param id     Request ID from the JSON message.
 * @param json   Full JSON line (null-terminated) received from the host.
 * @param offset Byte offset past the already-parsed "c" and "i" fields.
 */
void ibutton_read_start_handler(uint32_t id, const char* json, size_t offset);
