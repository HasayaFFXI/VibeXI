/*
 * chart.js -- canvas drawing for the three chart forms this app needs.
 *
 * Mark specs are fixed (dataviz house style) and deliberately not options:
 *   lines 2px round-joined - markers >= 8px with a 2px surface ring -
 *   bars capped at 24px with a 4px rounded data-end and a 2px surface gap -
 *   gridlines and axes hairline, solid, one step off the surface.
 *
 * Colors are read from CSS custom properties at draw time, so the light/dark
 * swap lives entirely in style.css and no palette is duplicated here.
 */
(function (global) {
  'use strict';

  var DPS = global.DPS || (global.DPS = {});
  var F = DPS.stats;

  var PAD = { top: 18, right: 76, bottom: 34, left: 68 };

  // ------------------------------------------------------------------- theme

  /* Both of these come from shared-ui/js/theme.js, which reads the live custom
     properties off :root -- so the palette lives in one stylesheet shared with
     ws_calculator and the light/dark swap needs no JS table here. `el` is accepted
     and ignored; the tokens are only ever defined on :root. */
  function theme(el) { return global.FFXITheme.chart(); }

  /* Eight fixed categorical slots, in the documented order. Never cycled. */
  function seriesColor(el, slot) { return global.FFXITheme.series(slot); }

  // ------------------------------------------------------------------ canvas

  function setup(canvas) {
    // Hover handlers close over the data that was drawn with them -- `line`'s
    // mouseleave repaints a saved snapshot of it. Detach both on every draw so
    // a handler left over from the previous data can never repaint it over the
    // new chart; each drawing path reinstalls its own before returning. A meter
    // reset is where this bit: the canvas cleared correctly, then the first
    // mouseleave put the pre-reset series straight back.
    canvas.onmousemove = null;
    canvas.onmouseleave = null;

    // The canvas's own window, not this one: a popped-out panel can be dragged
    // onto a second monitor with a different pixel ratio, and scaling it by the
    // main window's would leave the chart soft or clipped.
    var win = canvas.ownerDocument.defaultView || global;
    var dpr = win.devicePixelRatio || 1;
    var rect = canvas.getBoundingClientRect();
    var w = Math.max(1, Math.round(rect.width));
    var h = Math.max(1, Math.round(rect.height));
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
    var ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    return { ctx: ctx, w: w, h: h };
  }

  function roundRectPath(ctx, x, y, w, h, r) {
    r = Math.max(0, Math.min(r, h / 2, w / 2));
    ctx.beginPath();
    ctx.moveTo(x, y + h);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h);
    ctx.closePath();
  }

  /* Same shape rotated for horizontal bars: rounded right end, square left. */
  function roundRectPathH(ctx, x, y, w, h, r) {
    r = Math.max(0, Math.min(r, w, h / 2));
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h - r);
    ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    ctx.lineTo(x, y + h);
    ctx.closePath();
  }

  // ------------------------------------------------------------------- ticks

  function niceTicks(min, max, count) {
    if (!isFinite(min) || !isFinite(max) || max <= min) return [min || 0];
    var span = max - min;
    var raw = span / Math.max(1, count);
    var mag = Math.pow(10, Math.floor(Math.log(raw) / Math.LN10));
    var norm = raw / mag;
    var step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
    var out = [];
    for (var v = Math.ceil(min / step) * step; v <= max + step * 1e-9; v += step) {
      out.push(Math.round(v / step) * step);
    }
    return out;
  }

  var TIME_STEPS = [1, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400];

  function timeTicks(t0, t1, count) {
    var span = (t1 - t0) / 1000;
    var want = span / Math.max(1, count);
    var step = TIME_STEPS[TIME_STEPS.length - 1];
    for (var i = 0; i < TIME_STEPS.length; i++) {
      if (TIME_STEPS[i] >= want) { step = TIME_STEPS[i]; break; }
    }
    var out = [];
    var d = new Date(t0);
    var startSec = Math.ceil(
      (d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds()) / step) * step;
    var base = new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
    for (var t = base + startSec * 1000; t <= t1; t += step * 1000) {
      if (t >= t0) out.push(t);
    }
    return out;
  }

  // ------------------------------------------------------------ tooltip host

  function tipHost(canvas) {
    var wrap = canvas.parentNode;
    var tip = wrap.querySelector('.chart-tip');
    if (!tip) {
      // ownerDocument, not `document`: the wrap may be in a popped-out window.
      tip = canvas.ownerDocument.createElement('div');
      // `fade` keeps the tip laid out and hidden by opacity, so offsetWidth can
      // be measured before it is positioned. The shared default hides with
      // display:none, which would measure zero.
      tip.className = 'chart-tip fade';
      tip.setAttribute('role', 'status');
      wrap.appendChild(tip);
    }
    return tip;
  }

  function hideTip(tip) { tip.style.opacity = '0'; tip.style.visibility = 'hidden'; }

  function showTip(tip, html, x, y, wrapW) {
    tip.innerHTML = html;
    tip.style.visibility = 'visible';
    tip.style.opacity = '1';
    var tw = tip.offsetWidth;
    var left = x + 14;
    if (left + tw > wrapW - 4) left = x - tw - 14;
    if (left < 4) left = 4;
    tip.style.left = left + 'px';
    tip.style.top = Math.max(4, y - 12) + 'px';
  }

  // ---------------------------------------------------------- cumulative line

  /*
   * model: { times:[ms], series:[{name, values:Float64Array, color, slot}] }
   *
   * All series share one x grid, so the crosshair reads every line at the same
   * instant -- that is what the shared grid in stats.cumulative buys.
   */
  function line(canvas, model, opts) {
    opts = opts || {};
    var th = theme(canvas);
    var s = setup(canvas);
    var ctx = s.ctx;
    var tip = tipHost(canvas);

    var times = model.times || [];
    var series = (model.series || []).filter(function (x) { return x.visible !== false; });

    var plot = {
      x: PAD.left,
      y: PAD.top,
      w: Math.max(10, s.w - PAD.left - PAD.right),
      h: Math.max(10, s.h - PAD.top - PAD.bottom)
    };

    if (!times.length || !series.length) {
      ctx.fillStyle = th.muted;
      ctx.font = '13px ' + th.font;
      ctx.textAlign = 'center';
      ctx.fillText(opts.empty || 'No damage yet', s.w / 2, s.h / 2);
      hideTip(tip);
      return;
    }

    var t0 = times[0], t1 = times[times.length - 1];
    if (t1 === t0) t1 = t0 + 1000;

    var vmax = 0;
    for (var i = 0; i < series.length; i++) {
      var v = series[i].values;
      if (v.length && v[v.length - 1] > vmax) vmax = v[v.length - 1];
    }
    if (vmax <= 0) vmax = 1;

    var yTicks = niceTicks(0, vmax, 5);
    var yTop = Math.max(vmax, yTicks[yTicks.length - 1]);

    function px(t) { return plot.x + (t - t0) / (t1 - t0) * plot.w; }
    function py(v) { return plot.y + plot.h - (v / yTop) * plot.h; }

    // ---- grid + axes (hairline, solid, recessive)
    ctx.lineWidth = 1;
    ctx.font = '11px ' + th.font;
    ctx.fillStyle = th.muted;

    ctx.strokeStyle = th.grid;
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (i = 0; i < yTicks.length; i++) {
      var yy = Math.round(py(yTicks[i])) + 0.5;
      ctx.beginPath();
      ctx.moveTo(plot.x, yy);
      ctx.lineTo(plot.x + plot.w, yy);
      ctx.stroke();
      ctx.fillText(F.fmtCompact(yTicks[i]), plot.x - 10, yy);
    }

    var xTicks = timeTicks(t0, t1, Math.max(2, Math.floor(plot.w / 90)));
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    for (i = 0; i < xTicks.length; i++) {
      var xx = Math.round(px(xTicks[i])) + 0.5;
      ctx.fillText(F.fmtClock(xTicks[i]), xx, plot.y + plot.h + 10);
    }

    ctx.strokeStyle = th.axis;
    ctx.beginPath();
    ctx.moveTo(plot.x, Math.round(plot.y + plot.h) + 0.5);
    ctx.lineTo(plot.x + plot.w, Math.round(plot.y + plot.h) + 0.5);
    ctx.stroke();

    // ---- series
    ctx.lineWidth = 2;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    for (i = 0; i < series.length; i++) {
      var ser = series[i];
      ctx.strokeStyle = ser.color;
      ctx.beginPath();
      for (var j = 0; j < times.length; j++) {
        var X = px(times[j]), Y = py(ser.values[j]);
        if (j === 0) ctx.moveTo(X, Y); else ctx.lineTo(X, Y);
      }
      ctx.stroke();
    }

    // ---- end markers, 2px surface ring so overlaps stay legible
    for (i = 0; i < series.length; i++) {
      var se = series[i];
      var ex = px(times[times.length - 1]);
      var ey = py(se.values[se.values.length - 1]);
      ctx.beginPath();
      ctx.arc(ex, ey, 4.5, 0, Math.PI * 2);
      ctx.fillStyle = se.color;
      ctx.fill();
      ctx.lineWidth = 2;
      ctx.strokeStyle = th.surface;
      ctx.stroke();
    }

    // ---- direct end-labels, only when <= 4 series and they do not collide.
    // Text wears an ink token, never the series color; the dot carries identity.
    if (series.length <= 4) {
      var labels = series.map(function (se) {
        return {
          y: py(se.values[se.values.length - 1]),
          text: F.fmtCompact(se.values[se.values.length - 1])
        };
      }).sort(function (a, b) { return a.y - b.y; });

      var collide = false;
      for (i = 1; i < labels.length; i++) {
        if (labels[i].y - labels[i - 1].y < 14) { collide = true; break; }
      }
      if (!collide) {
        ctx.font = '600 11px ' + th.font;
        ctx.fillStyle = th.ink2;
        ctx.textAlign = 'left';
        ctx.textBaseline = 'middle';
        for (i = 0; i < labels.length; i++) {
          ctx.fillText(labels[i].text, plot.x + plot.w + 11, labels[i].y);
        }
      }
    }

    // ---- hover: nearest grid index, crosshair + every series at that instant
    var wrapW = canvas.parentNode.clientWidth;

    canvas.onmousemove = function (ev) {
      var r = canvas.getBoundingClientRect();
      var mx = ev.clientX - r.left, my = ev.clientY - r.top;
      if (mx < plot.x - 8 || mx > plot.x + plot.w + 8 || my < plot.y - 8 || my > plot.y + plot.h + 8) {
        hideTip(tip);
        return;
      }
      var frac = (mx - plot.x) / plot.w;
      var idx = Math.max(0, Math.min(times.length - 1, Math.round(frac * (times.length - 1))));

      var rows = series.slice().sort(function (a, b) {
        return b.values[idx] - a.values[idx];
      }).map(function (se) {
        return '<tr><td><i style="background:' + se.color + '"></i>' + esc(se.name) +
               '</td><td>' + F.fmtInt(se.values[idx]) + '</td></tr>';
      }).join('');

      showTip(tip,
        '<div class="chart-tip-head">' + F.fmtClock(times[idx]) + '</div>' +
        '<table>' + rows + '</table>',
        px(times[idx]), my, wrapW);

      drawCrosshair(idx);
    };

    canvas.onmouseleave = function () { hideTip(tip); redraw(); };

    // Crosshair is drawn on a second pass over a saved snapshot so hover does
    // not accumulate ink; snapshot is taken once per full draw.
    var snapshot = ctx.getImageData(0, 0, canvas.width, canvas.height);

    function redraw() {
      ctx.save();
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.putImageData(snapshot, 0, 0);
      ctx.restore();
    }

    function drawCrosshair(idx) {
      redraw();
      var X = Math.round(px(times[idx])) + 0.5;
      ctx.save();
      ctx.lineWidth = 1;
      ctx.strokeStyle = th.axis;
      ctx.beginPath();
      ctx.moveTo(X, plot.y);
      ctx.lineTo(X, plot.y + plot.h);
      ctx.stroke();
      for (var k = 0; k < series.length; k++) {
        var Y = py(series[k].values[idx]);
        ctx.beginPath();
        ctx.arc(X, Y, 4.5, 0, Math.PI * 2);
        ctx.fillStyle = series[k].color;
        ctx.fill();
        ctx.lineWidth = 2;
        ctx.strokeStyle = th.surface;
        ctx.stroke();
      }
      ctx.restore();
    }
  }

  // ------------------------------------------------------------ horizontal bars

  /*
   * rows: [{ label, value, color, sub }]. One bar per entity, entity color --
   * identity, not rank, so filtering never repaints the survivors.
   */
  function bars(canvas, rows, opts) {
    opts = opts || {};
    var th = theme(canvas);
    var s = setup(canvas);
    var ctx = s.ctx;
    var tip = tipHost(canvas);

    if (!rows.length) {
      ctx.fillStyle = th.muted;
      ctx.font = '13px ' + th.font;
      ctx.textAlign = 'center';
      ctx.fillText(opts.empty || 'No data', s.w / 2, s.h / 2);
      hideTip(tip);
      return;
    }

    var labelW = opts.labelWidth || 116;
    var valueW = 70;
    var left = labelW + 10;
    var right = s.w - valueW;
    var w = Math.max(10, right - left);

    var max = 0;
    for (var i = 0; i < rows.length; i++) max = Math.max(max, rows[i].value);
    if (max <= 0) max = 1;

    var band = s.h / rows.length;
    var barH = Math.min(24, Math.max(8, band - 12));   // cap at 24px; the rest is air

    ctx.font = '12px ' + th.font;
    ctx.textBaseline = 'middle';

    var geo = [];
    for (i = 0; i < rows.length; i++) {
      var r = rows[i];
      var cy = band * i + band / 2;
      var bw = Math.max(2, (r.value / max) * w);

      ctx.textAlign = 'right';
      ctx.fillStyle = th.ink2;
      ctx.fillText(clip(ctx, r.label, labelW), labelW, cy);

      ctx.fillStyle = r.color;
      roundRectPathH(ctx, left, cy - barH / 2, bw, barH, 4);
      ctx.fill();

      ctx.textAlign = 'left';
      ctx.fillStyle = th.ink;
      ctx.font = '600 12px ' + th.font;
      ctx.fillText(F.fmtCompact(r.value), left + bw + 8, cy);
      ctx.font = '12px ' + th.font;

      geo.push({ row: r, y0: band * i, y1: band * (i + 1) });
    }

    var wrapW = canvas.parentNode.clientWidth;
    canvas.onmousemove = function (ev) {
      var rc = canvas.getBoundingClientRect();
      var my = ev.clientY - rc.top, mx = ev.clientX - rc.left;
      for (var k = 0; k < geo.length; k++) {
        if (my >= geo[k].y0 && my < geo[k].y1) {
          var row = geo[k].row;
          showTip(tip,
            '<div class="chart-tip-head"><i style="background:' + row.color + '"></i>' +
            esc(row.label) + '</div>' +
            (row.sub || ('<table><tr><td>Damage</td><td>' + F.fmtInt(row.value) + '</td></tr></table>')),
            mx, my, wrapW);
          return;
        }
      }
      hideTip(tip);
    };
    canvas.onmouseleave = function () { hideTip(tip); };
  }

  // ------------------------------------------------------------- histogram

  /*
   * Single-series distribution. One color for every column (a value-ramp here
   * would double-encode height as hue), with min / mean / max called out by
   * rules rather than by a label on every column.
   */
  function histogram(canvas, dist, opts) {
    opts = opts || {};
    var th = theme(canvas);
    var s = setup(canvas);
    var ctx = s.ctx;
    var tip = tipHost(canvas);
    var color = opts.color || seriesColor(canvas, 0);

    var pad = { top: 22, right: 16, bottom: 34, left: 46 };
    var plot = {
      x: pad.left, y: pad.top,
      w: Math.max(10, s.w - pad.left - pad.right),
      h: Math.max(10, s.h - pad.top - pad.bottom)
    };

    if (!dist || !dist.bins.length) {
      ctx.fillStyle = th.muted;
      ctx.font = '13px ' + th.font;
      ctx.textAlign = 'center';
      ctx.fillText(opts.empty || 'No hits recorded', s.w / 2, s.h / 2);
      hideTip(tip);
      return;
    }

    var bins = dist.bins;
    var lo = bins[0].lo, hi = bins[bins.length - 1].hi;
    if (hi <= lo) hi = lo + 1;

    var cmax = 0;
    for (var i = 0; i < bins.length; i++) cmax = Math.max(cmax, bins[i].count);
    if (!cmax) cmax = 1;

    function bx(v) { return plot.x + (v - lo) / (hi - lo) * plot.w; }
    function by(c) { return plot.y + plot.h - (c / cmax) * plot.h; }

    // y grid
    ctx.lineWidth = 1;
    ctx.strokeStyle = th.grid;
    ctx.font = '11px ' + th.font;
    ctx.fillStyle = th.muted;
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    var yt = niceTicks(0, cmax, 4);
    for (i = 0; i < yt.length; i++) {
      var yy = Math.round(by(yt[i])) + 0.5;
      ctx.beginPath();
      ctx.moveTo(plot.x, yy);
      ctx.lineTo(plot.x + plot.w, yy);
      ctx.stroke();
      ctx.fillText(F.fmtInt(yt[i]), plot.x - 8, yy);
    }

    // Columns fill their bin: unlike a categorical bar, a histogram column's
    // width IS the bin interval, so the 24px cap must not apply here. Only the
    // 2px surface gap separates neighbours -- never a stroke.
    var slot = plot.w / bins.length;
    var bw = Math.max(1, slot - 2);
    for (i = 0; i < bins.length; i++) {
      if (!bins[i].count) continue;
      var x = plot.x + slot * i + 1;
      var y = by(bins[i].count);
      ctx.fillStyle = color;
      roundRectPath(ctx, x, y, bw, plot.y + plot.h - y, 4);
      ctx.fill();
    }

    // baseline
    ctx.strokeStyle = th.axis;
    ctx.beginPath();
    ctx.moveTo(plot.x, Math.round(plot.y + plot.h) + 0.5);
    ctx.lineTo(plot.x + plot.w, Math.round(plot.y + plot.h) + 0.5);
    ctx.stroke();

    // x labels: clean damage values across the range, ends always shown
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.fillStyle = th.muted;
    var xt = niceTicks(lo, hi, Math.max(2, Math.floor(plot.w / 110)));
    for (i = 0; i < xt.length; i++) {
      var tx = bx(xt[i]);
      // Drop interior ticks that would collide with the always-shown end labels.
      if (tx - plot.x < 34 || plot.x + plot.w - tx < 34) continue;
      ctx.fillText(F.fmtInt(xt[i]), tx, plot.y + plot.h + 10);
    }
    ctx.textAlign = 'left';
    ctx.fillText(F.fmtInt(lo), plot.x, plot.y + plot.h + 10);
    ctx.textAlign = 'right';
    ctx.fillText(F.fmtInt(hi), plot.x + plot.w, plot.y + plot.h + 10);

    // mean rule -- solid hairline in ink, labelled once
    var mx = Math.round(bx(dist.avg)) + 0.5;
    if (mx >= plot.x && mx <= plot.x + plot.w) {
      ctx.strokeStyle = th.ink2;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(mx, plot.y - 6);
      ctx.lineTo(mx, plot.y + plot.h);
      ctx.stroke();
      ctx.fillStyle = th.ink2;
      ctx.font = '600 11px ' + th.font;
      ctx.textBaseline = 'bottom';
      ctx.textAlign = mx > plot.x + plot.w - 46 ? 'right' : 'left';
      ctx.fillText('avg ' + F.fmtInt(dist.avg), mx + (ctx.textAlign === 'right' ? -4 : 4), plot.y - 6);
    }

    var wrapW = canvas.parentNode.clientWidth;
    canvas.onmousemove = function (ev) {
      var rc = canvas.getBoundingClientRect();
      var px2 = ev.clientX - rc.left, py2 = ev.clientY - rc.top;
      if (px2 < plot.x || px2 > plot.x + plot.w || py2 < plot.y - 10 || py2 > plot.y + plot.h + 10) {
        hideTip(tip); return;
      }
      var k = Math.max(0, Math.min(bins.length - 1, Math.floor((px2 - plot.x) / slot)));
      var b = bins[k];
      showTip(tip,
        '<div class="chart-tip-head">' + F.fmtInt(b.lo) + ' &ndash; ' + F.fmtInt(b.hi) + '</div>' +
        '<table><tr><td>Hits</td><td>' + b.count + '</td></tr>' +
        '<tr><td>Share</td><td>' + F.fmtNum(dist.count ? b.count / dist.count * 100 : 0, 1) + '%</td></tr></table>',
        px2, py2, wrapW);
    };
    canvas.onmouseleave = function () { hideTip(tip); };
  }

  // ------------------------------------------------------------------ helpers

  function clip(ctx, text, maxW) {
    text = String(text);
    if (ctx.measureText(text).width <= maxW) return text;
    var t = text;
    while (t.length > 1 && ctx.measureText(t + '…').width > maxW) t = t.slice(0, -1);
    return t + '…';
  }

  function esc(s) {
    return String(s).replace(/[&<>"]/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c];
    });
  }

  DPS.chart = {
    line: line,
    bars: bars,
    histogram: histogram,
    theme: theme,
    seriesColor: seriesColor,
    niceTicks: niceTicks,
    esc: esc
  };
})(window);
