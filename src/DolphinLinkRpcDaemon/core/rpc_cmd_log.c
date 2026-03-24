/**
 * rpc_cmd_log.c — On-screen command log ring buffer implementation
 */

#include "rpc_cmd_log.h"

#include <string.h>
#include <stdio.h>

/* Ring buffer storage */
char cmd_log[CMD_LOG_LINES][CMD_LOG_LINE_LEN];
size_t cmd_log_next = 0;
size_t cmd_log_count = 0;

ViewPort* g_view_port = NULL;

void cmd_log_push(const char* entry) {
    snprintf(cmd_log[cmd_log_next], CMD_LOG_LINE_LEN, "%s", entry);
    cmd_log_next = (cmd_log_next + 1) % CMD_LOG_LINES;
    if(cmd_log_count < CMD_LOG_LINES) cmd_log_count++;
    if(g_view_port) view_port_update(g_view_port);
}

void cmd_log_reset(void) {
    memset(cmd_log, 0, sizeof(cmd_log));
    cmd_log_next = 0;
    cmd_log_count = 0;
}
