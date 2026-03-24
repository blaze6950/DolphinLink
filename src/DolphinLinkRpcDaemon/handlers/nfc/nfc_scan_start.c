/**
 * nfc_scan_start.c — nfc_scan_start RPC handler implementation
 *
 * Opens a streaming NFC protocol scan session.  Each time a tag protocol is
 * detected by the NfcScanner, its name is posted to stream_event_queue.
 *
 * Wire format (stream event):
 *   {"t":1,"i":M,"p":{"pr":"ISO15693-3"}}
 *
 * Resources: RESOURCE_NFC (pre-acquired by the dispatcher)
 *
 * The NfcScanner callback fires on the NFC worker thread.
 * furi_message_queue_put() is safe there.
 */

#include "nfc_scan_start.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_resource.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <nfc/nfc.h>
#include <nfc/nfc_scanner.h>
#include <nfc/nfc_device.h>
#include <inttypes.h>

static void nfc_scanner_callback(NfcScannerEvent event, void* ctx) {
    if(event.type != NfcScannerEventTypeDetected) return;
    if(event.data.protocol_num == 0) return;

    uint32_t stream_id = (uint32_t)(uintptr_t)ctx;

    StreamEvent ev;
    ev.stream_id = stream_id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"pr\":\"%s\"",
        nfc_device_get_protocol_name(event.data.protocols[0]));
    furi_message_queue_put(stream_event_queue, &ev, 0);
}

static void nfc_teardown(size_t slot_idx) {
    NfcScanner* scanner = active_streams[slot_idx].hw.nfc.scanner;
    Nfc* nfc = active_streams[slot_idx].hw.nfc.nfc;
    if(scanner) {
        nfc_scanner_stop(scanner);
        nfc_scanner_free(scanner);
        active_streams[slot_idx].hw.nfc.scanner = NULL;
    }
    if(nfc) {
        nfc_free(nfc);
        active_streams[slot_idx].hw.nfc.nfc = NULL;
    }
}

void nfc_scan_start_handler(uint32_t id, const char* json, size_t offset) {
    (void)json;
    (void)offset;

    uint32_t stream_id = 0;
    int slot = stream_open(id, "nfc_scan_start", RESOURCE_NFC, &stream_id);
    if(slot < 0) return;

    Nfc* nfc = nfc_alloc();
    NfcScanner* scanner = nfc_scanner_alloc(nfc);
    nfc_scanner_start(scanner, nfc_scanner_callback, (void*)(uintptr_t)stream_id);

    active_streams[slot].hw.nfc.nfc = nfc;
    active_streams[slot].hw.nfc.scanner = scanner;
    active_streams[slot].teardown = nfc_teardown;

    stream_send_opened(id, stream_id, "nfc_scan_start");
    FURI_LOG_I("RPC", "NFC scan stream opened id=%" PRIu32, stream_id);
}
