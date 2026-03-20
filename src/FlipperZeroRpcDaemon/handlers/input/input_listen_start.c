/**
 * input_listen_start.c — input_listen_start RPC handler implementation
 *
 * Subscribes to the Flipper input FuriPubSub and streams every button event
 * to the host as NDJSON stream events.  Multiple concurrent streams are
 * supported (broadcast — no exclusive resource lock).
 *
 * Wire format (request):
 *   {"id":N,"cmd":"input_listen_start"}
 *
 * Wire format (stream opened):
 *   {"id":N,"stream":M}
 *
 * Wire format (stream events):
 *   {"event":{"key":"ok","type":"short"},"stream":M}
 *
 *   key  : "up" | "down" | "left" | "right" | "ok" | "back"
 *   type : "press" | "release" | "short" | "long" | "repeat"
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
#include <string.h>
#include <inttypes.h>

/* =========================================================
 * Key / type name tables
 * ========================================================= */

static const char* input_key_name(InputKey key) {
    switch(key) {
    case InputKeyUp:
        return "up";
    case InputKeyDown:
        return "down";
    case InputKeyLeft:
        return "left";
    case InputKeyRight:
        return "right";
    case InputKeyOk:
        return "ok";
    case InputKeyBack:
        return "back";
    default:
        return "unknown";
    }
}

static const char* input_type_name(InputType type) {
    switch(type) {
    case InputTypePress:
        return "press";
    case InputTypeRelease:
        return "release";
    case InputTypeShort:
        return "short";
    case InputTypeLong:
        return "long";
    case InputTypeRepeat:
        return "repeat";
    default:
        return "unknown";
    }
}

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
        "\"key\":\"%s\",\"type\":\"%s\"",
        input_key_name(event->key),
        input_type_name(event->type));

    furi_message_queue_put(stream_event_queue, &ev, 0);
}

/* =========================================================
 * Exit-key / exit-type parsing helpers
 * ========================================================= */

/**
 * Parse an optional "exit_key" JSON string arg.
 * Returns true and sets *out if a recognised key name is present.
 */
static bool parse_exit_key(const char* json, InputKey* out) {
    char buf[16];
    if(!json_extract_string(json, "exit_key", buf, sizeof(buf))) return false;
    if(strcmp(buf, "up") == 0)    { *out = InputKeyUp;    return true; }
    if(strcmp(buf, "down") == 0)  { *out = InputKeyDown;  return true; }
    if(strcmp(buf, "left") == 0)  { *out = InputKeyLeft;  return true; }
    if(strcmp(buf, "right") == 0) { *out = InputKeyRight; return true; }
    if(strcmp(buf, "ok") == 0)    { *out = InputKeyOk;    return true; }
    if(strcmp(buf, "back") == 0)  { *out = InputKeyBack;  return true; }
    return false;
}

/**
 * Parse an optional "exit_type" JSON string arg.
 * Returns true and sets *out if a recognised type name is present.
 */
static bool parse_exit_type(const char* json, InputType* out) {
    char buf[16];
    if(!json_extract_string(json, "exit_type", buf, sizeof(buf))) return false;
    if(strcmp(buf, "press") == 0)   { *out = InputTypePress;   return true; }
    if(strcmp(buf, "release") == 0) { *out = InputTypeRelease; return true; }
    if(strcmp(buf, "short") == 0)   { *out = InputTypeShort;   return true; }
    if(strcmp(buf, "long") == 0)    { *out = InputTypeLong;    return true; }
    if(strcmp(buf, "repeat") == 0)  { *out = InputTypeRepeat;  return true; }
    return false;
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

    /* Parse optional exit key/type combo */
    InputKey exit_key;
    InputType exit_type;
    if(parse_exit_key(json, &exit_key) && parse_exit_type(json, &exit_type)) {
        active_streams[slot].hw.input.exit_key = exit_key;
        active_streams[slot].hw.input.exit_type = exit_type;
        active_streams[slot].hw.input.has_exit_combo = true;
    }

    active_streams[slot].teardown = input_teardown;

    stream_send_opened(id, stream_id, "input_listen_start");
    FURI_LOG_I("RPC", "Input listen stream opened id=%" PRIu32, stream_id);
}
