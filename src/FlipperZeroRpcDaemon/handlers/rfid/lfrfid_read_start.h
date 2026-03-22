/**
 * lfrfid_read_start.h — RPC handler declaration for the "lfrfid_read_start" command
 *
 * Opens a streaming LF RFID tag reader.  The LFRFIDWorker runs on its own
 * FreeRTOS task and posts StreamEvents to the shared stream_event_queue when
 * a tag is detected.
 *
 * Wire format (request):
 *   {"c":37,"i":N}
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
 * Resources: RESOURCE_RFID.
 */

#pragma once

#include <stdint.h>

/**
 * Handle the "lfrfid_read_start" RPC command.
 *
 * @param id   Request ID from the JSON message.
 * @param json Full JSON line (null-terminated) received from the host.
 */
void lfrfid_read_start_handler(uint32_t id, const char* json);
