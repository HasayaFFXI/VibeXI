// Generic distribution histogram. Takes an array of numbers, so it isn't tied to
// damage -- any Monte-Carlo output on any page can render through it.
FFXI.components.histogram = (function () {
  const { resolveAll, minMax } = FFXI.core;
  const C = FFXI.chart;

  // config:
  //   mount    { canvas, tooltip }
  //   bins     bin count (default 28)
  //   height   chart height override in px; omit to use the laid-out height
  //   xLabel / yLabel  axis captions
  //   unit     noun used in the tooltip ("hit" -> "12 hits (3.1%)")
  function create(config) {
    const m = resolveAll(config.mount || {});
    const binCount = config.bins || 28;
    const height = config.height || 0;
    const xLabel = config.xLabel || 'Value';
    const yLabel = config.yLabel || 'Count';
    const unit = config.unit || 'sample';
    const pad = { left: 48, right: 10, top: 10, bottom: 22 };

    let bins = [];
    let total = 0;

    function draw(values) {
      if (!m.canvas) return;
      const { ctx, w, h } = C.setupCanvas(m.canvas, height);
      const box = C.plotBox(w, h, pad);

      bins = [];
      total = values.length;
      if (!values.length) return;

      const { min, max } = minMax(values);
      const range = Math.max(1, max - min);
      const counts = new Array(binCount).fill(0);
      values.forEach(v => {
        let idx = Math.floor(((v - min) / range) * binCount);
        if (idx >= binCount) idx = binCount - 1;
        counts[idx]++;
      });
      const maxCount = Math.max(...counts);
      const barW = box.width / binCount;

      C.drawAxes(ctx, box, C.COLORS.axisAlt);

      // y gridlines + ticks
      [0, 0.5, 1].forEach(frac => {
        const y = box.bottom - frac * box.height;
        C.label(ctx, String(Math.round(maxCount * frac)), box.left - 6, y, 'right', 'middle', C.SANS);
        if (frac > 0) {
          ctx.strokeStyle = C.COLORS.grid;
          ctx.beginPath();
          ctx.moveTo(box.left, y);
          ctx.lineTo(box.right, y);
          ctx.stroke();
        }
      });

      ctx.save();
      ctx.translate(12, box.top + box.height / 2);
      ctx.rotate(-Math.PI / 2);
      C.label(ctx, yLabel, 0, 0, 'center', 'middle', C.SANS);
      ctx.restore();

      counts.forEach((c, i) => {
        const barH = maxCount ? (c / maxCount) * box.height : 0;
        const x = box.left + i * barW;
        ctx.globalAlpha = 0.55 + 0.45 * (maxCount ? c / maxCount : 0);
        ctx.fillStyle = C.COLORS.blade;
        ctx.fillRect(x + 1, box.bottom - barH, barW - 2, barH);
        ctx.globalAlpha = 1;

        bins.push({
          x0: x, x1: x + barW,
          binMin: min + (i / binCount) * range,
          binMax: min + ((i + 1) / binCount) * range,
          count: c,
        });
      });

      C.label(ctx, Math.round(min).toLocaleString(), box.left, box.bottom + 5, 'left', 'top', C.SANS);
      C.label(ctx, Math.round((min + max) / 2).toLocaleString(), box.left + box.width / 2, box.bottom + 5, 'center', 'top', C.SANS);
      C.label(ctx, Math.round(max).toLocaleString(), box.right, box.bottom + 5, 'right', 'top', C.SANS);
      C.label(ctx, xLabel, box.left + box.width / 2, box.bottom + 15, 'center', 'top', C.SANS);
    }

    C.attachTooltip(m.canvas, m.tooltip, (x, y, rect) => {
      const bin = bins.find(b => x >= b.x0 && x < b.x1);
      if (!bin || bin.count === 0 || y > (rect.height - pad.bottom)) return null;
      const pct = total ? ((bin.count / total) * 100).toFixed(1) : '0.0';
      return {
        left: (bin.x0 + bin.x1) / 2,
        top: Math.max(0, y - 10),
        html: `<b>${Math.round(bin.binMin).toLocaleString()}–${Math.round(bin.binMax).toLocaleString()}</b> ${xLabel.toLowerCase()}<br>${bin.count.toLocaleString()} ${unit}${bin.count === 1 ? '' : 's'} (${pct}%)`,
      };
    });

    return { draw, hasData: () => total > 0 };
  }

  return { create };
})();
