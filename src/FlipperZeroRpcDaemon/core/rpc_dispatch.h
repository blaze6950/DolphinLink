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

/** Handler function signature — all command handlers must match this. */
typedef void (*RpcHandler)(uint32_t request_id, const char* json);

/** One entry in the command registry. */
typedef struct {
    const char* name; /* Command name as it appears in the "cmd" JSON field */
    ResourceMask resources; /* Bitmask of resources required; 0 means none        */
    RpcHandler handler; /* Function to invoke when the command is matched      */
} RpcCommand;

/**
 * Parse @p json, look up the command, check resources, and invoke the handler.
 * Sends the appropriate error response if parsing fails or a resource is busy.
 * Must be called from the main thread only.
 */
void rpc_dispatch(const char* json);
