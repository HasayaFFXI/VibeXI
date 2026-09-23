// Core weaponskill damage engine, ported from the server's weaponskills.lua and
// the C++ battleutils pDIF path.
//
// Everything in here is PURE: no DOM reads, no DOM writes, no element ids. That
// is the whole point of the extraction -- this is the layer that gets validated
// against the server Lua, so it has to be callable with a plain object from a
// console, a test harness, or any page.
FFXI.damage = (function () {
  const { clamp, randomInt, minMax } = FFXI.core;

  // xi.weaponskills.fTP (weaponskills.lua:1075). Returns 1 with no table or
  // below 1000 TP -- right for an fTP multiplier, wrong for an additive bonus,
  // which is what tpFactor below is for.
  function fTPInterp(tp, table) {
    if (!table || tp < 1000) return 1;
    if (tp >= 2000) return table[1] + (tp - 2000) * (table[2] - table[1]) / 1000;
    return table[0] + (tp - 1000) * (table[1] - table[0]) / 1000;
  }

  // xi.combat.physical.calculateTPfactor (physical_utilities.lua:451). The same
  // interpolation, but an absent table means *zero*. The server carries both and
  // uses them for different things; the no-table return value is the whole
  // difference, and it is the reason accVaries cannot just reuse fTPInterp.
  function tpFactor(tp, table) {
    if (!table) return 0;
    if (tp >= 2000) return table[1] + (tp - 2000) * (table[2] - table[1]) / 1000;
    if (tp >= 1000) return table[0] + (tp - 1000) * (table[1] - table[0]) / 1000;
    return table[0];
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
  // existing console-validation workflow still works unchanged. `atkMult` is
  // appended, never inserted, for that same reason -- it defaults to 1, so every
  // existing call site is unaffected.
  //
  // atkMult is the WS's `atkVaries` factor. physical_utilities.lua:646 applies
  // it to ATT *inside* pDIF -- max(1, floor(ATT * wsAttackMod * flourish)) --
  // so it moves the ratio, and with it which branch of the cap curve you land
  // on. It is not a flat multiplier on the damage.
  function calculateMeleePDIF(actorAttack, targetDefense, isCritical, applyLevelCorrection, levelDiff, weaponCap, dlPlus, dlPercent, critDmgBonus, atkMult) {
    const att = Math.max(1, Math.floor(actorAttack * (atkMult === undefined ? 1 : atkMult)));
    const wRatio = (att / Math.max(1, targetDefense)) + (isCritical ? 1 : 0);
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

  // The order xi.combat.physical.calculateWSC takes its multipliers in.
  const WSC_STATS = ['str', 'dex', 'vit', 'agi', 'int', 'mnd', 'chr'];

  // Multi-stat WSC (physical_utilities.lua:424). Each stat's term is floored
  // *separately* before the sum -- floor(STR x 0.20) + floor(DEX x 0.20), not
  // floor(STR x 0.20 + DEX x 0.20). That is worth a point either way on a WS
  // like Penta Thrust that splits its modifier across two stats, and it is the
  // kind of difference that only shows up as an off-by-one nobody can explain.
  //
  // gearMods are the WS_<STAT>_BONUS mods, in whole percent, added to the WS's
  // own multiplier before the multiply -- not to the product.
  function wscParts(stats, mods, gearMods) {
    const parts = {};
    WSC_STATS.forEach(k => {
      const mult = ((mods && mods[k]) || 0) + (((gearMods && gearMods[k]) || 0) / 100);
      if (mult === 0) return;
      parts[k] = Math.floor(((stats && stats[k]) || 0) * mult);
    });
    return parts;
  }

  function calcWSC(stats, mods, gearMods) {
    const parts = wscParts(stats, mods, gearMods);
    return Object.keys(parts).reduce((t, k) => t + parts[k], 0);
  }

  // Deterministic part of the WS: the per-hit base before fTP and pDIF.
  //
  // Two WSC paths. `p.wscMods` selects the multi-stat one and reads the actor's
  // stats from `p.stats`; without it the original single-`wscStat` path runs
  // exactly as before, which is what keeps ws-calculator's readParams() working
  // untouched.
  function calcMainBase(p) {
    const fSTR = calcFSTR_PC(p.str, p.vit, p.wRank);
    const alpha = calcAlpha(p.aLvl, p.adoulin);

    let wsc, parts = null;
    if (p.wscMods) {
      parts = wscParts(p.stats || { str: p.str }, p.wscMods, p.wscGearMods);
      wsc = Object.keys(parts).reduce((t, k) => t + parts[k], 0);
    } else {
      const wscStatVal = p.wscStat === 'str' ? p.str : p.vit;
      // weaponskills.lua: wsc is floored per-stat, then scaled by alpha inside
      // the mainBase floor.
      wsc = Math.floor(wscStatVal * ((p.wscWeight + p.wscGear) / 100));
    }

    const mainBase = Math.floor(p.wDmg + fSTR + (p.bonusWSmods || 0) + wsc * alpha);
    return { fSTR, wsc, alpha, mainBase, wscParts: parts };
  }

  // pDIF bounds for a given ATT, ignoring the random roll -- used by the attack
  // curve panel. Pass ATT already multiplied by atkMult if the WS has one.
  function pDifBoundsForATT(att, def, pDifFinalCap) {
    return wRatioCapPC(att / Math.max(1, def), pDifFinalCap);
  }

  // getMultiAttacks (weaponskills.lua:59). QA, else TA, else DA -- and each tier
  // gets its *own* roll in the server's elseif chain, so a failed QA check does
  // not hand its number down to TA.
  function rollMultiAttacks(p) {
    if (randomInt(1, 100) <= (p.qaRate || 0)) return 3;
    if (randomInt(1, 100) <= (p.taRate || 0)) return 2;
    if (randomInt(1, 100) <= (p.daRate || 0)) return 1;
    return 0;
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

    // 'X varies with TP', for attack and for accuracy. atkVaries goes through
    // fTPInterp (absent => 1.0, a no-op multiplier); accVaries through tpFactor
    // (absent => 0, a no-op addend). Both are resolved once -- they depend on TP
    // only, and TP is fixed for the whole weaponskill.
    const atkMult = p.atkVaries ? fTPInterp(p.tp, p.atkVaries) : 1;
    const accBonus = tpFactor(p.tp, p.accVaries);
    // physical_hit_rate.lua: hitdiff = (acc - eva) / 2, so an accuracy bonus is
    // worth half as many points of hit rate. Same conversion buffs.js applies to
    // Hasso's +10 ACC.
    const accPoints = accBonus / 2;

    // Hasso moves STR, which moves fSTR/WSC/mainBase, and its accuracy moves both
    // hit-rate clamps -- so all of that is resolved once per state rather than per
    // trial. Hit 1 is rolled with bonusAcc + 100 (getHitRate): 100 ACC == +50
    // points of hit rate, then clamped to the weapon's cap. Later hits use the
    // unmodified rate.
    const states = inStates.map(s => {
      const perState = Object.assign({}, p, { str: s.str });
      // On the multi-stat path the buff's STR has to reach p.stats too, or the
      // WSC term would keep using the unbuffed value.
      if (p.wscMods) perState.stats = Object.assign({}, p.stats, { str: s.str });
      const base = calcMainBase(perState);
      return {
        prob: s.prob,
        label: s.label,
        active: s.active || [],
        str: s.str,
        att: s.att,
        fSTR: base.fSTR, wsc: base.wsc, alpha: base.alpha, mainBase: base.mainBase,
        wscParts: base.wscParts,
        hitRate: clamp(s.hitRate + accPoints, 20, p.hitRateCap),
        firstHitRate: clamp(s.hitRate + accPoints + 50, 20, p.hitRateCap),
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
    let hits = 0, misses = 0, sumHitsLanded = 0, sumHitsSwung = 0;
    let sumAll = 0, sumHit = 0, sumPhys = 0, sumMagic = 0;

    for (let i = 0; i < trials; i++) {
      // One draw per weaponskill, not per hit: the buffs are either up for the
      // whole WS or they aren't.
      const st = drawState();
      st.trials++;

      const mainBase = st.mainBase;
      let physicalTotal = 0;
      let hitsLanded = 0;
      let hitsDone = 0;   // the server's hitsDone: swings attempted, misses included

      // One swing. Miss, crit and pDIF are each rolled per hit -- a miss does not
      // end the weaponskill, it just contributes nothing.
      const swing = (rate, ftp) => {
        hitsDone++;
        if (Math.random() * 100 > rate) return;
        hitsLanded++;
        const isCrit = Math.random() * 100 < p.critRate;
        const pdif = calculateMeleePDIF(st.att, p.def, isCrit, p.lvlCorrection, levelDiff, p.weaponCap, p.dlPlus, p.dlPercent, p.critDmg, atkMult);
        physicalTotal += mainBase * ftp * pdif;
      };

      // A hybrid WS forces the physical fTP to 1 + gear fTP regardless of ftpMod;
      // the table is consumed by the magic component instead.
      const firstFTP = (p.hybridOn ? 1 : fTPInterp(p.tp, p.ftpMod)) + p.gearFTP;
      // weaponskills.lua:434 -- fTP resets to a flat 1 after hit 1 unless the WS
      // sets multiHitfTP, in which case the whole thing, gear bonus included,
      // carries across every hit.
      const laterFTP = p.multiHitFTP ? firstFTP : 1;

      swing(st.firstHitRate, firstFTP);

      // Multi-attack is rolled after hit 1 and again after each later hit, but
      // stops contributing once 2 procs have landed (weaponskills.lua:494). Every
      // swing, multi-attack included, counts against the hard 8-hit ceiling.
      let multis = rollMultiAttacks(p);
      let procs = multis > 0 ? 1 : 0;

      for (let hit = 1; hit < p.numHits && hitsDone < 8; hit++) {
        swing(st.hitRate, laterFTP);
        if (procs < 2) {
          const extra = rollMultiAttacks(p);
          multis += extra;
          if (extra > 0) procs++;
        }
      }

      for (let m = 0; m < multis && hitsDone < 8; m++) {
        swing(st.hitRate, laterFTP);
      }

      sumHitsLanded += hitsLanded;
      sumHitsSwung += hitsDone;

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
      avgHitsSwung: sumHitsSwung / trials,
      trials, hits, misses,
      // The TP-varying factors, resolved once -- the UI wants to show them.
      atkMult, accBonus, accPoints,
      fSTR: modal.fSTR, wsc: modal.wsc, alpha: modal.alpha, mainBase: modal.mainBase,
      wscParts: modal.wscParts,
      mainBaseMin: Math.min.apply(null, baseVals),
      mainBaseMax: Math.max.apply(null, baseVals),
      // Per-state detail for the breakdown. avgAll is conditional on being in that
      // state, so it is directly comparable across states.
      states: states.map(s => ({
        label: s.label, active: s.active, prob: s.prob,
        str: s.str, att: s.att, hitRate: s.hitRate, firstHitRate: s.firstHitRate,
        mainBase: s.mainBase,
        trials: s.trials, hits: s.hits,
        avgAll: s.trials ? s.sumAll / s.trials : 0,
        avgHit: s.hits ? s.sumHit / s.hits : 0,
      })),
      landedVals,
    };
  }

  return {
    fTPInterp, tpFactor, calcFSTR_PC, wRatioCapPC, spikeRatioPC,
    calculateMeleePDIF, calcAlpha, calcWSC, wscParts, WSC_STATS,
    calcMainBase, pDifBoundsForATT, rollMultiAttacks, simulate,
  };
})();
