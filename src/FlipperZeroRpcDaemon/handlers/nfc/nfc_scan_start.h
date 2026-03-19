/**
 * nfc_scan_start.h — nfc_scan_start RPC handler declaration
 *
 * Command: nfc_scan_start  (streaming)
 *
 * Wire format (request):
 *   {"id":N,"cmd":"nfc_scan_start"}
 *
 * Wire format (stream opened):
 *   {"id":N,"stream":M}
 *
 * Wire format (stream event — emitted each time a tag protocol is detected):
 *   {"event":{"protocol":"ISO15693-3"},"stream":M}
 *     protocol — NFC protocol name from nfc_device_get_protocol_name()
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

/**
 * Handle an "nfc_scan_start" request.
 *
 * Allocates an NFC scanner, starts it, allocates a stream slot, and sends
 * the stream-opened response.  Protocol-detected events are posted to
 * stream_event_queue by the scanner callback.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line (unused).
 */
void nfc_scan_start_handler(uint32_t id, const char* json);
