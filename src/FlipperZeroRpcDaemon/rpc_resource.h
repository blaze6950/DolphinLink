/**
 * rpc_resource.h — Hardware resource bitmask management
 *
 * Each RPC command declares which hardware resources it needs (BLE, SubGHz, IR, …).
 * The dispatcher checks availability before invoking handlers; releasing a stream
 * releases its associated resources.
 *
 * All functions must be called from the main thread only.
 */

#pragma once

#include <stdint.h>
#include <stdbool.h>

typedef uint32_t ResourceMask;

#define RESOURCE_BLE     (1u << 0) /* reserved — BLE GAP observer not exposed in FAP SDK */
#define RESOURCE_SUBGHZ  (1u << 1)
#define RESOURCE_IR      (1u << 2)
#define RESOURCE_NFC     (1u << 3)
#define RESOURCE_SPEAKER (1u << 4)
#define RESOURCE_RFID    (1u << 5)
#define RESOURCE_IBUTTON (1u << 6)

/* Module-level state — storage provided by flipper_zero_rpc_daemon.c */
extern ResourceMask active_resources;

/** Returns true if none of the bits in @p mask are currently held. */
static inline bool resource_can_acquire(ResourceMask mask) {
    return (active_resources & mask) == 0;
}

/** Marks the specified resources as acquired. */
static inline void resource_acquire(ResourceMask mask) {
    active_resources |= mask;
}

/** Releases the specified resources. */
static inline void resource_release(ResourceMask mask) {
    active_resources &= ~mask;
}

/** Resets all resources to idle.  Call during init and cleanup. */
static inline void resource_reset(void) {
    active_resources = 0;
}
