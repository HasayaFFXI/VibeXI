/*
 * stats.js -- damage events -> the numbers the UI draws.
 *
 * DOM-free, same contract as parser.js: every function takes plain data and
 * returns plain data, so it can be exercised from the console.
 */
(function (global) {
  'use strict';

  var DPS = global.DPS || (global.DPS = {});

  /* Events that represent an attempted damaging action (hit or whiff). */
  function isCombat(e) {
    return e.kind !== 'defeat';
  }

  // --------------------------------------------------------------- filtering

  /*
   * opts: { from, to, roster, actors: {name:bool}, skillchains: bool }
   * `actors` is the UI's per-actor checkbox map; absent means "all on".
   *
   * Pass a `roster` and every event whose *actor* is a monster is dropped: this
   * is a party damage meter, and damage the monsters dealt is neither shown nor
   * counted anywhere. It is dropped here rather than in the parser because
   * `roster.rebuild` derives who is a monster from who fights whom, so the
   * monsters' own events are what classify the article-less names -- see the
   * fixed point in parser.js. They are parsed, then filtered out of the view.
   *
   * `skillchains: false` drops skillchain damage entirely. It is a filter and
   * not a parse rule on purpose: the events are already parsed and the log
   * lines are long gone, so toggling it has to be a re-render, not a re-read.
   */
  function filter(events, opts) {
    opts = opts || {};
    var roster = opts.roster;
    var from = opts.from == null ? -Infinity : opts.from;
    var to = opts.to == null ? Infinity : opts.to;
    var actors = opts.actors;
    var skillchains = opts.skillchains !== false;
    var out = [];

    for (var i = 0; i < events.length; i++) {
      var e = events[i];
      if (!isCombat(e)) continue;
      if (!skillchains && e.kind === 'skillchain') continue;
      if (e.t < from || e.t > to) continue;
      if (roster && roster.isMob(e.actor)) continue;
      if (actors && actors[e.actor] === false) continue;
      out.push(e);
    }
    return out;
  }

  // -------------------------------------------------------------- aggregation

  function blankBucket(name) {
    return {
      name: name,
      total: 0,
      hits: 0,
      misses: 0,
      crits: 0,
      bursts: 0,
      min: Infinity,
      max: 0,
      first: Infinity,
      last: -Infinity,
      values: []
    };
  }

  function addTo(b, e) {
    if (e.hit) {
      b.hits++;
      b.total += e.dmg;
      b.values.push(e.dmg);
      if (e.dmg < b.min) b.min = e.dmg;
      if (e.dmg > b.max) b.max = e.dmg;
      if (e.crit) b.crits++;
      if (e.burst) b.bursts++;
    } else {
      b.misses++;
    }
    if (e.t < b.first) b.first = e.t;
    if (e.t > b.last) b.last = e.t;
  }

  function finish(b) {
    b.swings = b.hits + b.misses;
    b.avg = b.hits ? b.total / b.hits : 0;
    b.avgPerSwing = b.swings ? b.total / b.swings : 0;
    b.accuracy = b.swings ? b.hits / b.swings : 0;
    if (b.min === Infinity) b.min = 0;
    if (b.first === Infinity) { b.first = null; b.last = null; }
    return b;
  }

  /*
   * Per-actor totals plus a per-action breakdown inside each.
   *
   * DPS uses the actor's own active window (first to last action), not the
   * encounter wall clock -- a character who joined halfway through should not
   * be divided by time they were not there.
   */
  function aggregate(events) {
    var byActor = {};
    var order = [];
    var i, e, a;

    for (i = 0; i < events.length; i++) {
      e = events[i];
      a = byActor[e.actor];
      if (!a) {
        a = byActor[e.actor] = blankBucket(e.actor);
        a.actions = {};
        a.actionOrder = [];
        order.push(e.actor);
      }
      addTo(a, e);

      var act = a.actions[e.action];
      if (!act) {
        act = a.actions[e.action] = blankBucket(e.action);
        act.kind = e.kind;
        a.actionOrder.push(e.action);
      }
      addTo(act, e);
    }

    var actors = [];
    var grand = 0;
    var tMin = Infinity, tMax = -Infinity;

    for (i = 0; i < order.length; i++) {
      a = finish(byActor[order[i]]);
      a.actionList = a.actionOrder.map(function (k) { return finish(a.actions[k]); })
        .sort(function (x, y) { return y.total - x.total; });
      var span = (a.last - a.first) / 1000;
      a.duration = span;
      a.dps = span > 0 ? a.total / span : 0;
      grand += a.total;
      if (a.first != null && a.first < tMin) tMin = a.first;
      if (a.last != null && a.last > tMax) tMax = a.last;
      actors.push(a);
    }

    actors.sort(function (x, y) { return y.total - x.total; });
    for (i = 0; i < actors.length; i++) {
      actors[i].share = grand ? actors[i].total / grand : 0;
    }

    return {
      actors: actors,
      total: grand,
      start: tMin === Infinity ? null : tMin,
      end: tMax === -Infinity ? null : tMax,
      duration: tMin === Infinity ? 0 : (tMax - tMin) / 1000,
      events: events.length
    };
  }

  // ------------------------------------------------------------ time series

  /*
   * Cumulative damage per actor, sampled onto a shared time grid.
   *
   * A shared grid (rather than each series carrying its own points) is what
   * makes one crosshair able to read every series at the same instant.
   * `maxPoints` caps the grid so a 6-hour log still draws at interactive speed.
   */
  function cumulative(events, names, opts) {
    opts = opts || {};
    var maxPoints = opts.maxPoints || 400;

    if (!events.length || !names.length) {
      return { times: [], series: [], step: 0 };
    }

    var t0 = Infinity, t1 = -Infinity;
    for (var i = 0; i < events.length; i++) {
      if (events[i].t < t0) t0 = events[i].t;
      if (events[i].t > t1) t1 = events[i].t;
    }
    if (t1 <= t0) t1 = t0 + 1000;

    var step = Math.max(1000, Math.ceil((t1 - t0) / maxPoints / 1000) * 1000);
    var n = Math.floor((t1 - t0) / step) + 1;

    var times = new Array(n);
    for (i = 0; i < n; i++) times[i] = t0 + i * step;

    var idx = {}, series = [];
    for (i = 0; i < names.length; i++) {
      idx[names[i]] = i;
      series.push({ name: names[i], values: new Float64Array(n) });
    }

    for (i = 0; i < events.length; i++) {
      var e = events[i];
      if (!e.hit || !e.dmg) continue;
      var s = series[idx[e.actor]];
      if (!s) continue;
      var bin = Math.min(n - 1, Math.floor((e.t - t0) / step));
      s.values[bin] += e.dmg;
    }

    // Bin totals -> running total.
    for (i = 0; i < series.length; i++) {
      var v = series[i].values, run = 0;
      for (var j = 0; j < n; j++) { run += v[j]; v[j] = run; }
    }

    return { times: times, series: series, step: step, t0: t0, t1: t1 };
  }

  // ------------------------------------------------------------ distribution

  function quantile(sorted, q) {
    if (!sorted.length) return 0;
    var pos = (sorted.length - 1) * q;
    var lo = Math.floor(pos), hi = Math.ceil(pos);
    if (lo === hi) return sorted[lo];
    return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
  }

  /*
   * Every hit of one actor's one action, with the summary stats and a
   * histogram. Bin count follows Freedman-Diaconis, clamped -- fixed bin counts
   * either flatten a tight weaponskill distribution or shatter a wide one.
   */
  function distribution(events, actor, action, opts) {
    opts = opts || {};
    var picked = [];
    var misses = 0, crits = 0, bursts = 0;

    for (var i = 0; i < events.length; i++) {
      var e = events[i];
      if (e.actor !== actor) continue;
      if (action != null && e.action !== action) continue;
      if (!e.hit) { misses++; continue; }
      picked.push(e);
      if (e.crit) crits++;
      if (e.burst) bursts++;
    }

    var values = picked.map(function (e) { return e.dmg; });
    var sorted = values.slice().sort(function (a, b) { return a - b; });
    var n = sorted.length;

    var sum = 0;
    for (i = 0; i < n; i++) sum += sorted[i];
    var avg = n ? sum / n : 0;

    var varc = 0;
    for (i = 0; i < n; i++) varc += (sorted[i] - avg) * (sorted[i] - avg);
    var stdev = n > 1 ? Math.sqrt(varc / (n - 1)) : 0;

    var min = n ? sorted[0] : 0;
    var max = n ? sorted[n - 1] : 0;
    var q1 = quantile(sorted, 0.25);
    var q3 = quantile(sorted, 0.75);

    var bins = [];
    if (n > 0 && max > min) {
      var iqr = q3 - q1;
      var width = iqr > 0 ? 2 * iqr / Math.pow(n, 1 / 3) : 0;
      var count = width > 0 ? Math.ceil((max - min) / width) : Math.ceil(Math.sqrt(n));
      count = Math.max(6, Math.min(opts.maxBins || 28, count || 6));

      var bw = (max - min) / count;
      for (i = 0; i < count; i++) {
        bins.push({ lo: min + i * bw, hi: min + (i + 1) * bw, count: 0 });
      }
      for (i = 0; i < n; i++) {
        var b = Math.min(count - 1, Math.floor((sorted[i] - min) / bw));
        bins[b].count++;
      }
    } else if (n > 0) {
      bins.push({ lo: min, hi: min, count: n });   // every hit identical
    }

    return {
      actor: actor,
      action: action,
      events: picked,
      values: values,
      sorted: sorted,
      bins: bins,
      count: n,
      misses: misses,
      swings: n + misses,
      accuracy: (n + misses) ? n / (n + misses) : 0,
      crits: crits,
      critRate: n ? crits / n : 0,
      bursts: bursts,
      total: sum,
      min: min,
      max: max,
      avg: avg,
      median: quantile(sorted, 0.5),
      q1: q1,
      q3: q3,
      p90: quantile(sorted, 0.9),
      stdev: stdev
    };
  }

  // -------------------------------------------------------------- formatting

  function fmtInt(n) {
    return Math.round(n).toLocaleString('en-US');
  }

  function fmtNum(n, dp) {
    return Number(n).toLocaleString('en-US', {
      minimumFractionDigits: dp == null ? 1 : dp,
      maximumFractionDigits: dp == null ? 1 : dp
    });
  }

  function fmtCompact(n) {
    var a = Math.abs(n);
    if (a >= 1e6) return fmtNum(n / 1e6, 1) + 'M';
    if (a >= 1e4) return fmtNum(n / 1e3, 1) + 'K';
    return fmtInt(n);
  }

  function fmtClock(ms) {
    var d = new Date(ms);
    return String(d.getHours()).padStart(2, '0') + ':' +
           String(d.getMinutes()).padStart(2, '0') + ':' +
           String(d.getSeconds()).padStart(2, '0');
  }

  function fmtDuration(sec) {
    sec = Math.max(0, Math.round(sec));
    var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
    if (h) return h + 'h ' + m + 'm';
    if (m) return m + 'm ' + String(s).padStart(2, '0') + 's';
    return s + 's';
  }

  DPS.stats = {
    filter: filter,
    aggregate: aggregate,
    cumulative: cumulative,
    distribution: distribution,
    quantile: quantile,
    fmtInt: fmtInt,
    fmtNum: fmtNum,
    fmtCompact: fmtCompact,
    fmtClock: fmtClock,
    fmtDuration: fmtDuration
  };
})(window);
