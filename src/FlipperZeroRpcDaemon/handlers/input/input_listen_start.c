/**
 * input_listen_start.c — input_listen_start RPC handler implementation
 *
 * Subscribes to the Flipper input FuriPubSub and streams every button event
 * to the host as NDJSON stream events.  Multiple concurrent streams are
 * supported (broadcast — no exclusive resource lock).
 *
 * Wire format (request):
 *   {"c":N,"i":M}                          — no exit combo
 *   {"c":N,"i":M,"ek":4,"et":2}            — exit on Ok+Short
 *
 * Wire format (stream opened):
 *   {"t":0,"i":N,"p":{"s":M}}
 *
 * Wire format (stream events):
 *   {"t":1,"i":M,"p":{"k":4,"ty":2}}
 *
 *   k  : 0=Up 1=Down 2=Left 3=Right 4=Ok 5=Back  ($FlipperInputKey integer)
 *   ty : 0=Press 1=Release 2=Short 3=Long 4=Repeat ($FlipperInputType integer)
 *
 * Threading note
 * --------------
 * furi_pubsub callbacks are invoked by the publisher on whatever thread calls
 * furi_pubsub_publish().  The input service publishes from its own thread.
 * Our callback uses only furi_message_queue_put() (ISR-safe) and snprintf,
 * both of which are safe to call from any thread.
 */

#include "input_listen_start.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <input/input.h>
#include <inttypes.h>

/* =========================================================
 * PubSub callback — called on the input service thread
 * ========================================================= */

/**
 * ctx is the RpcStream slot pointer.
 * The callback is safe to call concurrently with the main thread because it
 * only calls furi_message_queue_put (thread-safe) and snprintf (re-entrant).
 */
static void input_pubsub_callback(const void* message, void* ctx) {
    const InputEvent* event = (const InputEvent*)message;
    RpcStream* slot = (RpcStream*)ctx;

    if(!slot->active) return;

    /* Suppress the exit combo itself — it is consumed by on_input_queue() to
     * stop the daemon and must not also be forwarded to the host as a stream
     * event.  Without this guard the exit key press races: the host receives a
     * stream event followed immediately by {"disconnect":true}, which can leave
     * the host hanging while the daemon has already exited. */
    if(slot->hw.input.has_exit_combo &&
       event->key == slot->hw.input.exit_key &&
       event->type == slot->hw.input.exit_type) {
        return;
    }

    StreamEvent ev;
    ev.stream_id = slot->id;
    snprintf(
        ev.json_fragment,
        STREAM_FRAG_MAX,
        "\"k\":%" PRIu32 ",\"ty\":%" PRIu32,
        (uint32_t)event->key,
        (uint32_t)event->type);

    furi_message_queue_put(stream_event_queue, &ev, 0);
}

/* =========================================================
 * Teardown — called from the main thread when the stream is closed
 * ========================================================= */

static void input_teardown(size_t slot_idx) {
    FuriPubSubSubscription* sub = active_streams[slot_idx].hw.input.subscription;
    if(sub) {
        FuriPubSub* input_events = furi_record_open(RECORD_INPUT_EVENTS);
        furi_pubsub_unsubscribe(input_events, sub);
        furi_record_close(RECORD_INPUT_EVENTS);
        active_streams[slot_idx].hw.input.subscription = NULL;
    }
}

/* =========================================================
 * Handler
 * ========================================================= */

void input_listen_start_handler(uint32_t id, const char* json) {
    /* No exclusive resource — broadcast to all listeners */
    uint32_t stream_id = 0;
    int slot = stream_open(id, "input_listen_start", 0, &stream_id);
    if(slot < 0) return;

    FuriPubSub* input_events = furi_record_open(RECORD_INPUT_EVENTS);
    FuriPubSubSubscription* sub =
        furi_pubsub_subscribe(input_events, input_pubsub_callback, &active_streams[slot]);
    furi_record_close(RECORD_INPUT_EVENTS);

    active_streams[slot].hw.input.subscription = sub;
    active_streams[slot].hw.input.has_exit_combo = false;
    /* Mark this slot as an input stream so on_input_queue() knows it is safe
     * to read hw.input.has_exit_combo.  Without this flag, on_input_queue()
     * would read the field on ALL active streams, and for non-input streams
     * the hw union aliases has_exit_combo onto hardware-pointer bytes,
     * incorrectly suppressing the default Back+Short daemon-exit combo. */
    active_streams[slot].is_input_stream = true;

    /* Parse optional exit key/type combo — integer wire keys "ek" and "et" */
    uint32_t ek = 0, et = 0;
    if(json_extract_uint32(json, "ek", &ek) && json_extract_uint32(json, "et", &et)) {
        active_streams[slot].hw.input.exit_key = (InputKey)ek;
        active_streams[slot].hw.input.exit_type = (InputType)et;
        active_streams[slot].hw.input.has_exit_combo = true;
    }

    active_streams[slot].teardown = input_teardown;

    stream_send_opened(id, stream_id, "input_listen_start");
    FURI_LOG_I("RPC", "Input listen stream opened id=%" PRIu32, stream_id);
}
