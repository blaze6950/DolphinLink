/**
 * nfc_scan_start.h — nfc_scan_start RPC handler declaration
 *
 * Command: nfc_scan_start  (streaming)
 *
 * Wire format (request):
 *   {"c":23,"i":N}
 *
 * Wire format (stream opened):
 *   {"t":0,"i":N,"p":{"s":M}}
 *
 * Wire format (stream event — emitted each time a tag protocol is detected):
 *   {"t":1,"i":M,"p":{"pr":"ISO15693-3"}}
 *     pr — NFC protocol name from nfc_device_get_protocol_name()
 *
 * Error codes:
 *   stream_table_full — no free stream slots
 *
 * Resources: RESOURCE_NFC (pre-acquired by the dispatcher)
 *
 * The NfcScanner callback fires on the NFC worker thread.
 * furi_message_queue_put() is safe there.
 * Only NfcScannerEventTypeDetected events with at least one protocol are forwarded.
 */

#pragma once

#include <stdint.h>
#include <stddef.h>

/**
 * Handle an "nfc_scan_start" request.
 *
 * @param id     Request ID echoed in the response.
 * @param json   Full JSON request line (unused).
 * @param offset Byte offset past the already-parsed envelope fields.
 */
void nfc_scan_start_handler(uint32_t id, const char* json, size_t offset);
