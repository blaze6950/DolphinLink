// rpc-console.js — drag-to-resize and scroll helpers for the RPC Console footer.

/**
 * Initialises drag-to-resize on the console footer.
 * @param {DotNetObjectReference} dotnet - Blazor DotNet reference (unused, reserved)
 * @param {string} handleId   - element id of the drag handle bar
 * @param {string} logId      - element id of the scrollable log area
 * @param {number} minHeight  - minimum log-area height in px
 * @param {number} maxRatio   - maximum fraction of viewport height for the log area (0–1)
 */
export function initResize(dotnet, handleId, logId, minHeight, maxRatio) {
    const handle = document.getElementById(handleId);
    if (!handle) return;

    let startY = 0;
    let startH = 0;

    function getLog() { return document.getElementById(logId); }

    function applyHeight(h) {
        const log = getLog();
        if (!log) return;
        const max = Math.floor(window.innerHeight * maxRatio);
        const clamped = Math.max(minHeight, Math.min(max, h));
        log.style.height = clamped + 'px';
        // Keep content from hiding behind the footer
        syncPadding();
    }

    function syncPadding() {
        const footer = handle.closest('.rpc-console-footer');
        if (!footer) return;
        const h = footer.offsetHeight;
        document.querySelectorAll('.content').forEach(el => {
            el.style.paddingBottom = (h + 16) + 'px';
        });
    }

    // ── Mouse ────────────────────────────────────────────────────────────────

    function onMouseMove(e) {
        const delta = startY - e.clientY;
        applyHeight(startH + delta);
    }

    function onMouseUp() {
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
        document.body.style.userSelect = '';
        document.body.style.cursor = '';
    }

    handle.addEventListener('mousedown', e => {
        e.preventDefault();
        const log = getLog();
        startY = e.clientY;
        startH = log ? log.offsetHeight : 260;
        document.body.style.userSelect = 'none';
        document.body.style.cursor = 'ns-resize';
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
    });

    // ── Touch ────────────────────────────────────────────────────────────────

    function onTouchMove(e) {
        if (e.touches.length !== 1) return;
        const delta = startY - e.touches[0].clientY;
        applyHeight(startH + delta);
    }

    function onTouchEnd() {
        document.removeEventListener('touchmove', onTouchMove);
        document.removeEventListener('touchend', onTouchEnd);
    }

    handle.addEventListener('touchstart', e => {
        if (e.touches.length !== 1) return;
        const log = getLog();
        startY = e.touches[0].clientY;
        startH = log ? log.offsetHeight : 260;
        document.addEventListener('touchmove', onTouchMove, { passive: false });
        document.addEventListener('touchend', onTouchEnd);
    }, { passive: true });

    // Initial sync so CSS padding matches the JS-controlled height.
    syncPadding();
}

/**
 * Scrolls the log element to its bottom.
 * @param {string} logId
 */
export function scrollToBottom(logId) {
    const el = document.getElementById(logId);
    if (el) el.scrollTop = el.scrollHeight;
}

/**
 * Re-syncs the content padding to the current footer height.
 * Call after expand/collapse transitions.
 * @param {string} handleId
 */
export function syncContentPadding(handleId) {
    const handle = document.getElementById(handleId);
    if (!handle) return;
    const footer = handle.closest('.rpc-console-footer');
    if (!footer) return;
    const h = footer.offsetHeight;
    document.querySelectorAll('.content').forEach(el => {
        el.style.paddingBottom = (h + 16) + 'px';
    });
}
