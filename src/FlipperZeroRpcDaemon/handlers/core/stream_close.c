/**
 * stream_close.c — stream_close command handler implementation
 *
 * Wire protocol:
 *   Request:  {"id":N,"cmd":"stream_close","stream":M}
 *   Response: {"id":N,"status":"ok"}
 *   Errors:   missing_stream_id, stream_not_found
 *
 * Resources required: none (releases resources held by the target stream).
 * Threading: main thread (FuriEventLoop).
 */

#include "stream_close.h"
#include "../../core/rpc_response.h"
#include "../../core/rpc_stream.h"
#include "../../core/rpc_json.h"

#include <furi.h>
#include <inttypes.h>

void stream_close_handler(uint32_t id, const char* json) {
    uint32_t stream_id = 0;
    if(!json_extract_uint32(json, "stream", &stream_id)) {
        rpc_send_error(id, "missing_stream_id", "stream_close");
        return;
    }

    int slot = stream_find_by_id(stream_id);
    if(slot < 0) {
        rpc_send_error(id, "stream_not_found", "stream_close");
        return;
    }

    stream_close_by_index((size_t)slot);
    FURI_LOG_I("RPC", "stream %" PRIu32 " closed", stream_id);

    rpc_send_ok(id, "stream_close");
}
