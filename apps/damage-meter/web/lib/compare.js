/*
 * compare.js -- two exported parses -> an A/B comparison.
 *
 * DOM-free, same contract as source.js and stats.js: plain data in, plain data
 * out, callable from the console.
 *
 *   DPS.compare.measure(run, opts)   one run -> everything the view draws of it
 *   DPS.compare.diff(ma, mb)         two measurements -> paired rows
 *   DPS.compare.pace(ma, mb, opts)   both runs' cumulative party damage on one grid
 *
 * A RUN IS AN IMPORT. `run` is what `DPS.source.importParse` returns -- an
 * ordinary reader plus a paused session -- so a compared parse goes through
 * exactly the same `filter` and `aggregate` the meter draws with, and its
 * numbers are the numbers the meter would print for it. Nothing here counts
 * damage a second way; it only lines two answers up side by side.
 *
 * A IS THE BASELINE. Every delta is B minus A, and every percentage is that
 * delta over A. "B vs A" is the one reading the view offers, so a sign means
 * the same thing in every cell.
 */
(function (global) {
  'use strict';

  var DPS = global.DPS || (global.DPS = {});
  var S = DPS.stats;

  /* Damage by type, in the order the view lists them. A pet's rows are one type
     whatever their kind, which is how the character table's Pet column counts. */
  var KINDS = [
    { key: 'melee',      label: 'Melee' },
    { key: 'ws',         label: 'Weaponskills' },
    { key: 'skillchain', label: 'Skillchains' },
    { key: 'magic',      label: 'Magic' },
    { key: 'pet',        label: 'Pet' },
    { key: 'ability',    label: 'Job abilities' },
    { key: 'ranged',     label: 'Ranged' },
    { key: 'addl',       label: 'Additional effects' },
    { key: 'reaction',   label: 'Counters & spikes' },
    { key: 'mobtp',      label: 'Other' }
  ];

  var UNKNOWN_JOB = 'Unknown job';

  /* A character's main job in this run, or UNKNOWN_JOB. Never borrowed from the
     other run: a character absent from the party table in one run is often on a
     different job in it (Tav_Run2's "Pestii" is a DRG, not Run1's PUP). */
  function mainJob(roster, name) {
    var j = roster.jobOf(name);
    return j && j.main && j.main !== 'NON' ? j.main : UNKNOWN_JOB;
  }

  /*
   * One run, measured.
   *
   *   opts.skillchains   false drops skillchain rows, as the meter's toggle does
   *   opts.by            'actor' (default) or 'job' -- what a row is
   *
   * By job, every event is re-actored onto its character's main job before the
   * aggregate, so accuracy, WS average and the action breakdown are all counted
   * over the job's combined swings rather than averaged from per-character
   * figures. Copies, never mutations -- `filter` already handed back copies.
   */
  function measure(run, opts) {
    opts = opts || {};
    var roster = run.source.roster;
    var sn = run.session;
    var byJob = opts.by === 'job';

    var scoped = S.filter(run.source.events, {
      session: sn, roster: roster, skillchains: opts.skillchains !== false
    });

    // The export's own clock, frozen at its Pause: an import is always paused,
    // so this is the fixed denominator the exporter's meter was showing.
    var secs = S.sessionElapsed(sn, sn.pausedAt) / 1000;

    // Who stands behind each row: the characters, and the job(s) they were on.
    var members = {};
    function note(key, name) {
      var m = members[key] || (members[key] = { names: [], jobs: [] });
      if (m.names.indexOf(name) < 0) m.names.push(name);
      var lab = roster.jobLabel(name);
      if (lab && m.jobs.indexOf(lab) < 0) m.jobs.push(lab);
    }

    var rows = byJob ? scoped.map(function (e) {
      var key = mainJob(roster, e.actor);
      note(key, e.actor);
      var c = {}, k;
      for (k in e) if (Object.prototype.hasOwnProperty.call(e, k)) c[k] = e[k];
      c.actor = key;
      return c;
    }) : scoped;
    if (!byJob) scoped.forEach(function (e) { note(e.actor, e.actor); });

    var agg = S.aggregate(rows, { duration: secs });

    // Healing, over the same session window, re-keyed by job the same way.
    // `heals` is absent on a reader from before the addon recorded healing.
    var hl = S.filterHeals(run.source.heals || [], { session: sn, roster: roster });
    if (byJob) {
      hl = hl.map(function (h) {
        var key = mainJob(roster, h.actor);
        note(key, h.actor);
        var c = {}, k;
        for (k in h) if (Object.prototype.hasOwnProperty.call(h, k)) c[k] = h[k];
        c.actor = key;
        return c;
      });
    }
    var heal = S.healing(hl);

    // For Healing mode: the cumulative line (Healing, so no pet heals -- the
    // same rule as the Healing column), and party healing by spell and by the
    // character who received it (pet heals included; these say where healing
    // went, not whose column it counts in).
    var healEvents = [], healSpells = {}, healTargets = {};
    for (var q = 0; q < hl.length; q++) {
      var hh = hl[q];
      if (!hh.owner) healEvents.push({ t: hh.t, hit: true, dmg: hh.hp });
      var sp = hh.by ? hh.action.slice(hh.by.length + 2) : hh.action;
      healSpells[sp] = (healSpells[sp] || 0) + hh.hp;
      healTargets[hh.target] = (healTargets[hh.target] || 0) + hh.hp;
    }

    // Damage by type and by target, over every landed row. Uncollapsed is right
    // for both: they only sum, and an AoE's damage belongs to each victim.
    var kinds = {}, targets = {};
    for (var i = 0; i < scoped.length; i++) {
      var e = scoped[i];
      if (!e.hit || !e.dmg) continue;
      var kk = e.owner ? 'pet' : e.kind;
      kinds[kk] = (kinds[kk] || 0) + e.dmg;
      targets[e.target] = (targets[e.target] || 0) + e.dmg;
    }

    // Party figures, summed from the per-row splits so they agree with the
    // character table by construction.
    var party = { autoTries: 0, autoHits: 0, wsTotal: 0, wsTries: 0, wsHits: 0, scTotal: 0, scRows: 0, petTotal: 0 };
    agg.actors.forEach(function (a) {
      var s = a.split;
      party.autoTries += s.autoTries; party.autoHits += s.autoHits;
      party.wsTotal += s.wsTotal; party.wsTries += s.wsTries; party.wsHits += s.wsHits;
      party.scTotal += s.scTotal; party.scRows += s.scRows;
      party.petTotal += s.petTotal;
    });

    var best = null;
    agg.actors.forEach(function (a) {
      a.actionList.forEach(function (act) {
        if (!best || act.max > best.max) best = { max: act.max, who: a.name, what: act.name };
      });
    });

    return {
      run: run,
      by: byJob ? 'job' : 'actor',
      events: scoped,
      agg: agg,
      members: members,
      duration: secs,
      total: agg.total,
      dps: secs > 0 ? agg.total / secs : 0,
      accuracy: party.autoTries ? party.autoHits / party.autoTries : null,
      wsTotal: party.wsTries ? party.wsTotal : null,
      wsCount: party.wsTries,
      wsAvg: party.wsHits ? party.wsTotal / party.wsHits : null,
      wsAcc: party.wsTries ? party.wsHits / party.wsTries : null,
      scTotal: party.scRows ? party.scTotal : null,
      scCount: party.scRows,
      characters: agg.actors.filter(function (a) { return a.total > 0; }).length,
      best: best,
      kinds: kinds,
      targets: targets,
      heal: heal,
      healEvents: healEvents,
      healSpells: healSpells,
      healTargets: healTargets,
      // Whether this parse could have healing at all: an export from before
      // addon 0.3.0 has no heal lines, which is not the same as healing 0.
      hasHeals: !!(run.source.heals && run.source.heals.length)
    };
  }

  /* The union of two key sets, each paired with what either side had for it. */
  function pairUp(mapA, mapB) {
    var keys = Object.keys(mapA);
    Object.keys(mapB).forEach(function (k) { if (!(k in mapA)) keys.push(k); });
    return keys.map(function (k) {
      return { key: k, a: k in mapA ? mapA[k] : null, b: k in mapB ? mapB[k] : null };
    });
  }

  function byName(list) {
    var m = {};
    list.forEach(function (x) { m[x.name] = x; });
    return m;
  }

  function size(x) { return x ? x.total : 0; }

  /*
   * Two measurements -> rows the view can print.
   *
   *   rows      one per character (or job): { key, a, b } of aggregate actors,
   *             either side null when the key was not in that run. Nobody at
   *             zero on both sides, as on the meter's character card. Largest
   *             first, by the bigger of the two.
   *   kinds     damage by type, every type either run dealt any of
   *   targets   damage by target name, largest first
   */
  function diff(ma, mb) {
    var rows = pairUp(byName(ma.agg.actors), byName(mb.agg.actors))
      .filter(function (r) { return size(r.a) > 0 || size(r.b) > 0; })
      .sort(function (x, y) {
        return Math.max(size(y.a), size(y.b)) - Math.max(size(x.a), size(x.b));
      });

    var kinds = [];
    var seen = {};
    KINDS.forEach(function (k) {
      seen[k.key] = true;
      var a = ma.kinds[k.key] || 0, b = mb.kinds[k.key] || 0;
      if (a || b) kinds.push({ key: k.key, label: k.label, a: a, b: b });
    });
    // A kind this file does not know yet still gets a row rather than vanishing.
    pairUp(ma.kinds, mb.kinds).forEach(function (r) {
      if (!seen[r.key]) kinds.push({ key: r.key, label: r.key, a: r.a || 0, b: r.b || 0 });
    });

    var targets = pairUp(ma.targets, mb.targets).map(function (r) {
      return { key: r.key, a: r.a || 0, b: r.b || 0 };
    }).sort(function (x, y) { return Math.max(y.a, y.b) - Math.max(x.a, x.b); });

    var heals = pairUp(byName(ma.heal.actors), byName(mb.heal.actors))
      .filter(function (r) { return healSize(r.a) > 0 || healSize(r.b) > 0; })
      .sort(function (x, y) {
        return Math.max(healSize(y.a), healSize(y.b)) - Math.max(healSize(x.a), healSize(x.b));
      });

    function totals(ma2, mb2) {
      return pairUp(ma2, mb2).map(function (r) {
        return { key: r.key, a: r.a || 0, b: r.b || 0 };
      }).sort(function (x, y) { return Math.max(y.a, y.b) - Math.max(x.a, x.b); });
    }

    return { rows: rows, kinds: kinds, targets: targets, heals: heals,
             healSpells: totals(ma.healSpells, mb.healSpells),
             healTargets: totals(ma.healTargets, mb.healTargets) };
  }

  /* Everything a healer put out, pet included -- for ordering and the zero test. */
  function healSize(x) { return x ? x.total + x.petTotal : 0; }

  /* One healer's heals, paired by name, largest first. */
  function healActions(row) {
    var la = row.a ? byName(row.a.actionList) : {};
    var lb = row.b ? byName(row.b.actionList) : {};
    return pairUp(la, lb).sort(function (x, y) {
      return Math.max(size(y.a), size(y.b)) - Math.max(size(x.a), size(x.b));
    });
  }

  /*
   * One row's actions, paired: { key, a, b } of action buckets, largest first.
   *
   * Only actions that dealt damage in at least one run. An Erase or a resisted
   * Stun reaches the aggregate by being used, not by doing damage, and a row of
   * dashes and zeroes for it is noise in a damage comparison. An action that did
   * damage in one run and none in the other stays -- that zero is the news.
   */
  function actions(row) {
    var la = row.a ? byName(row.a.actionList) : {};
    var lb = row.b ? byName(row.b.actionList) : {};
    return pairUp(la, lb).filter(function (x) {
      return size(x.a) > 0 || size(x.b) > 0;
    }).sort(function (x, y) {
      return Math.max(size(y.a), size(y.b)) - Math.max(size(x.a), size(x.b));
    });
  }

  /*
   * Both runs' cumulative party damage on ONE time grid, so a crosshair reads
   * the two at the same elapsed instant -- the whole point of putting them on
   * one chart is "where was each run at minute forty".
   *
   * The grid runs to the LONGER run's clock. The shorter run is NaN past its own
   * end rather than carried flat: a flat line would claim it was still being
   * measured, and chart.js lifts the pen on NaN.
   */
  function pace(ma, mb, opts) {
    opts = opts || {};
    // Which list to draw: the damage events, or (Healing mode) `healEvents`.
    var field = opts.field || 'events';
    var maxPoints = opts.maxPoints || 400;
    var end = Math.max(ma.duration, mb.duration) * 1000;
    if (end <= 0) end = 1000;
    var step = Math.max(1000, Math.ceil(end / maxPoints / 1000) * 1000);
    var n = Math.ceil(end / step) + 1;
    var times = new Array(n);
    for (var i = 0; i < n; i++) times[i] = Math.min(i * step, end);

    function line(m) {
      var v = new Float64Array(n);
      var stop = m.duration * 1000;
      var list = m[field];
      for (var k = 0; k < list.length; k++) {
        var e = list[k];
        if (!e.hit || !e.dmg) continue;
        // Into the first sample at or after the hit, so a sample reads the
        // damage done BY that instant, never a hit still to come.
        var bin = Math.min(n - 1, Math.max(0, Math.ceil(e.t / step)));
        v[bin] += e.dmg;
      }
      var run = 0;
      for (var j = 0; j < n; j++) {
        run += v[j];
        v[j] = times[j] <= stop + step - 1 ? run : NaN;
      }
      return v;
    }

    return { times: times, step: step, a: line(ma), b: line(mb) };
  }

  /*
   * B against A, as plain numbers: `d` the difference, `pct` that over A (null
   * when A is zero or either side has nothing to measure).
   */
  function delta(a, b) {
    if (a == null || b == null) return { d: null, pct: null };
    return { d: b - a, pct: a ? (b - a) / Math.abs(a) : null };
  }

  DPS.compare = {
    KINDS: KINDS,
    UNKNOWN_JOB: UNKNOWN_JOB,
    measure: measure,
    diff: diff,
    actions: actions,
    healActions: healActions,
    pace: pace,
    delta: delta
  };
})(window);
