/**
 * rpc_cmd_log.h — On-screen command log ring buffer
 *
 * A small fixed-size ring buffer of recent command log entries that is
 * displayed on the Flipper screen by the GUI draw callback.
 *
 * Write side: cmd_log_push() — call from main thread only.
 * Read side:  cmd_log[], cmd_log_count, cmd_log_next — read by draw_callback
 *             (called from the GUI thread, but the Flipper display update is
 *             always triggered via view_port_update which is thread-safe).
 */

#pragma once

#include <stddef.h>
#include <gui/view_port.h>

#define CMD_LOG_LINES    4
#define CMD_LOG_LINE_LEN 40

/* Ring buffer storage — readable by the draw callback */
extern char cmd_log[CMD_LOG_LINES][CMD_LOG_LINE_LEN];
extern size_t cmd_log_next; /* index of the next slot to overwrite */
extern size_t cmd_log_count; /* total entries written, saturates at CMD_LOG_LINES */

/**
 * ViewPort pointer used to trigger a redraw after each push.
 * Set by rpc_gui_setup(); cleared by rpc_gui_teardown().
 */
extern ViewPort* g_view_port;

/** Append an entry to the ring buffer and request a screen redraw. */
void cmd_log_push(const char* entry);

/** Reset the ring buffer to empty (call during init). */
void cmd_log_reset(void);
