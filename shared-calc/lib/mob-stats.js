// Mob VIT / DEF derivation, ported from mobutils.cpp. Pure -- the DOM wiring
// lives in components/mob-selector.js.
FFXI.mobStats = (function () {

  function getBaseToRank(rank, lvl) {
    switch (+rank) {
      case 1: return 5 + Math.floor((lvl - 1) * 50 / 100); // A
      case 2: return 4 + Math.floor((lvl - 1) * 45 / 100); // B
      case 3: return 4 + Math.floor((lvl - 1) * 40 / 100); // C
      case 4: return 3 + Math.floor((lvl - 1) * 35 / 100); // D
      case 5: return 3 + Math.floor((lvl - 1) * 30 / 100); // E
      case 6: return 2 + Math.floor((lvl - 1) * 25 / 100); // F
      case 7: return 2 + Math.floor((lvl - 1) * 20 / 100); // G
      default: return 0;
    }
  }

  // mobutils.cpp GetBaseDefEva -- the shared f(level, rank) curve behind both
  // base DEF and base EVA.
  //
  // returns { value, interpolated }
  function getBaseDefEva(rank, lvl) {
    rank = +rank;
    if (lvl > 50) {
      switch (rank) {
        case 1: return { value: Math.floor(153 + (lvl - 50) * 5.0), interpolated: false };
        case 2: return { value: Math.floor(147 + (lvl - 50) * 4.9), interpolated: false };
        case 3: return { value: Math.floor(142 + (lvl - 50) * 4.8), interpolated: false };
        case 4: return { value: Math.floor(136 + (lvl - 50) * 4.7), interpolated: false };
        case 5: return { value: Math.floor(126 + (lvl - 50) * 4.5), interpolated: false };
      }
    } else {
      switch (rank) {
        case 1: return { value: Math.floor(6 + (lvl - 1) * 3.0), interpolated: false };
        case 2: return { value: Math.floor(5 + (lvl - 1) * 2.9), interpolated: false };
        case 3: return { value: Math.floor(5 + (lvl - 1) * 2.8), interpolated: false };
        case 4: return { value: Math.floor(4 + (lvl - 1) * 2.7), interpolated: false };
        // E-rank below 51 used to be guessed here at 2.6, by continuing the
        // -0.1-per-rank slope of A-D. The server actually says 2.5
        // (mobutils.cpp:261), which breaks that pattern -- so this is the real
        // value now, not an estimate, and nothing is flagged.
        case 5: return { value: Math.floor(4 + (lvl - 1) * 2.5), interpolated: false };
      }
    }
    return { value: 0, interpolated: false };
  }

  // opts: { mLvl, sLvl, famVitRank, mainVitRank, subVitRank, defRank, statMult,
  //         preFiftyZone,
  //         famAgiRank, mainAgiRank, subAgiRank, evaRank }   (AGI/EVA optional)
  //
  // AGI and EVA are derived the same way as VIT and DEF and by the same source
  // lines, but only when the AGI ranks are supplied -- a caller that only wants
  // VIT/DEF (ws-calculator) passes nothing extra and gets AGI/EVA back as null.
  function derive(opts) {
    const fVIT = getBaseToRank(opts.famVitRank, opts.mLvl);
    const mVIT = getBaseToRank(opts.mainVitRank, opts.mLvl);

    // The full pre-CoP era subjob curve isn't ported (only visible in source for
    // ranks A/B and partially C-E) -- both paths use the standard "halved"
    // subjob contribution used by level 50+ content. preFiftyZone only flags the
    // result as approximate.
    const sVIT = Math.floor(getBaseToRank(opts.subVitRank, opts.sLvl) / 2);

    const rawVIT = fVIT + mVIT + sVIT;
    const VIT = Math.floor(rawVIT * opts.statMult);

    const defResult = getBaseDefEva(opts.defRank, opts.mLvl);
    const DEF = 8 + Math.floor(VIT * 0.5) + defResult.value;

    const out = {
      VIT, DEF, fVIT, mVIT, sVIT,
      statMult: opts.statMult,
      defBase: defResult.value,
      defInterpolated: defResult.interpolated,
      subjobApproximate: !!opts.preFiftyZone,
      level: opts.mLvl,
      AGI: null, EVA: null, evaBase: null, fAGI: null, mAGI: null, sAGI: null,
    };

    if (opts.famAgiRank) {
      const fAGI = getBaseToRank(opts.famAgiRank, opts.mLvl);
      const mAGI = getBaseToRank(opts.mainAgiRank, opts.mLvl);
      const sAGI = Math.floor(getBaseToRank(opts.subAgiRank, opts.sLvl) / 2);
      const AGI = Math.floor((fAGI + mAGI + sAGI) * opts.statMult);

      // mobutils.cpp:931 seeds Mod::EVA from the base curve at the mob's evasion
      // rank; battle_entity.cpp:1661 then adds AGI/2 on top for every entity.
      // Unlike DEF there is no flat +8 term.
      const evaResult = getBaseDefEva(opts.evaRank, opts.mLvl);
      out.AGI = AGI;
      out.fAGI = fAGI; out.mAGI = mAGI; out.sAGI = sAGI;
      out.evaBase = evaResult.value;
      out.EVA = evaResult.value + Math.floor(AGI / 2);
    }

    return out;
  }

  return { getBaseToRank, getBaseDefEva, derive };
})();
