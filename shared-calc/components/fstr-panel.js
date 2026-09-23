// fSTR breakpoint table + step chart.
//
// Pulls its inputs through a `getInputs()` callback rather than reading fixed
// element ids, so the same panel can sit on a gear planner or a comparison page.
FFXI.components.fstrPanel = (function () {
  const { resolveAll, scrollWithin } = FFXI.core;
  const C = FFXI.chart;

  // Walks the STR range and collapses it into contiguous runs of equal fSTR.
  function computeTiers(vit, weaponRank, minStr, maxStr) {
    const tiers = [];
    let prevF = null;
    let tierStart = minStr;
    for (let s = minStr; s <= maxStr; s++) {
      const f = FFXI.damage.calcFSTR_PC(s, vit, weaponRank);
      if (prevF === null) {
        prevF = f; tierStart = s;
      } else if (f !== prevF) {
        tiers.push({ fstr: prevF, min: tierStart, max: s - 1 });
        prevF = f; tierStart = s;
      }
    }
    tiers.push({ fstr: prevF, min: tierStart, max: maxStr });
    return tiers;
  }

  // config:
  //   mount      { table, canvas, tooltip, summary }
  //   getInputs  () => { str, vit, weaponRank, note }
  //              note is optional -- what the passed-in STR already includes,
  //              defaulting to "incl. food"
  //   height     chart height override in px; omit to use the laid-out height
  //              of the canvas (shared .chart-wrap sets it)
  function create(config) {
    const m = resolveAll(config.mount || {});
    const getInputs = config.getInputs;
    const height = config.height || 0;
    let chartParams = null;

    function render() {
      const { str, vit, weaponRank, note } = getInputs();
      const minStr = 1;
      const maxStr = Math.max(str + 60, vit + 160);
      const tiers = computeTiers(vit, weaponRank, minStr, maxStr);
      const currentF = FFXI.damage.calcFSTR_PC(str, vit, weaponRank);

      if (m.summary) {
        m.summary.innerHTML =
          `Weapon rank ${weaponRank}, Target VIT ${vit}. Effective STR (${note || 'incl. food'}) ${str} &rarr; fSTR <b style="color:var(--blade);">${currentF}</b>.`;
      }

      if (m.table) {
        const tbody = m.table.querySelector('tbody');
        tbody.innerHTML = '';
        tiers.forEach(t => {
          const isCurrent = str >= t.min && str <= t.max;
          const tr = document.createElement('tr');
          if (isCurrent) tr.className = 'current-tier';
          const rangeLabel = t.min === t.max ? `${t.min}` : `${t.min}–${t.max === maxStr ? t.max + '+' : t.max}`;
          const deltaLabel = isCurrent ? 'current' : (str < t.min ? `+${t.min - str} STR` : `−${str - t.max} STR`);
          tr.innerHTML = `<td>${t.fstr}</td><td>${rangeLabel}</td><td>${deltaLabel}</td>`;
          tbody.appendChild(tr);
        });
        scrollWithin(tbody.querySelector('.current-tier'));
      }

      draw(tiers, str, currentF, minStr, maxStr);
    }

    function draw(tiers, currentStr, currentF, minStr, maxStr) {
      if (!m.canvas) return;
      const { ctx, w, h } = C.setupCanvas(m.canvas, height);
      const box = C.plotBox(w, h, { left: 40, right: 12, top: 12, bottom: 24 });

      const fVals = tiers.map(t => t.fstr);
      const fMin = Math.min(...fVals), fMax = Math.max(...fVals);

      const xForStr = C.scale(minStr, maxStr, box.left, box.right);
      const yForF = C.scale(fMin, fMax || fMin + 1, box.bottom, box.top);
      chartParams = { tiers, minStr, maxStr, box };

      C.drawAxes(ctx, box);

      C.label(ctx, String(fMax), box.left - 6, yForF(fMax) + 3, 'right');
      C.label(ctx, String(fMin), box.left - 6, yForF(fMin) + 3, 'right');
      C.label(ctx, String(minStr), box.left, box.bottom + 16, 'center');
      C.label(ctx, String(maxStr), box.right, box.bottom + 16, 'center');

      // step line
      ctx.strokeStyle = C.COLORS.blade;
      ctx.lineWidth = 2;
      ctx.beginPath();
      tiers.forEach((t, i) => {
        const x0 = xForStr(t.min), x1 = xForStr(t.max === maxStr ? maxStr : t.max + 1);
        const y = yForF(t.fstr);
        if (i === 0) ctx.moveTo(x0, y); else ctx.lineTo(x0, y);
        ctx.lineTo(x1, y);
      });
      ctx.stroke();

      // current marker
      const mx = xForStr(currentStr), my = yForF(currentF);
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
      const { tiers, minStr, maxStr, box } = chartParams;
      if (!C.inPlot(box, x, y)) return null;
      const str = Math.round(minStr + ((x - box.left) / box.width) * (maxStr - minStr));
      const tier = tiers.find(t => str >= t.min && str <= t.max) || tiers[tiers.length - 1];
      if (!tier) return null;
      const rangeLabel = tier.min === tier.max ? `${tier.min}` : `${tier.min}–${tier.max === maxStr ? tier.max + '+' : tier.max}`;
      return { left: x, top: y, html: `STR <b>${rangeLabel}</b><br>fSTR ${tier.fstr}` };
    });

    // The host decides when to render; there is no self-wired recalculate button.
    return { render, computeTiers, hasRendered: () => !!chartParams };
  }

  return { create, computeTiers };
})();
