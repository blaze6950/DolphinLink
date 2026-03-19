/**
 * ui_canvas_session.h — Shared state for the host canvas session
 *
 * The host canvas session is a secondary ViewPort registered on the fullscreen
 * GUI layer.  While it is active (RESOURCE_GUI held), the daemon's own ViewPort
 * is hidden so the host has exclusive control of the screen.
 *
 * Draw operations are stored in a small fixed-size ring buffer (ui_draw_ops[]).
 * ui_flush triggers view_port_update(), which causes the Flipper GUI to call
 * the draw callback, which replays all buffered ops on the Canvas.
 *
 * All functions must be called from the main thread only.
 */

#pragma once

#include <gui/gui.h>
#include <gui/view_port.h>
#include <gui/canvas.h>
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

/* -------------------------------------------------------------------------
 * Draw operation types
 * ------------------------------------------------------------------------- */

typedef enum {
    UI_OP_CLEAR,
    UI_OP_DRAW_STR,
    UI_OP_DRAW_RECT,
    UI_OP_DRAW_LINE,
} UiOpType;

#define UI_STR_MAX  64 /* max string length for draw_str ops */
#define UI_OPS_MAX  32 /* ring buffer capacity */

typedef struct {
    UiOpType type;
    union {
        struct {
            uint8_t x;
            uint8_t y;
            uint8_t font; /* 0=primary, 1=secondary, 2=bold */
            char text[UI_STR_MAX];
        } draw_str;
        struct {
            uint8_t x;
            uint8_t y;
            uint8_t w;
            uint8_t h;
            bool filled;
        } draw_rect;
        struct {
            uint8_t x1;
            uint8_t y1;
            uint8_t x2;
            uint8_t y2;
        } draw_line;
    };
} UiDrawOp;

/* -------------------------------------------------------------------------
 * Session state — single instance, owned by ui_screen_acquire handler
 * ------------------------------------------------------------------------- */

typedef struct {
    ViewPort* viewport; /**< Secondary ViewPort (fullscreen layer) */
    Gui* gui; /**< GUI record — needed to remove the viewport on release */
    UiDrawOp ops[UI_OPS_MAX]; /**< Draw operation ring buffer */
    size_t op_count; /**< Number of valid ops in the buffer */
} UiCanvasSession;

/** Module-level singleton — valid only while RESOURCE_GUI is held. */
extern UiCanvasSession g_canvas_session;

/* -------------------------------------------------------------------------
 * Helpers called by ui_screen_acquire / ui_screen_release
 * ------------------------------------------------------------------------- */

/** Initialise the session, allocate the ViewPort and register it. */
void ui_canvas_session_init(Gui* gui);

/** Destroy the session and remove the ViewPort from the GUI. */
void ui_canvas_session_deinit(void);

/** Clear the draw op buffer. */
void ui_canvas_ops_clear(void);

/** Append a draw op (drops silently if buffer is full). */
void ui_canvas_op_push(const UiDrawOp* op);
