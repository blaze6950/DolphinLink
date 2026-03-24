/**
 * rpc_dispatch.h — RPC command registry and dispatcher
 *
 * rpc_dispatch() parses an incoming NDJSON request line, looks up the command
 * in the registry, performs the resource pre-check, and invokes the handler.
 *
 * To add a new command:
 *   1. Implement the handler in rpc_handlers.c and declare it in rpc_handlers.h.
 *   2. Add a row to the commands[] table in rpc_dispatch.c.
 */

#pragma once

#include "rpc_resource.h"

#include <stdint.h>
#include <stddef.h>

/**
 * Handler function signature -- all command handlers must match this.
 *
 * @param request_id  The request id extracted from the "i" field.
 * @param json        The full NDJSON line (NUL-terminated).
 * @param offset      Byte offset into json where command-specific args begin
 *                    (past the already-parsed "c" and "i" fields).  Pass as
 *                    the initial hint to json_find().
 */
typedef void (*RpcHandler)(uint32_t request_id, const char* json, size_t offset);

/** One entry in the command registry. Index in commands[] == command ID. */
typedef struct {
    const char* name; /* Command name string (for logging and capability negotiation) */
    ResourceMask resources; /* Bitmask of resources required; 0 means none        */
    RpcHandler handler; /* Function to invoke when the command is matched      */
} RpcCommand;

/**
 * Parse @p json (V1 format: {"c":<cmd_id>,"i":<id>,...}), look up the command
 * by integer ID, check resources, and invoke the handler.
 * Sends the appropriate error response if parsing fails or a resource is busy.
 * Must be called from the main thread only.
 */
void rpc_dispatch(const char* json);
