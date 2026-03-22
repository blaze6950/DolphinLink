/**
 * stream_close.c — stream_close command handler implementation
 *
 * Wire protocol:
 *   Request:  {"c":1,"i":N,"s":M}
 *   Response: {"t":0,"i":N}
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

void stream_close_handler(uint32_t id, const char* json, size_t offset) {
    uint32_t stream_id = 0;
    JsonValue val;
    if(!json_find(json, "s", offset, &val)) {
        rpc_send_error(id, "missing_stream_id", "stream_close");
        return;
    }
    json_value_uint32(&val, &stream_id);

    int slot = stream_find_by_id(stream_id);
    if(slot < 0) {
        rpc_send_error(id, "stream_not_found", "stream_close");
        return;
    }

    stream_close_by_index((size_t)slot);
    FURI_LOG_I("RPC", "stream %" PRIu32 " closed", stream_id);

    rpc_send_ok(id, "stream_close");
}
