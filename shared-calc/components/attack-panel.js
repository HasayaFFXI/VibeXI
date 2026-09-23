// pDIF-vs-ATT regime table + banded curve chart.
FFXI.components.attackPanel = (function () {
  const { resolveAll, scrollWithin } = FFXI.core;
  const C = FFXI.chart;

  // Splits the ATT axis at every wRatio breakpoint in wRatioCapPC, plus the two
  // points where the upper/lower bounds hit the pDIF cap.
  function computeRegimes(def, pDifFinalCap, maxAtt) {
    const upperCapReach = pDifFinalCap - 0.375; // wRatio at which upper bound plateaus
    const lowerCapReach = pDifFinalCap + 0.375; // wRatio at which lower bound plateaus

    let wPoints = [0, 0.38, 0.5, 0.7, 1.2, 1.25, 1.5, 1.51, 2.44];
    if (upperCapReach > 1.5) wPoints.push(upperCapReach);
    if (lowerCapReach > 2.44) wPoints.push(lowerCapReach);
    wPoints.push(maxAtt / def);
    wPoints = [...new Set(wPoints.map(w => Math.max(0, w)))]
      .filter(w => w * def <= maxAtt + 1)
      .sort((a, b) => a - b);

    const regimes = [];
    for (let i = 0; i < wPoints.length - 1; i++) {
      const w0 = wPoints[i], w1 = wPoints[i + 1];
      if (w1 - w0 < 1e-9) continue;
      const att0 = Math.round(w0 * def), att1 = Math.round(w1 * def);
      if (att1 <= att0) continue;
      const [l0, u0] = FFXI.damage.wRatioCapPC(w0, pDifFinalCap);
      const [l1, u1] = FFXI.damage.wRatioCapPC(w1, pDifFinalCap);
      const mid0 = (l0 + u0) / 2, mid1 = (l1 + u1) / 2;
      regimes.push({ att0, att1, mid0, mid1, slopePer100: (mid1 - mid0) / (att1 - att0) * 100 });
    }
    return regimes;
  }

  // config:
  //   mount      { table, canvas, tooltip, summary }
  //   getInputs  () => { att, def, weaponCap, dlPlus, dlPercent, note }
  //              dlPlus is already /100 and dlPercent already 1 + x/100
  //              note is optional -- what the passed-in ATT already includes,
  //              defaulting to "incl. food"
  //   height     chart height override in px; omit to use the laid-out height
  //              of the canvas (shared .chart-wrap sets it)
  function create(config) {
    const m = resolveAll(config.mount || {});
    const getInputs = config.getInputs;
    const height = config.height || 0;
    let chartParams = null;

    function render() {
      const { att, def, weaponCap, dlPlus, dlPercent, note } = getInputs();
      const pDifFinalCap = (weaponCap + dlPlus) * dlPercent;
      const maxAtt = Math.max(att * 2, def * (pDifFinalCap + 1), 200);
      const [curLower, curUpper] = FFXI.damage.pDifBoundsForATT(att, def, pDifFinalCap);
      const curMid = (curLower + curUpper) / 2;

      if (m.summary) {
        m.summary.innerHTML =
          `Target DEF ${def}, pDIF cap ${pDifFinalCap.toFixed(2)}. Effective ATT (${note || 'incl. food'}) ${Math.round(att)} &rarr; pDIF bounds <b style="color:var(--blade);">${curLower.toFixed(2)}&ndash;${curUpper.toFixed(2)}</b>.`;
      }

      const regimes = computeRegimes(def, pDifFinalCap, maxAtt);

      if (m.table) {
        const tbody = m.table.querySelector('tbody');
        tbody.innerHTML = '';
        regimes.forEach(r => {
          const isCurrent = att >= r.att0 && att < r.att1;
          const capped = r.slopePer100 < 0.001;
          const tr = document.createElement('tr');
          if (isCurrent) tr.className = 'current-tier';
          const rangeLabel = `${r.att0}–${r.att1 === Math.round(maxAtt) ? r.att1 + '+' : r.att1}`;
          const midLabel = `${r.mid0.toFixed(2)} → ${r.mid1.toFixed(2)}`;
          const slopeLabel = capped ? 'capped — no further gain' : `+${r.slopePer100.toFixed(3)}`;
          tr.innerHTML = `<td>${rangeLabel}</td><td>${midLabel}</td><td>${slopeLabel}</td>`;
          tbody.appendChild(tr);
        });
        scrollWithin(tbody.querySelector('.current-tier'));
      }

      draw(def, pDifFinalCap, att, curMid, maxAtt);
    }

    function draw(def, pDifFinalCap, currentAtt, currentMid, maxAtt) {
      if (!m.canvas) return;
      const { ctx, w, h } = C.setupCanvas(m.canvas, height);
      const box = C.plotBox(w, h, { left: 40, right: 12, top: 12, bottom: 24 });
      const minAtt = 1;
      const yMax = pDifFinalCap * 1.05;

      chartParams = { def, pDifFinalCap, minAtt, maxAtt, box };

      const xForAtt = C.scale(minAtt, maxAtt, box.left, box.right);
      const yForP = p => box.bottom - (Math.max(0, p) / yMax) * box.height;

      C.drawAxes(ctx, box);

      C.label(ctx, yMax.toFixed(1), box.left - 6, yForP(yMax) + 3, 'right');
      C.label(ctx, '0', box.left - 6, yForP(0) + 3, 'right');
      C.label(ctx, String(Math.round(minAtt)), box.left, box.bottom + 16, 'center');
      C.label(ctx, String(Math.round(maxAtt)), box.right, box.bottom + 16, 'center');

      // dashed cap line
      ctx.strokeStyle = C.COLORS.brass;
      ctx.setLineDash([4, 4]);
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(box.left, yForP(pDifFinalCap));
      ctx.lineTo(box.right, yForP(pDifFinalCap));
      ctx.stroke();
      ctx.setLineDash([]);

      // sample the upper/lower band
      const steps = 120;
      const upperPts = [], lowerPts = [];
      for (let i = 0; i <= steps; i++) {
        const a = minAtt + (maxAtt - minAtt) * (i / steps);
        const [lo, up] = FFXI.damage.pDifBoundsForATT(a, def, pDifFinalCap);
        upperPts.push([xForAtt(a), yForP(up)]);
        lowerPts.push([xForAtt(a), yForP(lo)]);
      }

      ctx.fillStyle = C.COLORS.bladeFill;
      ctx.beginPath();
      upperPts.forEach(([x, y], i) => i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y));
      for (let i = lowerPts.length - 1; i >= 0; i--) ctx.lineTo(lowerPts[i][0], lowerPts[i][1]);
      ctx.closePath();
      ctx.fill();

      ctx.strokeStyle = C.COLORS.blade;
      ctx.lineWidth = 2;
      ctx.beginPath();
      upperPts.forEach(([x, y], i) => i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y));
      ctx.stroke();

      ctx.strokeStyle = C.COLORS.bladeDim;
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      lowerPts.forEach(([x, y], i) => i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y));
      ctx.stroke();

      // current marker
      const mx = xForAtt(currentAtt), my = yForP(currentMid);
      ctx.fillStyle = C.COLORS.brass;
      ctx.beginPath();
      ctx.arc(mx, my, 4, 0, Math.PI * 2);
      ctx.fill();
      ctx.strokeStyle = C.COLORS.brassDim;
      ctx.beginPath();
      ctx.moveTo(mx, box.top);
      ctx.lineTo(mx, box.bottom);
      ctx.stroke();
    }

    C.attachTooltip(m.canvas, m.tooltip, (x, y) => {
      if (!chartParams) return null;
      const { def, pDifFinalCap, minAtt, maxAtt, box } = chartParams;
      if (!C.inPlot(box, x, y)) return null;
      const att = minAtt + ((x - box.left) / box.width) * (maxAtt - minAtt);
      const [lo, up] = FFXI.damage.pDifBoundsForATT(att, def, pDifFinalCap);
      return {
        left: x, top: y,
        html: `ATT <b>${Math.round(att).toLocaleString()}</b><br>pDIF ${lo.toFixed(2)}–${up.toFixed(2)} (mid ${((lo + up) / 2).toFixed(2)})`,
      };
    });

    // The host decides when to render; there is no self-wired recalculate button.
    return { render, computeRegimes, hasRendered: () => !!chartParams };
  }

  return { create, computeRegimes };
})();
