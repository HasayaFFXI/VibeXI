// Core weaponskill damage engine, ported from the server's weaponskills.lua and
// the C++ battleutils pDIF path.
//
// Everything in here is PURE: no DOM reads, no DOM writes, no element ids. That
// is the whole point of the extraction -- this is the layer that gets validated
// against the server Lua, so it has to be callable with a plain object from a
// console, a test harness, or any page.
FFXI.damage = (function () {
  const { clamp, randomInt, minMax } = FFXI.core;

  function fTPInterp(tp, table) {
    if (!table || tp < 1000) return 1;
    if (tp >= 2000) return table[1] + (tp - 2000) * (table[2] - table[1]) / 1000;
    return table[0] + (tp - 1000) * (table[1] - table[0]) / 1000;
  }

  function calcFSTR_PC(str, vit, weaponRank) {
    let statDiff = str - vit;
    const statLower = (7 + weaponRank * 2) * -2;
    const statUpper = (14 + weaponRank * 2) * 2;
    statDiff = clamp(statDiff, statLower, statUpper);

    let fSTR;
    if (statDiff >= 12) fSTR = statDiff + 4;
    else if (statDiff >= 6) fSTR = statDiff + 6;
    else if (statDiff >= 1) fSTR = statDiff + 7;
    else if (statDiff >= -2) fSTR = statDiff + 8;
    else if (statDiff >= -7) fSTR = statDiff + 9;
    else if (statDiff >= -15) fSTR = statDiff + 10;
    else if (statDiff >= -21) fSTR = statDiff + 12;
    else fSTR = statDiff + 13;

    const upperCap = weaponRank + 8;
    const lowerCap = weaponRank === 0 ? -1 : -weaponRank;
    return clamp(fSTR / 4, lowerCap, upperCap);
  }

  function wRatioCapPC(wRatio, pDifFinalCap) {
    let upper, lower;
    if (wRatio < 0.5) upper = wRatio + 0.5;
    else if (wRatio < 0.7) upper = 1;
    else if (wRatio < 1.2) upper = wRatio + 0.3;
    else if (wRatio < 1.5) upper = wRatio + wRatio * 0.25;
    else upper = Math.min(wRatio + 0.375, pDifFinalCap);

    if (wRatio < 0.38) lower = 0;
    else if (wRatio < 1.25) lower = wRatio * 1176 / 1024 - 448 / 1024;
    else if (wRatio < 1.51) lower = 1;
    else if (wRatio < 2.44) lower = wRatio * 1176 / 1024 - 775 / 1024;
    else lower = Math.min(wRatio - 0.375, pDifFinalCap);

    return [lower, upper];
  }

  function spikeRatioPC(wRatio) {
    if (wRatio > 0.5 && wRatio < 1.5) {
      const s = (0.5 - Math.abs(wRatio - 1)) * 1.2;
      return clamp(s, 0, 1 / 3);
    }
    return 0;
  }

  // Positional signature kept identical to the pre-refactor global so the
  // existing console-validation workflow still works unchanged.
  function calculateMeleePDIF(actorAttack, targetDefense, isCritical, applyLevelCorrection, levelDiff, weaponCap, dlPlus, dlPercent, critDmgBonus) {
    const wRatio = (actorAttack / Math.max(1, targetDefense)) + (isCritical ? 1 : 0);
    const pDifFinalCap = (weaponCap + dlPlus) * dlPercent + (isCritical ? 1 : 0);

    if (Math.random() <= spikeRatioPC(wRatio)) return 1.0;

    const [lowerCap, upperCap] = wRatioCapPC(wRatio, pDifFinalCap);

    // Players: no positive level correction, only negative
    const levelDifFactor = applyLevelCorrection ? Math.min(0, levelDiff * 3 / 64) : 0;

    const upperMax = Math.random() < 0.5 ? 0.5 : 0;
    const upperBound = Math.max(upperCap + levelDifFactor, upperMax);
    const lowerBound = Math.max(lowerCap + levelDifFactor, 0);

    if (upperBound === 0) return 0;

    let pDif = lowerBound + Math.random() * (upperBound - lowerBound);
    const meleeRandom = 1 + randomInt(0, 5) * 0.01;
    pDif *= meleeRandom;

    if (isCritical) {
      pDif *= (100 + critDmgBonus) / 100;
    }
    return pDif;
  }

  // Legacy WSC alpha. Only applies when USE_ADOULIN_WEAPON_SKILL_CHANGES is
  // false; Adoulin-era servers dropped alpha entirely.
  function calcAlpha(level, adoulin) {
    if (adoulin) return 1;
    if (level > 75) return 0.85;
    if (level > 59) return 0.9 - Math.floor((level - 60) / 2) / 100;
    if (level > 5) return 1 - Math.floor(level / 6) / 100;
    return 1;
  }

  // Deterministic part of the WS: the per-hit base before fTP and pDIF.
  function calcMainBase(p) {
    const fSTR = calcFSTR_PC(p.str, p.vit, p.wRank);
    const wscStatVal = p.wscStat === 'str' ? p.str : p.vit;
    const alpha = calcAlpha(p.aLvl, p.adoulin);
    // weaponskills.lua: wsc is floored per-stat, then scaled by alpha inside the
    // mainBase floor.
    const wsc = Math.floor(wscStatVal * ((p.wscWeight + p.wscGear) / 100));
    const mainBase = Math.floor(p.wDmg + fSTR + wsc * alpha);
    return { fSTR, wsc, alpha, mainBase };
  }

  // pDIF bounds for a given ATT, ignoring the random roll -- used by the attack
  // curve panel.
  function pDifBoundsForATT(att, def, pDifFinalCap) {
    return wRatioCapPC(att / Math.max(1, def), pDifFinalCap);
  }

  // Monte-Carlo the whole weaponskill. `p` is the parameter object; `trials` is
  // clamped by the caller. Returns summary stats plus the landed-hit samples so
  // a histogram can be drawn from them.
  function simulate(p, trials) {
    // Job-ability buffs arrive as `p.buffStates`: an exact discrete distribution
    // over buff states from FFXI.buffs.resolveStates, each carrying a probability
    // plus the already-resolved effective stats for that state. The engine stays
    // ignorant of which abilities those are. Without it, the sim runs a single
    // state built from p.str / p.att / p.hitRate -- exactly what it did before
    // buffs existed, so the old console-validation calls are unaffected.
    const inStates = (p.buffStates && p.buffStates.length)
      ? p.buffStates
      : [{ prob: 1, label: 'no buffs', active: [], str: p.str, att: p.att, hitRate: p.hitRate }];

    const levelDiff = p.aLvl - p.tLvl;

    // addBonusesAbility: mab multiplier, floored at 0. No buff here touches it.
    const mabMult = Math.max(0, (100 + p.matt) / (100 + p.mdef));

    // Hasso moves STR, which moves fSTR/WSC/mainBase, and its accuracy moves both
    // hit-rate clamps -- so all of that is resolved once per state rather than per
    // trial. Hit 1 is rolled with bonusAcc + 100 (getHitRate): 100 ACC == +50
    // points of hit rate, then clamped to the weapon's cap. Later hits use the
    // unmodified rate.
    const states = inStates.map(s => {
      const base = calcMainBase(Object.assign({}, p, { str: s.str }));
      return {
        prob: s.prob,
        label: s.label,
        active: s.active || [],
        str: s.str,
        att: s.att,
        fSTR: base.fSTR, wsc: base.wsc, alpha: base.alpha, mainBase: base.mainBase,
        hitRate: clamp(s.hitRate, 20, p.hitRateCap),
        firstHitRate: clamp(s.hitRate + 50, 20, p.hitRateCap),
        trials: 0, hits: 0, sumAll: 0, sumHit: 0,
      };
    });

    // Cumulative weights, so one roll per trial draws a state. Scaling the roll by
    // the running total rather than assuming it is 1 keeps this correct if a caller
    // hands over weights that don't quite normalise.
    const cum = [];
    let totalWeight = 0;
    states.forEach(s => { totalWeight += s.prob; cum.push(totalWeight); });
    const single = states.length === 1 ? states[0] : null;

    function drawState() {
      if (single) return single;
      const r = Math.random() * totalWeight;
      for (let i = 0; i < cum.length; i++) {
        if (r < cum[i]) return states[i];
      }
      return states[states.length - 1];
    }

    const landedVals = [];
    let hits = 0, misses = 0, sumHitsLanded = 0;
    let sumAll = 0, sumHit = 0, sumPhys = 0, sumMagic = 0;

    for (let i = 0; i < trials; i++) {
      // One draw per weaponskill, not per hit: the buffs are either up for the
      // whole WS or they aren't.
      const st = drawState();
      st.trials++;

      const mainBase = st.mainBase;
      let physicalTotal = 0;
      let hitsLanded = 0;

      for (let hit = 0; hit < p.numHits; hit++) {
        // Each hit rolls its own miss check.
        if (Math.random() * 100 > (hit === 0 ? st.firstHitRate : st.hitRate)) continue;
        hitsLanded++;

        const isCrit = Math.random() * 100 < p.critRate;
        // A hybrid WS forces the physical fTP to 1 + gear fTP regardless of
        // ftpMod; the table is consumed by the magic component instead. Non-first
        // hits reset to exactly 1 unless the WS sets multiHitfTP (which also
        // drops the gear bonus).
        let ftp;
        if (hit === 0 || p.multiHitFTP) {
          ftp = (p.hybridOn ? 1 : fTPInterp(p.tp, p.ftpMod)) + p.gearFTP;
        } else {
          ftp = 1;
        }
        const pdif = calculateMeleePDIF(st.att, p.def, isCrit, p.lvlCorrection, levelDiff, p.weaponCap, p.dlPlus, p.dlPercent, p.critDmg);
        physicalTotal += mainBase * ftp * pdif;
      }

      sumHitsLanded += hitsLanded;

      if (hitsLanded === 0) {
        misses++;
        continue;
      }
      hits++;
      st.hits++;

      // WS damage bonus lands on the physical total *before* the magic component
      // is derived from it, then applies a second time to the magic itself.
      physicalTotal = Math.floor(physicalTotal * (100 + p.wsdPercent) / 100);

      let magicTotal = 0;
      if (p.hybridOn) {
        const ftpMagic = fTPInterp(p.tp, p.ftpMod);
        magicTotal = Math.floor(physicalTotal * ftpMagic + p.magicDmg);
        magicTotal = Math.floor(magicTotal * (100 + p.wsdPercent) / 100);
        magicTotal = Math.floor(magicTotal * mabMult);
        magicTotal = Math.floor(magicTotal + p.gearFTP * physicalTotal);
        magicTotal = Math.floor(magicTotal * p.resistMult);
      }

      const total = physicalTotal + magicTotal;

      sumPhys += physicalTotal;
      sumMagic += magicTotal;
      sumAll += total;
      sumHit += total;
      st.sumAll += total;
      st.sumHit += total;
      landedVals.push(total);
    }

    const avgAll = sumAll / trials;
    const avgHit = hits > 0 ? sumHit / hits : 0;
    const { min, max } = minMax(landedVals);

    let variance = 0;
    landedVals.forEach(v => variance += (v - avgHit) ** 2);
    variance = landedVals.length ? variance / landedVals.length : 0;

    // The modal state -- resolveStates returns them most-probable-first, and with
    // no buffs there is only one, which is why the top-level fSTR/wsc/alpha/
    // mainBase keys still mean what they always did for an unbuffed run.
    const modal = states[0];
    const baseVals = states.map(s => s.mainBase);

    return {
      avgAll, avgHit, min, max,
      avgPhys: hits ? sumPhys / hits : 0,
      avgMagic: hits ? sumMagic / hits : 0,
      std: Math.sqrt(variance),
      missRate: misses / trials,
      avgHitsLanded: sumHitsLanded / trials,
      trials, hits, misses,
      fSTR: modal.fSTR, wsc: modal.wsc, alpha: modal.alpha, mainBase: modal.mainBase,
      mainBaseMin: Math.min.apply(null, baseVals),
      mainBaseMax: Math.max.apply(null, baseVals),
      // Per-state detail for the breakdown. avgAll is conditional on being in that
      // state, so it is directly comparable across states.
      states: states.map(s => ({
        label: s.label, active: s.active, prob: s.prob,
        str: s.str, att: s.att, hitRate: s.hitRate, mainBase: s.mainBase,
        trials: s.trials, hits: s.hits,
        avgAll: s.trials ? s.sumAll / s.trials : 0,
        avgHit: s.hits ? s.sumHit / s.hits : 0,
      })),
      landedVals,
    };
  }

  return {
    fTPInterp, calcFSTR_PC, wRatioCapPC, spikeRatioPC,
    calculateMeleePDIF, calcAlpha, calcMainBase, pDifBoundsForATT, simulate,
  };
})();
