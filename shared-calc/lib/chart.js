// Canvas chart primitives.
//
// The three charts in the original calculator each re-implemented dpr scaling,
// plot-box math, axis text and an almost byte-identical mousemove/mouseleave
// tooltip handler. That's all here now.
FFXI.chart = (function () {

  // Live view of the shared palette. Every key is a getter that re-reads the CSS
  // custom property off :root, so the stylesheet stays the single source of
  // truth and a theme swap needs no work here. `text` is the old name for the
  // secondary ink and is kept so component draw code reads unchanged.
  const COLORS = FFXITheme.liveColors();
  Object.defineProperty(COLORS, 'text', {
    enumerable: true,
    get: () => FFXITheme.v('--mist', '#8d92a3'),
  });

  const MONO = '10px JetBrains Mono, monospace';
  const SANS = '9.5px Inter, sans-serif';

  // Sizes the backing store for the device pixel ratio and clears it. Returns
  // the drawing context plus CSS-pixel dimensions.
  //
  // `height` is optional and normally omitted: the laid-out height of the canvas
  // wins, which is what keeps the JS from having to restate a number the shared
  // .chart-wrap rules already set.
  function setupCanvas(canvas, height) {
    const ctx = canvas.getContext('2d');
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.clientWidth, h = height || canvas.clientHeight;
    canvas.width = w * dpr;
    canvas.height = h * dpr;
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, w, h);
    return { ctx, w, h };
  }

  // pad: { left, right, top, bottom } -> plot box in CSS pixels.
  function plotBox(w, h, pad) {
    const left = pad.left, top = pad.top;
    const right = w - pad.right, bottom = h - pad.bottom;
    return { left, top, right, bottom, width: right - left, height: bottom - top };
  }

  // L-shaped axis lines.
  function drawAxes(ctx, box, color) {
    ctx.strokeStyle = color || COLORS.axis;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(box.left, box.top);
    ctx.lineTo(box.left, box.bottom);
    ctx.lineTo(box.right, box.bottom);
    ctx.stroke();
  }

  // Shared tooltip wiring. `resolve(x, y, rect)` runs on every mousemove and
  // returns { html, left, top } to show the tooltip there, or null/undefined to
  // hide it. Returns a detach function.
  function attachTooltip(canvas, tooltip, resolve) {
    if (!canvas || !tooltip) return function () {};

    const onMove = (e) => {
      const rect = canvas.getBoundingClientRect();
      const hit = resolve(e.clientX - rect.left, e.clientY - rect.top, rect);
      if (!hit) {
        tooltip.style.display = 'none';
        return;
      }
      tooltip.style.display = 'block';
      tooltip.style.left = hit.left + 'px';
      tooltip.style.top = hit.top + 'px';
      tooltip.innerHTML = hit.html;
    };
    const onLeave = () => { tooltip.style.display = 'none'; };

    canvas.addEventListener('mousemove', onMove);
    canvas.addEventListener('mouseleave', onLeave);
    return function detach() {
      canvas.removeEventListener('mousemove', onMove);
      canvas.removeEventListener('mouseleave', onLeave);
    };
  }

  // True when (x, y) is inside the plot box -- the bounds test all three charts
  // were doing by hand.
  function inPlot(box, x, y) {
    return x >= box.left && x <= box.right && y >= box.top && y <= box.bottom;
  }

  // Linear scale factory. Returns a function mapping domain -> pixel.
  function scale(d0, d1, p0, p1) {
    const span = (d1 - d0) || 1;
    return v => p0 + ((v - d0) / span) * (p1 - p0);
  }

  function label(ctx, text, x, y, align, baseline, font, color) {
    ctx.fillStyle = color || COLORS.text;
    ctx.font = font || MONO;
    ctx.textAlign = align || 'left';
    ctx.textBaseline = baseline || 'alphabetic';
    ctx.fillText(text, x, y);
  }

  return { COLORS, MONO, SANS, setupCanvas, plotBox, drawAxes, attachTooltip, inPlot, scale, label };
})();
