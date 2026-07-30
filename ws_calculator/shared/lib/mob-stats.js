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
        // E-rank, level <= 50: not visible in the traced source. Interpolated by
        // continuing the base/-0.1-slope pattern from A-D as a rough estimate.
        case 5: return { value: Math.floor(4 + (lvl - 1) * 2.6), interpolated: true };
      }
    }
    return { value: 0, interpolated: false };
  }

  // opts: { mLvl, sLvl, famVitRank, mainVitRank, subVitRank, defRank, statMult,
  //         preFiftyZone }
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

    return {
      VIT, DEF, fVIT, mVIT, sVIT,
      statMult: opts.statMult,
      defBase: defResult.value,
      defInterpolated: defResult.interpolated,
      subjobApproximate: !!opts.preFiftyZone,
      level: opts.mLvl,
    };
  }

  return { getBaseToRank, getBaseDefEva, derive };
})();
