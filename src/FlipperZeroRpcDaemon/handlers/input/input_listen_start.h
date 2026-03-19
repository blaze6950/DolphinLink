/**
 * input_listen_start.h — input_listen_start RPC handler declaration
 *
 * Command: input_listen_start  (streaming)
 *
 * Opens a broadcast streaming session that emits an event for every hardware
 * button event on the Flipper.  Multiple concurrent streams are allowed; all
 * active streams receive every event (pub/sub broadcast, no exclusive lock).
 *
 * Wire format (request):
 *   {"id":N,"cmd":"input_listen_start"}
 *
 * Wire format (stream opened):
 *   {"id":N,"stream":M}
 *
 * Wire format (stream events):
 *   {"event":{"key":"ok","type":"short"},"stream":M}
 *   {"event":{"key":"back","type":"long"},"stream":M}
 *
 *   key  : "up" | "down" | "left" | "right" | "ok" | "back"
 *   type : "press" | "release" | "short" | "long" | "repeat"
 *
 * Error codes:
 *   stream_table_full — no free stream slots
 *
 * Resources: none — broadcast; no exclusive resource token required.
 *
 * Threading: The FuriPubSub callback fires on the event-loop thread
 * (it is called from furi_pubsub_publish which is invoked by the input
 * service, which runs on a separate thread and posts into a FuriMessageQueue
 * that the event-loop drains — so the callback runs on the main thread here).
 * snprintf is safe inside the callback.
 */

#pragma once

#include <stdint.h>

/**
 * Handle an "input_listen_start" request.
 *
 * Allocates a stream slot, subscribes to the Flipper input FuriPubSub,
 * and sends the stream-opened response.  Button events are subsequently
 * posted to stream_event_queue by the pubsub callback and serialised to
 * the host by the main event loop.
 *
 * @param id   Request ID echoed in the response.
 * @param json Full JSON request line (no args required).
 */
void input_listen_start_handler(uint32_t id, const char* json);
