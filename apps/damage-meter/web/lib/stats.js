/*
 * stats.js -- damage events -> the numbers the UI draws.
 *
 * DOM-free, same contract as source.js: every function takes plain data and
 * returns plain data, so it can be exercised from the console.
 */
(function (global) {
  'use strict';

  var DPS = global.DPS || (global.DPS = {});

  /* Events that represent an attempted damaging action (hit or whiff). */
  function isCombat(e) {
    return e.kind !== 'defeat';
  }

  // ----------------------------------------------------------------- session

  /*
   * THE CLOCK IS THE SESSION, NOT THE WALL.
   *
   * Every number this app draws is measured from the session's zero; the x
   * axis, the DPS denominator and the per-hit table are all derived from it by
   * `at()` and `elapsed()` and by nothing else.
   *
   * Wall-clock time is still what arrives on the wire (`e.t`), because that is
   * what the addon can cheaply know. It is converted exactly once, at the
   * `filter` boundary, the same place and in the same style `credit` rewrites a
   * pet's actor. Downstream of that call there is no wall clock left.
   *
   *   { armedAt:   ms | null,        wall clock of the Start press
   *     startedAt: ms | null,        the first counted event -- the zero
   *     spans: [{from, to}],         completed pauses, in order
   *     pausedAt:  ms | null }       wall clock of the Pause press, if paused
   *
   * THE PRESS IS NOT THE ZERO. Start only ARMS the session; the clock starts on
   * the first event that would actually be counted, and that event's own
   * timestamp becomes zero. Pressing Start a beat early -- while the puller is
   * still running back, while you are still buffing -- therefore costs nothing,
   * which is the whole point: a stopwatch you have to press on the exact frame
   * of the first swing is a stopwatch that is always a second or two wrong, and
   * every DPS figure in the app is divided by that error.
   *
   * Three states, and each is real:
   *
   *   armedAt null, startedAt null   idle. Nothing is counted and nothing is
   *                                  drawn, so a page left open cannot quietly
   *                                  accumulate a pull nobody asked for.
   *   armedAt set,  startedAt null   ARMED. Still nothing counted -- there is
   *                                  no zero yet to measure from -- but the
   *                                  next counted event will set one.
   *   startedAt set                  running (or paused).
   *
   * `at()` and `elapsed()` both key on `startedAt` alone, so an armed session
   * behaves exactly like an idle one until it latches. That is what keeps the
   * arming out of every panel: one extra field, and no new branch downstream.
   */
  function arm(now) {
    // FLOORED TO THE SECOND, deliberately. The wire clock is whole seconds
    // (`os.time()`), so a swing 300 ms before the press and one 300 ms after it
    // carry the SAME `t` and cannot be told apart. Rounding down counts that
    // second rather than discarding it: catching the first swing of the pull
    // matters more than excluding a swing that beat the button by a moment, and
    // the alternative silently drops it from the accuracy figure too.
    now = now == null ? Date.now() : now;
    return {
      armedAt: Math.floor(now / 1000) * 1000,
      startedAt: null, spans: [], pausedAt: null
    };
  }

  function idle() {
    return { armedAt: null, startedAt: null, spans: [], pausedAt: null };
  }

  /* Latch the zero. Called once, with the timestamp of the first counted event;
     `startedAt` never moves again, so a later filter change cannot re-date a
     session that is already running and re-scale every number in it. */
  function start(sn, t) {
    if (sn && sn.startedAt == null) sn.startedAt = t;
    return sn;
  }

  /* Total paused time that had already ELAPSED by wall-clock `t`. A pause still
     open counts only up to `t`, which is what freezes the clock while paused. */
  function pausedBefore(sn, t) {
    var ms = 0, i, s;
    for (i = 0; i < sn.spans.length; i++) {
      s = sn.spans[i];
      if (s.from >= t) break;
      ms += Math.min(s.to, t) - s.from;
    }
    if (sn.pausedAt != null && sn.pausedAt < t) ms += t - sn.pausedAt;
    return ms;
  }

  /* Is wall-clock `t` inside a pause? Those events are dropped, not shifted. */
  function inPause(sn, t) {
    for (var i = 0; i < sn.spans.length; i++) {
      if (t >= sn.spans[i].from && t < sn.spans[i].to) return true;
    }
    return sn.pausedAt != null && t >= sn.pausedAt;
  }

  /*
   * Wall clock -> ms since Start, or null for an instant the session does not
   * cover: before it began, or inside a pause.
   *
   * THE PAUSED SPAN IS SUBTRACTED, NOT SKIPPED OVER. Two swings either side of
   * a three-minute pause come out three minutes closer together than their
   * timestamps are, so the chart is continuous and the axis agrees with the
   * denominator DPS is divided by. A pause is time the meter was not measuring,
   * and time it was not measuring is time that did not happen.
   */
  function at(sn, t) {
    if (!sn || sn.startedAt == null) return null;
    if (t < sn.startedAt) return null;
    if (inPause(sn, t)) return null;
    return t - sn.startedAt - pausedBefore(sn, t);
  }

  /* The session clock right now: how long Start has been running for. Frozen
     while paused, because `pausedBefore` grows at exactly the same rate. */
  function elapsed(sn, now) {
    if (!sn || sn.startedAt == null) return 0;
    now = now == null ? Date.now() : now;
    if (now < sn.startedAt) return 0;
    return now - sn.startedAt - pausedBefore(sn, now);
  }

  function running(sn) {
    return !!sn && sn.startedAt != null && sn.pausedAt == null;
  }

  /* Armed but not yet started: waiting for the event that will be its zero. */
  function armed(sn) {
    return !!sn && sn.armedAt != null && sn.startedAt == null;
  }

  function pause(sn, now) {
    if (running(sn)) sn.pausedAt = now == null ? Date.now() : now;
    return sn;
  }

  function resume(sn, now) {
    if (sn && sn.pausedAt != null) {
      sn.spans.push({ from: sn.pausedAt, to: now == null ? Date.now() : now });
      sn.pausedAt = null;
    }
    return sn;
  }

  // -------------------------------------------------------------- attribution

  /*
   * A PET'S DAMAGE IS ITS OWNER'S DAMAGE.
   *
   * The packet names the master on every row a pet produced (`owner`), so this
   * is attribution and not a guess. The event is re-actored rather than merely
   * summed under the owner, because everything downstream -- aggregate,
   * cumulative, distribution, the character chips -- keys on `actor`, and this
   * one rewrite is then the whole change.
   *
   * The action keeps the pet's name as a prefix, so the breakdown INSIDE the
   * owner still separates the two: a beastmaster and their jug pet both swing
   * something called 'Attack', and merging those reports one average and one
   * accuracy over two different creatures. `by` keeps who actually swung.
   *
   * A COPY, NEVER A MUTATION. The raw event list stays true to the file.
   * Idempotent as well, because `filter` runs twice over the same events --
   * once scoped, once for the chips -- and the second pass sees actor === owner
   * and does nothing.
   */
  function credit(e) {
    if (!e.owner || e.actor === e.owner) return e;
    var c = {}, k;
    for (k in e) if (Object.prototype.hasOwnProperty.call(e, k)) c[k] = e[k];
    c.actor = e.owner;
    c.actorKind = 'player';   // the owner came out of a party slot
    c.by = e.pet || e.actor;
    c.action = c.by + ': ' + e.action;
    return c;
  }

  // --------------------------------------------------------------- filtering

  /*
   * opts: { session, roster, actors: {name:bool}, skillchains: bool }
   *
   * A session with no zero yet selects NOTHING -- see `firstCounted`, which is
   * what gives an armed session its zero, and which applies the same drop rules
   * through the same `counted` predicate.
   *
   * `actors` is the UI's per-actor checkbox map; absent means "all on".
   *
   * Pet damage is credited to the owner on the way through; see `credit`.
   *
   * PASS A `session` AND THIS IS WHERE THE CLOCK CHANGES. Every event outside
   * it is dropped -- before Start, or during a pause -- and every survivor is
   * COPIED with `t` rewritten from wall clock to ms-since-Start. That makes
   * this the only place in the app that knows both clocks exist: `aggregate`,
   * `cumulative`, `distribution` and every table downstream see a timeline that
   * begins at zero and never mentions the time of day.
   *
   * A copy for the same reason `credit` copies: `app.source.events` stays true
   * to the file.
   * Without a session the events pass through with their wall clock intact,
   * which is what the second, chip-scoping `filter` call in `render` relies on
   * -- it is re-filtering an already-converted list and must not convert twice.
   *
   * Pass a `roster` and every event whose *actor* is a monster is dropped: this
   * is a party damage meter, and damage the monsters dealt is neither shown nor
   * counted anywhere. It is dropped here in the view rather than on the way in,
   * so the monsters' own events stay in the event list -- it stays true to the
   * file, and the drop is one rule in one place.
   *
   * `skillchains: false` drops skillchain damage entirely. The addon emits a
   * chain as its own event (kind 'skillchain', credited to whoever closed it),
   * so this is a re-render and never a re-read.
   */
  /*
   * EVERY DROP RULE THAT IS NOT ABOUT TIME, in one place. Returns the credited
   * event, or null if it does not count.
   *
   * `filter` and `firstCounted` both go through here so they cannot disagree
   * about what counts -- and they must not, because one decides what is drawn
   * and the other decides which event the clock starts on. Two copies of this
   * would let the meter start its clock on an event it then refuses to draw.
   */
  function counted(e, roster, actors, skillchains) {
    if (!isCombat(e)) return null;
    if (!skillchains && e.kind === 'skillchain') return null;
    // Asked of the RAW actor, so a pet is judged as the pet it is.
    if (roster && roster.isMob(e.actor)) return null;
    e = credit(e);
    // Keyed on the CREDITED name, so switching an owner off takes their pet
    // with them -- the chips are built from this same aggregate.
    if (actors && actors[e.actor] === false) return null;
    return e;
  }

  function filter(events, opts) {
    opts = opts || {};
    var roster = opts.roster;
    var sn = opts.session;
    var actors = opts.actors;
    var skillchains = opts.skillchains !== false;
    var out = [];

    // No zero yet -- idle, or armed and still waiting -- so there is no instant
    // for anything to be measured from and nothing is selected.
    if (sn && sn.startedAt == null) return out;

    for (var i = 0; i < events.length; i++) {
      var e = events[i], el = null;
      if (sn) {
        el = at(sn, e.t);
        if (el == null) continue;      // before the zero, or during a pause
      }
      e = counted(e, roster, actors, skillchains);
      if (!e) continue;
      if (el != null) e = onClock(e, el);
      out.push(e);
    }
    return out;
  }

  /*
   * The zero an armed session is waiting for: the timestamp of the first event
   * at or after the Start press that would actually be counted. Null while
   * nothing has qualified yet.
   *
   * "Would actually be counted" is the whole rule, and it is the same test
   * `filter` applies -- a monster's swing does not start the clock, nor does a
   * skillchain with chains switched off, nor an excluded character's attack.
   * The consequence worth knowing: with every character excluded, nothing
   * qualifies and the clock never starts. That is consistent (the meter would
   * draw nothing anyway) but it is the one way arming can look stuck.
   *
   * A MISS STARTS THE CLOCK, like any other swing. The alternative -- waiting
   * for damage to land -- would put every miss before it outside the session
   * and silently drop them from the accuracy figure, which is exactly the
   * number a run of whiffs at the start of a pull ought to be moving.
   */
  function firstCounted(events, sn, opts) {
    if (!sn || sn.armedAt == null || sn.startedAt != null) return null;
    opts = opts || {};
    var skillchains = opts.skillchains !== false;
    for (var i = 0; i < events.length; i++) {
      var e = events[i];
      if (e.t < sn.armedAt) continue;
      if (counted(e, opts.roster, opts.actors, skillchains)) return e.t;
    }
    return null;
  }

  /* A copy of `e` on the session clock. Never a mutation: `credit` may have
     handed back the caller's own object when there was no pet to re-actor. */
  function onClock(e, el) {
    var c = {}, k;
    for (k in e) if (Object.prototype.hasOwnProperty.call(e, k)) c[k] = e[k];
    c.wall = e.wall == null ? e.t : e.wall;   // the original wall clock, drawn nowhere
    c.t = el;
    return c;
  }

  // --------------------------------------------------------------- collapsing

  /*
   * Events -> uses. One announced action is ONE use of it however many targets
   * it reached, and the log writes a damage line per victim: uncollapsed, an
   * AoE weaponskill into three mobs counts as three hits, inflates the swing
   * count that accuracy divides by, and files the *splash* spread in the
   * histogram where the weaponskill's own spread belongs.
   *
   * The grouping is ground truth, not a guess: the action packet carries the
   * whole target list, so the addon mints one `use` per action in Lua and hands
   * it to every row that action produced. A single-target event is its own use,
   * so this is a no-op for melee and the input array is never mutated.
   *
   * Damage sums, and the flags are "any": a use that hit two targets and was
   * evaded by a third is one landed hit, not two hits and a miss.
   */
  function collapse(events) {
    var out = [], byUse = {}, i, e, u;

    for (i = 0; i < events.length; i++) {
      e = events[i];
      // Hand-built test data and anything parsed before `use` existed.
      if (e.use == null) { out.push(e); continue; }

      u = byUse[e.use];
      if (!u) {
        byUse[e.use] = u = {
          t: e.t, seq: e.seq, kind: e.kind, action: e.action, actionId: e.actionId,
          actor: e.actor, actorKind: e.actorKind,
          target: e.target, targetKind: e.targetKind,
          dmg: e.dmg, hit: e.hit, crit: e.crit, burst: e.burst,
          msg: e.msg, owner: e.owner, pet: e.pet,
          use: e.use, line: e.line, wall: e.wall,
          targets: [e.target], parts: 1
        };
        out.push(u);
        continue;
      }

      u.dmg += e.dmg;
      if (e.hit) u.hit = true;
      if (e.crit) u.crit = true;
      if (e.burst) u.burst = true;
      if (e.t < u.t) u.t = e.t;      // the use happened when it was announced
      if (u.targets.indexOf(e.target) < 0) u.targets.push(e.target);
      u.parts++;
    }

    // A use that reached several targets has no one target name to show.
    for (i = 0; i < out.length; i++) {
      u = out[i];
      if (u.targets && u.targets.length > 1) {
        u.target = u.targets.length + ' targets';
        u.targetKind = '';
      }
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
   * `opts.duration` is the SESSION CLOCK in seconds, and when it is given it is
   * the denominator under every DPS here -- the party's and each character's
   * alike. One denominator is what makes the column add up: the characters' DPS
   * figures now sum to the party's, and a character contributing nothing for a
   * minute is a character whose DPS fell, which is the question the meter is
   * being asked. It is also why the clock has to stop on Pause rather than the
   * events merely being dropped; a frozen numerator over a running denominator
   * would read as the party dying.
   *
   * Each actor's own active window (first to last action) is still measured and
   * still reported, as `window` -- it is what the tables show as a span, and
   * without it a character's first and last swing are unrecoverable from here.
   * It is no longer what DPS is divided by. Without `opts.duration` it still
   * is, which is what keeps `aggregate` usable on a bare event list.
   *
   * Counts are per *use*, not per damage line -- see `collapse`.
   */
  function aggregate(events, opts) {
    opts = opts || {};
    var fixed = opts.duration;
    var byActor = {};
    var order = [];
    var i, e, a;

    var lines = events.length;
    events = collapse(events);

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
      a.window = span;
      a.duration = fixed != null ? fixed : span;
      a.dps = a.duration > 0 ? a.total / a.duration : 0;
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
      window: tMin === Infinity ? 0 : (tMax - tMin) / 1000,
      duration: fixed != null ? fixed : (tMin === Infinity ? 0 : (tMax - tMin) / 1000),
      events: lines,          // damage lines in
      uses: events.length     // actions they collapsed to
    };
  }

  // ------------------------------------------------------------ time series

  /*
   * Cumulative damage per actor, sampled onto a shared time grid.
   *
   * A shared grid (rather than each series carrying its own points) is what
   * makes one crosshair able to read every series at the same instant.
   * `maxPoints` caps the grid so a 6-hour log still draws at interactive speed.
   *
   * Deliberately NOT collapsed: this only ever sums damage into time bins, and
   * the sum is the same either way. Collapsing would move an AoE's later
   * victims back onto the announcement's timestamp for no gain.
   *
   * `opts.from` PINS THE LEFT EDGE, and under a session clock it is 0 -- the
   * instant Start was pressed. Without it the grid begins at the first event,
   * which quietly hides the run-up: a pull whose first swing landed twenty
   * seconds in would draw an axis twenty seconds shorter than the span DPS is
   * divided by, so the chart and the number beside it would be describing
   * different windows. Those twenty seconds are part of the measurement, and a
   * flat line across them is what they looked like.
   *
   * `opts.now` is the LIVE EDGE. Without it the grid stops at the newest event,
   * so between events the chart is frozen and every arrival jumps it forward;
   * with it the grid runs to wall-clock now and each series is carried flat
   * across the silence -- which is what a cumulative total actually did during
   * that silence, not an extrapolation. The carry is one extra sample pinned to
   * exactly `now` rather than another whole bin, so the right edge can advance
   * by a frame's worth of time without the grid step having to change; callers
   * animating it move `times[times.length - 1]` and redraw (see `live`).
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
    if (opts.from != null && opts.from < t0) t0 = opts.from;
    var tEvent = t1;
    var live = opts.now != null && opts.now > t1;
    if (live) t1 = opts.now;
    if (t1 <= t0) t1 = t0 + 1000;

    var step = Math.max(1000, Math.ceil((t1 - t0) / maxPoints / 1000) * 1000);
    var nb = Math.floor((t1 - t0) / step) + 1;
    // The live sample is always allocated when there is a live edge, even when
    // the last bin happens to land on `now` -- an animating caller needs the
    // slot to exist before it has anywhere to move to.
    var n = nb + (live ? 1 : 0);

    var times = new Array(n);
    for (i = 0; i < nb; i++) times[i] = t0 + i * step;
    if (live) times[nb] = t1;

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
      var bin = Math.max(0, Math.min(nb - 1, Math.floor((e.t - t0) / step)));
      s.values[bin] += e.dmg;
    }

    // Bin totals -> running total. The live sample holds no damage of its own,
    // so it inherits the run and the tail comes out flat.
    for (i = 0; i < series.length; i++) {
      var v = series[i].values, run = 0;
      for (var j = 0; j < n; j++) { run += v[j]; v[j] = run; }
    }

    return {
      times: times, series: series, step: step, t0: t0, t1: t1,
      live: live,        // the last sample is the live edge, not an event
      tEvent: tEvent     // newest event in the model, whatever the edge says
    };
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
   *
   * Collapsed first, so an AoE contributes one figure -- what the action hit
   * for -- rather than one point per victim, which is a distribution of splash
   * and not of the action.
   */
  function distribution(events, actor, action, opts) {
    opts = opts || {};
    var picked = [];
    var misses = 0, crits = 0, bursts = 0;

    events = collapse(events);

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

  /*
   * Milliseconds since Start -> the label the whole app reads time in.
   *
   * `m:ss` under an hour and `h:mm:ss` over it, so the two are never confused
   * with each other and neither is confused with a time of day -- the leading
   * field is unpadded exactly so that `3:07` cannot be misread as an hour.
   * This replaces `fmtClock` everywhere a measurement is drawn; the clock
   * formatter stays for the status line, which reports a real time of day.
   */
  function fmtElapsed(ms) {
    var neg = ms < 0;
    var sec = Math.max(0, Math.floor(Math.abs(ms) / 1000));
    var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
    var out = h
      ? h + ':' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0')
      : m + ':' + String(s).padStart(2, '0');
    return neg ? '-' + out : out;
  }

  /*
   * The same clock as `fmtElapsed`, zero-padded to a fixed width: `MM:SS`, and
   * `H:MM:SS` once it runs past the hour.
   *
   * For the READOUT rather than a label. `fmtElapsed` drops the leading zero so
   * that a figure sitting inside a sentence or under an axis tick reads as
   * short as it is; a stopwatch is watched, and a watched figure that changes
   * width at 0:59 -> 1:00 shifts every digit beside it. Fixed width also makes
   * the two minutes columns line up between the tile and every floating bar.
   */
  function fmtStopwatch(ms) {
    var sec = Math.max(0, Math.floor(Math.abs(ms) / 1000));
    var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
    var mmss = String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');
    return h ? h + ':' + mmss : mmss;
  }

  function fmtDuration(sec) {
    sec = Math.max(0, Math.round(sec));
    var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
    if (h) return h + 'h ' + m + 'm';
    if (m) return m + 'm ' + String(s).padStart(2, '0') + 's';
    return s + 's';
  }

  DPS.stats = {
    arm: arm,
    idleSession: idle,
    sessionStart: start,
    sessionAt: at,
    sessionElapsed: elapsed,
    sessionRunning: running,
    sessionArmed: armed,
    sessionPause: pause,
    sessionResume: resume,
    firstCounted: firstCounted,
    counted: counted,
    filter: filter,
    credit: credit,
    collapse: collapse,
    aggregate: aggregate,
    cumulative: cumulative,
    distribution: distribution,
    quantile: quantile,
    fmtInt: fmtInt,
    fmtNum: fmtNum,
    fmtCompact: fmtCompact,
    fmtClock: fmtClock,
    fmtElapsed: fmtElapsed,
    fmtStopwatch: fmtStopwatch,
    fmtDuration: fmtDuration
  };
})(window);
