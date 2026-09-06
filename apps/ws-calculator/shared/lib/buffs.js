// Food / buff model. Pure -- callers pass base values in, no DOM access.
//
// Two things live here:
//   1. The "base + bonus, optionally capped" stat model (food, and anything else
//      shaped like a %-with-a-flat-cap buff).
//   2. The job-ability uptime model: ability durations/recasts from the server
//      tables, the potency formulas from job_utils/*.lua, and the rotation math
//      that turns three uptime percentages into an exact distribution over buff
//      states for the simulator to draw from.
FFXI.buffs = (function () {
  const { clamp } = FFXI.core;

  // Flat stat bonus, e.g. "STR +8".
  function applyFlat(base, flat) {
    return base + (flat || 0);
  }

  // Percentage bonus with an optional flat ceiling, e.g. "Attack +25%, up to 60".
  // A cap of 0 or less means uncapped. Kept for callers that want just this piece;
  // the ATT path itself goes through effectiveAtt, which composes two of them the
  // way the server does.
  function applyPercentCapped(base, pct, cap) {
    const rawBonus = base * (pct || 0) / 100;
    const bonus = (cap > 0) ? Math.min(rawBonus, cap) : rawBonus;
    return base + Math.max(0, bonus);
  }

  // Effective ATT, following CBattleEntity::ATT() in battle_entity.cpp:
  //
  //   max(1, ATT + (ATT * ATTP / 100) + min(ATT * FOOD_ATTP / 100, FOOD_ATT_CAP))
  //
  // Both percentages are taken off the same pre-percentage base and *added* --
  // they do not compound -- and only the food term is capped. ATTP is one shared
  // modifier, so Berserk and Warcry sum into it before this is called. Both terms
  // are integer division in the C++, hence the floors.
  function effectiveAtt(base, abilityPct, foodPct, foodCap) {
    const abilityBonus = Math.floor(base * (abilityPct || 0) / 100);
    const rawFood = Math.floor(base * (foodPct || 0) / 100);
    const foodBonus = (foodCap > 0) ? Math.min(rawFood, foodCap) : rawFood;
    return Math.max(1, base + abilityBonus + foodBonus);
  }

  // opts: { str, att, foodStr, foodAttPct, foodAttCap, buffStr, buffAttPct }
  // buffStr / buffAttPct are the job-ability contributions for one buff state and
  // default to 0, so a food-only caller can ignore them entirely.
  // Returns both the effective values and the deltas, so a summary line can be
  // rendered without recomputing.
  function effective(opts) {
    const foodStr = applyFlat(opts.str, opts.foodStr);
    // Hasso's TWOHAND_STR is a flat STR modifier like food's, so it just adds.
    const str = foodStr + (opts.buffStr || 0);
    const att = effectiveAtt(opts.att, opts.buffAttPct, opts.foodAttPct, opts.foodAttCap);
    const attFoodOnly = effectiveAtt(opts.att, 0, opts.foodAttPct, opts.foodAttCap);
    return {
      str, att,
      baseStr: opts.str,
      baseAtt: opts.att,
      strBonus: str - opts.str,
      attBonus: att - opts.att,
      // Split out so the food summary and the buff summary can each report their
      // own contribution instead of one merged number.
      foodStrBonus: foodStr - opts.str,
      foodAttBonus: attFoodOnly - opts.att,
      buffStrBonus: str - foodStr,
      buffAttBonus: att - attFoodOnly,
    };
  }

  // --- job abilities -------------------------------------------------------
  //
  // duration: the `duration` passed to addStatusEffect in scripts/globals/job_utils/
  // recast:   the `recastTime` column in sql/abilities.sql
  // job/reqLevel: the ability's row in that same table
  const ABILITIES = {
    hasso:   { name: 'Hasso',   job: 'SAM', reqLevel: 25, duration: 300, recast: 60,  potency: 'STR',  potencyHint: '+STR, +10 ACC' },
    berserk: { name: 'Berserk', job: 'WAR', reqLevel: 15, duration: 180, recast: 300, potency: 'ATT%', potencyHint: 'ATT%, and -25% DEF on you' },
    warcry:  { name: 'Warcry',  job: 'WAR', reqLevel: 35, duration: 30,  recast: 300, potency: 'ATT%', potencyHint: 'ATT%, party-wide' },
  };

  const ABILITY_IDS = ['hasso', 'berserk', 'warcry'];

  function fmtTime(sec) {
    return Math.floor(sec / 60) + ':' + String(sec % 60).padStart(2, '0');
  }

  // Uptime you get by recasting the instant the timer is up. Capped at 100 --
  // Hasso's 5:00 duration against a 1:00 recast means it never has to drop.
  function defaultUptime(id) {
    const a = ABILITIES[id];
    return Math.min(100, a.duration / a.recast * 100);
  }

  // utils.getActiveJobLevel: main job level if it matches, else sub job level if
  // that matches, else 0.
  function activeJobLevel(setup, job) {
    if (setup.mainJob === job) return setup.mainLvl;
    if (setup.subJob === job) return setup.subLvl;
    return 0;
  }

  function isAvailable(id, setup) {
    const a = ABILITIES[id];
    return activeJobLevel(setup, a.job) >= a.reqLevel;
  }

  // Effect power at a given job setup. Every one of these is handed to
  // addStatusEffect, which casts power through uint16 (lua_base_entity.cpp), so
  // fractional powers truncate -- that is why Warcry at WAR37 is 5% and not 5.47%.
  //
  // setup: { mainJob, mainLvl, subJob, subLvl }
  function derivePotency(setup) {
    const samLvl = activeJobLevel(setup, 'SAM');
    const warLvl = activeJobLevel(setup, 'WAR');

    // samurai.lua useHasso: strboost = lvl / 7 (+ HASSO_EFFECT job points).
    // Two-handed weapons only -- CBattleEntity::STR() ignores TWOHAND_STR otherwise.
    const hassoStr = isAvailable('hasso', setup) ? Math.floor(samLvl / 7) : 0;

    // warrior.lua useBerserk: 25 + BERSERK_POTENCY + clamp(floor((warLvl-40)/10)*2, 0, 10).
    // The level scale reads getMainJob(), so it only pays out on WAR *main*; as a
    // subjob Berserk stays at the flat 25%.
    const warMainLvl = setup.mainJob === 'WAR' ? setup.mainLvl : 0;
    const berserkPct = isAvailable('berserk', setup)
      ? 25 + clamp(Math.floor((warMainLvl - 40) / 10) * 2, 0, 10)
      : 0;

    // warrior.lua useWarcry: (floor(warLvl / 4 + 4.75) / 256) * 100, then truncated.
    const warcryPct = isAvailable('warcry', setup)
      ? Math.floor(Math.floor(warLvl / 4 + 4.75) / 256 * 100)
      : 0;

    return {
      hasso:   { str: hassoStr, acc: hassoStr > 0 ? 10 : 0 },
      berserk: { attPct: berserkPct },
      warcry:  { attPct: warcryPct },
      jobLevels: { SAM: samLvl, WAR: warLvl },
    };
  }

  // --- correlated rotation -------------------------------------------------

  function gcd(a, b) { while (b) { const t = a % b; a = b; b = t; } return a; }
  function lcm(a, b) { return a / gcd(a, b) * b; }

  // Turns per-buff uptimes into the exact distribution over distinct buff states.
  //
  // The model is a rotation, not three independent coin flips: each buff is
  // pressed the instant its recast is up, so inside its own recast-long cycle it
  // covers the leading `uptime%` of that cycle. A weaponskill lands at a uniformly
  // random phase of the shared cycle (the LCM of the recasts). Buffs that share a
  // recast therefore correlate -- Berserk and Warcry are both on a 300s timer, so
  // at their natural uptimes every Warcry window sits wholly inside a Berserk
  // window, exactly as it does when you press both off cooldown together. A buff
  // on a different timer (Hasso's 60s) stays effectively independent of them.
  //
  // Because the state is piecewise-constant in the phase, the distribution can be
  // enumerated exactly rather than sampled, so it contributes no noise of its own
  // to the mean and the states can be listed in the UI. Each buff's marginal
  // probability comes out exactly equal to its uptime.
  //
  // entries: [{ id, recast, uptime, on }]
  // returns: [{ prob, active: [id] }], most probable first
  function rotationStates(entries) {
    const live = (entries || []).filter(e => e.on && e.uptime > 0);
    if (!live.length) return [{ prob: 1, active: [] }];

    // A buff at 100% uptime never drops, so it needs no cycle of its own.
    const always = live.filter(e => e.uptime >= 100).map(e => e.id);
    const cycling = live.filter(e => e.uptime < 100);
    if (!cycling.length) return [{ prob: 1, active: always }];

    const cycle = cycling.reduce((t, e) => lcm(t, e.recast), 1);
    const activeFor = e => e.recast * e.uptime / 100;

    // Breakpoints: every re-press and every drop inside one shared cycle.
    const cuts = new Set([0, cycle]);
    cycling.forEach(e => {
      for (let t = 0; t < cycle; t += e.recast) {
        cuts.add(t);
        cuts.add(t + activeFor(e));
      }
    });
    const edges = Array.from(cuts).sort((a, b) => a - b);

    // Each segment holds one constant state; weight it by its share of the cycle
    // and merge segments that resolve to the same state.
    const byKey = new Map();
    for (let i = 0; i < edges.length - 1; i++) {
      const lo = edges[i], hi = edges[i + 1];
      const width = hi - lo;
      if (width <= 1e-9) continue;
      // Sampled at the midpoint, which keeps the comparison clear of the segment
      // boundaries and any float dust in them.
      const mid = (lo + hi) / 2;
      const active = always.concat(
        cycling.filter(e => (mid % e.recast) < activeFor(e)).map(e => e.id)
      );
      const key = active.join('+') || '-';
      const seen = byKey.get(key);
      if (seen) seen.prob += width / cycle;
      else byKey.set(key, { prob: width / cycle, active });
    }

    return Array.from(byKey.values()).sort((a, b) => b.prob - a.prob);
  }

  // Full resolution step: rotation states in, states carrying already-computed
  // effective stats out. This is what gets handed to FFXI.damage.simulate as
  // `p.buffStates`, which keeps the engine from needing to know anything about
  // abilities, potency or percentages.
  //
  // cfg: {
  //   uptimes:  { hasso, berserk, warcry }   percentages
  //   potency:  output shape of derivePotency (possibly with UI overrides applied)
  //   enabled:  { hasso, berserk, warcry }   booleans (job requirement met)
  //   base:     { str, att, hitRate, foodStr, foodAttPct, foodAttCap }
  // }
  function resolveStates(cfg) {
    const pot = cfg.potency;
    const entries = ABILITY_IDS.map(id => ({
      id,
      recast: ABILITIES[id].recast,
      uptime: clamp(cfg.uptimes[id] || 0, 0, 100),
      on: !!cfg.enabled[id],
    }));

    return rotationStates(entries).map(s => {
      const up = id => s.active.indexOf(id) !== -1;
      const buffStr = up('hasso') ? pot.hasso.str : 0;
      const buffAcc = up('hasso') ? pot.hasso.acc : 0;
      // ATTP is a single modifier on the server, so the two stack additively.
      const buffAttPct = (up('berserk') ? pot.berserk.attPct : 0) +
                         (up('warcry') ? pot.warcry.attPct : 0);

      const e = effective({
        str: cfg.base.str,
        att: cfg.base.att,
        foodStr: cfg.base.foodStr,
        foodAttPct: cfg.base.foodAttPct,
        foodAttCap: cfg.base.foodAttCap,
        buffStr, buffAttPct,
      });

      return {
        prob: s.prob,
        active: s.active,
        label: s.active.length ? s.active.map(id => ABILITIES[id].name).join(' + ') : 'no buffs',
        str: e.str,
        att: e.att,
        // physical_hit_rate.lua: hitdiff = (acc - eva) / 2, so Hasso's +10 ACC is
        // worth +5 points of hit rate. Clamping to the weapon cap happens in the
        // engine, alongside the existing hit-1 accuracy bonus.
        hitRate: cfg.base.hitRate + buffAcc / 2,
        buffStr, buffAttPct, buffAcc,
      };
    });
  }

  return {
    applyFlat, applyPercentCapped, effectiveAtt, effective,
    ABILITIES, ABILITY_IDS, fmtTime, defaultUptime,
    activeJobLevel, isAvailable, derivePotency,
    rotationStates, resolveStates,
  };
})();
