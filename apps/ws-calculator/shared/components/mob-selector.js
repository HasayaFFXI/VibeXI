// Zone/mob picker + VIT/DEF derivation panel.
//
// Previously this wrote straight into #vit/#def/#tLvl and then called the
// calculator's renderFSTRTiers() / renderAttackCurve() / renderPresetSlotStates()
// by name. Now it derives the stats and hands them to `onDerive`; the host page
// decides where they land, when derive() runs, and what to re-render.
FFXI.components.mobSelector = (function () {
  const { resolveAll, num, checked } = FFXI.core;

  // config:
  //   mount        { zoneSelect, mobSelect, mobMeta, output,
  //                  mLvl, sLvl, famVitRank, mainVitRank, subVitRank,
  //                  defRank, statMult, subCurveZone }
  //   defaultZone  zone id to preselect
  //   onDerive(result)  called with the FFXI.mobStats.derive() result
  //   onMobPicked(info) optional, called when a mob is chosen
  function create(config) {
    const m = resolveAll(config.mount || {});
    const MOB_DATA = FFXI.data.MOB_DATA;
    const { JOB_NAMES, JOB_VIT_RANK } = FFXI.data;
    const onDerive = config.onDerive || function () {};
    const onMobPicked = config.onMobPicked || function () {};

    function populateZones() {
      if (!m.zoneSelect) return;
      m.zoneSelect.innerHTML = '<option value="">— select zone —</option>';
      // Object.entries reorders integer-like keys numerically, so sort by name
      // explicitly.
      Object.entries(MOB_DATA.zones)
        .sort((a, b) => a[1].localeCompare(b[1]))
        .forEach(([zid, name]) => {
          const opt = document.createElement('option');
          opt.value = zid;
          opt.textContent = name;
          m.zoneSelect.appendChild(opt);
        });
    }

    function populateMobs(zoneId) {
      const sel = m.mobSelect;
      if (!sel) return;
      sel.innerHTML = '';
      const mobs = MOB_DATA.mobsByZone[zoneId] || [];
      if (!mobs.length) {
        sel.disabled = true;
        sel.innerHTML = '<option value="">— no data for this zone —</option>';
        return;
      }
      sel.disabled = false;
      sel.innerHTML = '<option value="">— select mob —</option>';
      mobs
        .map((mob, i) => i)
        .sort((a, b) => mobs[a][0].localeCompare(mobs[b][0]))
        .forEach(i => {
          const [name, minLvl, maxLvl] = mobs[i];
          const opt = document.createElement('option');
          opt.value = i;
          opt.textContent = `${name} (Lv ${minLvl === maxLvl ? minLvl : minLvl + '–' + maxLvl})`;
          sel.appendChild(opt);
        });
    }

    // Reads the rank/level fields and derives VIT + DEF.
    function derive() {
      const result = FFXI.mobStats.derive({
        mLvl: num(m.mLvl),
        sLvl: num(m.sLvl),
        famVitRank: m.famVitRank ? m.famVitRank.value : 3,
        mainVitRank: m.mainVitRank ? m.mainVitRank.value : 3,
        subVitRank: m.subVitRank ? m.subVitRank.value : 3,
        defRank: m.defRank ? m.defRank.value : 3,
        statMult: num(m.statMult, 1),
        preFiftyZone: checked(m.subCurveZone),
      });

      if (m.output) m.output.innerHTML = describe(result);
      onDerive(result);
      return result;
    }

    function describe(r) {
      let html =
        `VIT = <b>${r.VIT}</b> &nbsp;(f${r.fVIT} + m${r.mVIT} + s${r.sVIT}) &times; ${r.statMult}` +
        `<br>DEF = <b>${r.DEF}</b> &nbsp;8 + floor(VIT&times;0.5) + ${r.defBase}` +
        (r.defInterpolated ? `<span class="warn"> — E-rank DEF term is interpolated, not source-confirmed</span>` : '') +
        `<div class="chart-cap" style="margin-top:6px;">Applied to target stats automatically.</div>`;
      if (r.subjobApproximate) {
        html += `<div class="chart-cap">Note: the source's pre-level-50 subjob curve (rank-specific formulas) isn't ported here; this still uses the halved fallback, so pre-50-zone results are approximate.</div>`;
      }
      return html;
    }

    // --- wiring ---

    if (m.zoneSelect) {
      m.zoneSelect.addEventListener('change', (e) => {
        const zid = e.target.value;
        if (m.mobMeta) m.mobMeta.style.display = 'none';
        if (!zid) {
          if (m.mobSelect) {
            m.mobSelect.disabled = true;
            m.mobSelect.innerHTML = '<option value="">— select zone first —</option>';
          }
          return;
        }
        populateMobs(zid);
      });
    }

    if (m.mobSelect) {
      m.mobSelect.addEventListener('change', (e) => {
        const zid = m.zoneSelect ? m.zoneSelect.value : '';
        const idx = e.target.value;
        if (idx === '' || !zid) {
          if (m.mobMeta) m.mobMeta.style.display = 'none';
          return;
        }
        const [name, minLvl, maxLvl, speciesid, mJob, sJob] = MOB_DATA.mobsByZone[zid][idx];
        const sp = MOB_DATA.species[speciesid]; // [STR,DEX,VIT,AGI,INT,MND,CHR,ATT,DEF,ACC,EVA]

        const lvl = maxLvl || minLvl || 1;
        if (m.mLvl) m.mLvl.value = lvl;
        if (m.sLvl) m.sLvl.value = lvl;

        if (sp) {
          if (m.famVitRank) m.famVitRank.value = String(sp[2]);
          if (m.defRank) m.defRank.value = String(Math.min(sp[8], 5));
        }

        const mVitRank = JOB_VIT_RANK[mJob];
        const sVitRank = JOB_VIT_RANK[sJob];
        if (mVitRank && m.mainVitRank) m.mainVitRank.value = String(mVitRank);
        if (sVitRank && m.subVitRank) m.subVitRank.value = String(sVitRank);

        const mJobName = JOB_NAMES[mJob] || mJob;
        const sJobName = JOB_NAMES[sJob] || sJob;
        if (m.mobMeta) {
          m.mobMeta.style.display = 'block';
          m.mobMeta.innerHTML = `<span class="hint" style="display:block; color:var(--mist);">Level ${minLvl}${minLvl !== maxLvl ? '–' + maxLvl : ''} &middot; Main job ${mJobName} &middot; Sub job ${sJobName} &middot; Family VIT/DEF and main/sub-job VIT ranks auto-filled below (job ranks from LandSandBoat's fixed per-job JobGrades table, not the mob data).</span>`;
        }

        onMobPicked({ name, minLvl, maxLvl, speciesid, mJob, sJob, level: lvl });
      });
    }

    // derive() is called by the host, not by a button of this component's own.
    populateZones();
    if (config.defaultZone && m.zoneSelect) {
      m.zoneSelect.value = String(config.defaultZone);
      m.zoneSelect.dispatchEvent(new Event('change'));
    }

    return { derive, populateZones, populateMobs };
  }

  return { create };
})();
