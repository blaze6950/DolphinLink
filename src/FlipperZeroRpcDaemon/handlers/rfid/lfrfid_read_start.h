/**
 * lfrfid_read_start.h — RPC handler declaration for the "lfrfid_read_start" command
 *
 * Opens a streaming LF RFID tag reader.  The LFRFIDWorker runs on its own
 * FreeRTOS task and posts StreamEvents to the shared stream_event_queue when
 * a tag is detected.
 *
 * Wire format (request):
 *   {"id":N,"cmd":"lfrfid_read_start"}
 *
 * Wire format (response — stream opened):
 *   {"id":N,"stream":M}
 *
 * Wire format (stream events):
 *   {"event":{"type":"<protocol_name>","data":"<hex>"},"stream":M}
 *
 * Wire format (response — error):
 *   {"id":N,"error":"stream_table_full"}  — no free stream slot
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
