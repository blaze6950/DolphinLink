// demos.js — JS interop helpers for the three demo pages.
// Loaded as an ES module via IJSRuntime.InvokeAsync("import", "./demos.js").

// ═══════════════════════════════════════════════════════════════════════════════
// LED Color Picker helpers
// ═══════════════════════════════════════════════════════════════════════════════

/**
 * Renders the HSV saturation/value gradient onto a canvas, given a hue (0-360).
 * The canvas is filled with a gradient: white (top-left) → pure hue (top-right) →
 * black (bottom).
 * @param {string} canvasId
 * @param {number} hue  0-360
 */
export function renderHsvGradient(canvasId, hue) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const { width, height } = canvas;

    // Fill with the pure hue first
    ctx.fillStyle = `hsl(${hue}, 100%, 50%)`;
    ctx.fillRect(0, 0, width, height);

    // White gradient left-to-right (saturation axis)
    const whiteGrad = ctx.createLinearGradient(0, 0, width, 0);
    whiteGrad.addColorStop(0, 'rgba(255,255,255,1)');
    whiteGrad.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = whiteGrad;
    ctx.fillRect(0, 0, width, height);

    // Black gradient top-to-bottom (value axis)
    const blackGrad = ctx.createLinearGradient(0, 0, 0, height);
    blackGrad.addColorStop(0, 'rgba(0,0,0,0)');
    blackGrad.addColorStop(1, 'rgba(0,0,0,1)');
    ctx.fillStyle = blackGrad;
    ctx.fillRect(0, 0, width, height);
}

/**
 * Renders the hue strip canvas (horizontal rainbow gradient).
 * @param {string} canvasId
 */
export function renderHueStrip(canvasId) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const { width, height } = canvas;

    const grad = ctx.createLinearGradient(0, 0, width, 0);
    for (let i = 0; i <= 360; i += 30) {
        grad.addColorStop(i / 360, `hsl(${i}, 100%, 50%)`);
    }
    ctx.fillStyle = grad;
    ctx.fillRect(0, 0, width, height);
}

/**
 * Attaches mouse/touch event listeners to the HSV gradient canvas.
 * On each pointer event, calls dotnet.invokeMethodAsync('OnHsvPick', sx, sy)
 * where sx/sy are in [0,1] relative coordinates.
 * @param {string} canvasId
 * @param {DotNetObjectReference} dotnet
 */
export function attachHsvPicker(canvasId, dotnet) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    let dragging = false;

    function getRelative(e) {
        const rect = canvas.getBoundingClientRect();
        const clientX = e.touches ? e.touches[0].clientX : e.clientX;
        const clientY = e.touches ? e.touches[0].clientY : e.clientY;
        const sx = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
        const sy = Math.max(0, Math.min(1, (clientY - rect.top) / rect.height));
        return { sx, sy };
    }

    function notify(e) {
        const { sx, sy } = getRelative(e);
        dotnet.invokeMethodAsync('OnHsvPick', sx, sy);
    }

    canvas.addEventListener('mousedown', e => { dragging = true; notify(e); });
    canvas.addEventListener('mousemove', e => { if (dragging) notify(e); });
    document.addEventListener('mouseup', () => { dragging = false; });

    canvas.addEventListener('touchstart', e => { e.preventDefault(); notify(e); }, { passive: false });
    canvas.addEventListener('touchmove',  e => { e.preventDefault(); notify(e); }, { passive: false });
}

/**
 * Attaches mouse/touch event listeners to the hue strip canvas.
 * On each pointer event, calls dotnet.invokeMethodAsync('OnHuePick', hueRatio)
 * where hueRatio is in [0,1].
 * @param {string} canvasId
 * @param {DotNetObjectReference} dotnet
 */
export function attachHuePicker(canvasId, dotnet) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    let dragging = false;

    function getX(e) {
        const rect = canvas.getBoundingClientRect();
        const clientX = e.touches ? e.touches[0].clientX : e.clientX;
        return Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    }

    function notify(e) {
        dotnet.invokeMethodAsync('OnHuePick', getX(e));
    }

    canvas.addEventListener('mousedown', e => { dragging = true; notify(e); });
    canvas.addEventListener('mousemove', e => { if (dragging) notify(e); });
    document.addEventListener('mouseup', () => { dragging = false; });

    canvas.addEventListener('touchstart', e => { e.preventDefault(); notify(e); }, { passive: false });
    canvas.addEventListener('touchmove',  e => { e.preventDefault(); notify(e); }, { passive: false });
}

// ═══════════════════════════════════════════════════════════════════════════════
// Screen Canvas helpers
// ═══════════════════════════════════════════════════════════════════════════════

const FLIPPER_W = 128;
const FLIPPER_H = 64;

/**
 * Renders the screen canvas from a flat bit-array (length = 128*64).
 * Pixels are scaled up by `scale`. Draws a grid overlay.
 * @param {string} canvasId
 * @param {Uint8Array | number[]} pixels  0=white, 1=black
 * @param {number} scale  integer pixel scale factor (e.g. 4)
 */
export function renderScreenCanvas(canvasId, pixels, scale) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');

    ctx.clearRect(0, 0, canvas.width, canvas.height);

    // Draw pixels
    for (let y = 0; y < FLIPPER_H; y++) {
        for (let x = 0; x < FLIPPER_W; x++) {
            const px = pixels[y * FLIPPER_W + x];
            ctx.fillStyle = px ? '#000000' : '#ffffff';
            ctx.fillRect(x * scale, y * scale, scale, scale);
        }
    }

    // Grid overlay (subtle)
    if (scale >= 4) {
        ctx.strokeStyle = 'rgba(0,0,0,0.08)';
        ctx.lineWidth = 0.5;
        for (let x = 0; x <= FLIPPER_W; x++) {
            ctx.beginPath();
            ctx.moveTo(x * scale, 0);
            ctx.lineTo(x * scale, FLIPPER_H * scale);
            ctx.stroke();
        }
        for (let y = 0; y <= FLIPPER_H; y++) {
            ctx.beginPath();
            ctx.moveTo(0, y * scale);
            ctx.lineTo(FLIPPER_W * scale, y * scale);
            ctx.stroke();
        }
    }
}

/**
 * Draws a preview shape (line/rect) on the canvas in blue, without modifying pixel state.
 * Used for drag feedback.
 * @param {string} canvasId
 * @param {Uint8Array | number[]} pixels  current pixel state
 * @param {number} scale
 * @param {string} tool   'line' | 'rect' | 'filledrect'
 * @param {number} x1  grid coordinates
 * @param {number} y1
 * @param {number} x2
 * @param {number} y2
 */
export function renderScreenCanvasWithPreview(canvasId, pixels, scale, tool, x1, y1, x2, y2) {
    renderScreenCanvas(canvasId, pixels, scale);

    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');

    ctx.strokeStyle = 'rgba(13,110,253,0.8)';
    ctx.fillStyle   = 'rgba(13,110,253,0.3)';
    ctx.lineWidth   = 2;

    const px1 = Math.min(x1, x2) * scale;
    const py1 = Math.min(y1, y2) * scale;
    const pw  = (Math.abs(x2 - x1) + 1) * scale;
    const ph  = (Math.abs(y2 - y1) + 1) * scale;

    if (tool === 'line') {
        ctx.beginPath();
        ctx.moveTo(x1 * scale + scale / 2, y1 * scale + scale / 2);
        ctx.lineTo(x2 * scale + scale / 2, y2 * scale + scale / 2);
        ctx.stroke();
    } else if (tool === 'filledrect') {
        ctx.fillRect(px1, py1, pw, ph);
        ctx.strokeRect(px1, py1, pw, ph);
    } else {
        ctx.strokeRect(px1, py1, pw, ph);
    }
}

/**
 * Attaches mouse event listeners to the screen canvas for drawing.
 * Calls dotnet methods:
 *   OnCanvasMouseDown(gridX, gridY)
 *   OnCanvasMouseMove(gridX, gridY)
 *   OnCanvasMouseUp(gridX, gridY)
 * @param {string} canvasId
 * @param {number} scale
 * @param {DotNetObjectReference} dotnet
 */
export function attachScreenCanvas(canvasId, scale, dotnet) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    function getGrid(e) {
        const rect = canvas.getBoundingClientRect();
        const clientX = e.touches ? e.touches[0].clientX : e.clientX;
        const clientY = e.touches ? e.touches[0].clientY : e.clientY;
        const gx = Math.max(0, Math.min(FLIPPER_W - 1, Math.floor((clientX - rect.left) / scale)));
        const gy = Math.max(0, Math.min(FLIPPER_H - 1, Math.floor((clientY - rect.top) / scale)));
        return { gx, gy };
    }

    canvas.addEventListener('mousedown', e => {
        e.preventDefault();
        const { gx, gy } = getGrid(e);
        dotnet.invokeMethodAsync('OnCanvasMouseDown', gx, gy);
    });

    canvas.addEventListener('mousemove', e => {
        const { gx, gy } = getGrid(e);
        dotnet.invokeMethodAsync('OnCanvasMouseMove', gx, gy);
    });

    canvas.addEventListener('mouseup', e => {
        const { gx, gy } = getGrid(e);
        dotnet.invokeMethodAsync('OnCanvasMouseUp', gx, gy);
    });

    document.addEventListener('mouseup', () => {
        dotnet.invokeMethodAsync('OnCanvasMouseUp', -1, -1);
    });

    canvas.addEventListener('touchstart', e => {
        e.preventDefault();
        const { gx, gy } = getGrid(e);
        dotnet.invokeMethodAsync('OnCanvasMouseDown', gx, gy);
    }, { passive: false });

    canvas.addEventListener('touchmove', e => {
        e.preventDefault();
        const { gx, gy } = getGrid(e);
        dotnet.invokeMethodAsync('OnCanvasMouseMove', gx, gy);
    }, { passive: false });

    canvas.addEventListener('touchend', e => {
        dotnet.invokeMethodAsync('OnCanvasMouseUp', -1, -1);
    });
}

// ═══════════════════════════════════════════════════════════════════════════════
// Snake game canvas renderer
// ═══════════════════════════════════════════════════════════════════════════════

/**
 * Renders the snake game onto a canvas.
 * @param {string} canvasId
 * @param {object} state  { snake: [{x,y}], food: {x,y}, cols: number, rows: number, gameOver: bool }
 */
export function renderSnake(canvasId, state) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');

    const { snake, food, cols, rows, gameOver } = state;
    const cellW = canvas.width  / cols;
    const cellH = canvas.height / rows;

    // Background
    ctx.fillStyle = '#1a1a2e';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    // Grid lines (subtle)
    ctx.strokeStyle = 'rgba(255,255,255,0.04)';
    ctx.lineWidth = 0.5;
    for (let x = 0; x <= cols; x++) {
        ctx.beginPath();
        ctx.moveTo(x * cellW, 0);
        ctx.lineTo(x * cellW, canvas.height);
        ctx.stroke();
    }
    for (let y = 0; y <= rows; y++) {
        ctx.beginPath();
        ctx.moveTo(0, y * cellH);
        ctx.lineTo(canvas.width, y * cellH);
        ctx.stroke();
    }

    // Food
    if (food) {
        ctx.fillStyle = '#f87171';
        const fx = food.x * cellW + 2;
        const fy = food.y * cellH + 2;
        const fw = cellW - 4;
        const fh = cellH - 4;
        ctx.beginPath();
        ctx.roundRect(fx, fy, fw, fh, 3);
        ctx.fill();
    }

    // Snake body
    if (snake && snake.length > 0) {
        for (let i = snake.length - 1; i >= 0; i--) {
            const seg = snake[i];
            const isHead = i === 0;
            const t = 1 - i / snake.length;
            // Head: bright green; tail: darker
            const g = Math.round(120 + t * 135);
            ctx.fillStyle = isHead ? '#22c55e' : `rgb(34, ${g}, 78)`;
            const sx = seg.x * cellW + 1;
            const sy = seg.y * cellH + 1;
            const sw = cellW - 2;
            const sh = cellH - 2;
            ctx.beginPath();
            ctx.roundRect(sx, sy, sw, sh, isHead ? 4 : 2);
            ctx.fill();
        }
    }

    // Game-over overlay
    if (gameOver) {
        ctx.fillStyle = 'rgba(0,0,0,0.55)';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.fillStyle = '#f87171';
        ctx.font = `bold ${Math.round(cellH * 2)}px sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText('GAME OVER', canvas.width / 2, canvas.height / 2 - cellH);
        ctx.fillStyle = '#d4d4d4';
        ctx.font = `${Math.round(cellH * 1.1)}px sans-serif`;
        ctx.fillText('Press OK to restart', canvas.width / 2, canvas.height / 2 + cellH);
    }
}
