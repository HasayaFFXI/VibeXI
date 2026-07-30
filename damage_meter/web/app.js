/*
 * app.js -- polling, state and rendering.
 *
 * The server hands over raw log lines and nothing else; parsing (lib/parser.js)
 * and aggregation (lib/stats.js) both run here, so a parse-rule change is a
 * browser refresh rather than a server restart.
 */
(function () {
  'use strict';

  var P = DPS.parser, S = DPS.stats, C = DPS.chart;
  // Not getElementById: a popped-out card's nodes live in another document, and
  // getElementById only searches its own. lib/popout.js keeps the index.
  var $ = DPS.popout.byId;
  var esc = C.esc;

  var POLL_MS = 1000;
  var FIGHT_GAP_MS = 90000;   // silence longer than this starts a new fight

  var app = {
    file: null,
    offset: 0,
    parser: P.create(null, null),
    lines: 0,
    range: 'all',
    side: 'ally',
    actorsOff: {},          // name -> true when excluded; persisted
    resetAt: null,          // wall-clock of the last meter reset, for the status line
    paused: false,
    drill: null,            // { actor, action }
    slots: {},              // name -> colour slot 0-7, or -1 for "past eight"
    nextSlot: 0,
    seen: [],               // distinct actor names, first-appearance order
    seenSet: {},
    scanned: 0,             // how far into the event list `seen` is built
    visibleActors: [],      // names with a chip right now; scopes All / None
    lastOk: 0,
    error: null
  };

  // ------------------------------------------------------------------ colors

  /*
   * Colour follows the entity, never its rank or its row: a slot is assigned
   * once and never reassigned, so a character keeps its hue when a filter or a
   * lead change reorders the table.
   *
   * One shared pool, not one per side -- separate pools would hand a monster
   * the same hue as a party member in "Both" mode. Allies get first refusal on
   * the eight slots so the party always holds the leading colours and monsters
   * take what is left. Past eight no ninth hue is invented: the tail renders in
   * muted ink and folds into a single "Other" line on the chart.
   */
  function scanActors() {
    var ev = app.parser.events;
    for (var i = app.scanned; i < ev.length; i++) {
      var n = ev[i].actor;
      if (n && !app.seenSet[n]) { app.seenSet[n] = true; app.seen.push(n); }
    }
    app.scanned = ev.length;
  }

  function assignSlots() {
    scanActors();
    var roster = app.parser.roster;
    for (var pass = 0; pass < 2; pass++) {         // 0 = allies, 1 = monsters
      for (var i = 0; i < app.seen.length; i++) {
        var n = app.seen[i];
        if (n in app.slots) continue;
        if ((pass === 1) !== roster.isMob(n)) continue;
        app.slots[n] = app.nextSlot < 8 ? app.nextSlot++ : -1;
      }
    }
  }

  function slotOf(name) {
    if (!(name in app.slots)) {
      app.slots[name] = app.nextSlot < 8 ? app.nextSlot++ : -1;
    }
    return app.slots[name];
  }

  /* Reclassification changes who counts as a party member, so slots restart. */
  function resetColors() {
    app.slots = {};
    app.nextSlot = 0;
    assignSlots();
  }

  // ------------------------------------------------------------- persistence

  var EXCLUDE_KEY = 'ffxi_dps_excluded';

  function loadExcluded() {
    try {
      var raw = localStorage.getItem(EXCLUDE_KEY);
      if (raw) app.actorsOff = JSON.parse(raw) || {};
    } catch (e) { app.actorsOff = {}; }
  }

  function saveExcluded() {
    try { localStorage.setItem(EXCLUDE_KEY, JSON.stringify(app.actorsOff)); } catch (e) { }
  }

  // ------------------------------------------------------------------- reset

  /*
   * Drops every event collected so far and starts counting from the current
   * point in the log. The read offset is deliberately left alone -- this is the
   * "clear the meter between pulls" button, not a re-read; reloading the page
   * is what replays the whole file from the top.
   *
   * Deliberately kept across a reset:
   *   - colour slots, so a character does not change hue mid-session
   *   - the roster (including its sticky article signal and any manual
   *     override), so monsters stay classified as monsters
   *   - every filter, which is user intent rather than collected data
   */
  function resetMeter() {
    app.parser.reset();
    app.scanned = 0;          // `seen` is kept; only the scan cursor rewinds
    app.drill = null;
    app.resetAt = Date.now();
    $('drillCard').hidden = true;
    app.rendered = true;
    render();
    setStatus(app.file || 'waiting for a log file', app.paused ? 'stale' : 'live');
  }

  function colorOf(name) {
    var s = slotOf(name);
    return s < 0 ? cssVar('--text-muted') : cssVar('--series-' + (s + 1));
  }

  function cssVar(n) {
    return getComputedStyle(document.documentElement).getPropertyValue(n).trim();
  }

  // ------------------------------------------------------------------- fetch

  function poll() {
    if (app.paused) { schedule(); return; }

    var qs = '?offset=' + app.offset + (app.file ? '&file=' + encodeURIComponent(app.file) : '');
    fetch('/api/log' + qs, { cache: 'no-store' })
      .then(function (r) { return r.json(); })
      .then(function (d) {
        app.error = null;
        app.lastOk = Date.now();

        if (!d.file) { setStatus('waiting for a log file', 'stale'); schedule(); return; }

        if (d.reset || d.file !== app.file) {
          var meta = P.parseFilename(d.file);
          app.file = d.file;
          app.parser = P.create(meta.date, meta.owner);
          app.lines = 0;
          app.slots = {}; app.nextSlot = 0;
          app.seen = []; app.seenSet = {}; app.scanned = 0;
          app.drill = null;
          app.resetAt = null;   // a new file is its own fresh start
          $('drillCard').hidden = true;
        }
        app.offset = d.nextOffset;

        var lines = d.lines || [];
        for (var i = 0; i < lines.length; i++) app.parser.feed(lines[i]);
        app.lines += lines.length;

        if (lines.length) app.parser.roster.rebuild(app.parser.events);

        setStatus(app.file, 'live');
        // Idle polls must not rebuild the tables -- that would reset scroll
        // position and kill text selection once a second for no new data.
        if (lines.length || !app.rendered) { app.rendered = true; render(); }
      })
      .catch(function (e) {
        app.error = e.message || String(e);
        setStatus('server unreachable — is damage-meter.ps1 still running?', 'err');
      })
      .then(schedule);
  }

  function schedule() { setTimeout(poll, POLL_MS); }

  function setStatus(text, cls) {
    $('srcFile').textContent = text;
    $('liveDot').className = 'dot ' + (cls || '');
    var ev = app.parser.events.length;
    $('srcCount').textContent = S.fmtInt(ev) + ' event' + (ev === 1 ? '' : 's') +
      ' · ' + S.fmtInt(app.lines) + ' lines' +
      (app.resetAt ? ' · since reset at ' + S.fmtClock(app.resetAt) : '');
  }

  // ------------------------------------------------------------------ window

  /* Resolves the range control into an absolute [from, to] over event time. */
  function windowOf(events) {
    if (!events.length) return { from: -Infinity, to: Infinity };
    var last = events[events.length - 1].t;

    if (app.range === 'all') return { from: -Infinity, to: Infinity };

    if (app.range === 'fight') {
      // Walk back from the newest event until a gap longer than FIGHT_GAP_MS.
      var start = last;
      for (var i = events.length - 1; i > 0; i--) {
        if (events[i].t - events[i - 1].t > FIGHT_GAP_MS) { start = events[i].t; break; }
        start = events[i - 1].t;
      }
      return { from: start, to: Infinity };
    }
    return { from: last - (+app.range) * 1000, to: Infinity };
  }

  // ------------------------------------------------------------------ render

  function render() {
    var all = app.parser.events;
    var roster = app.parser.roster;
    assignSlots();

    var win = windowOf(all);
    var scoped = S.filter(all, {
      from: win.from, to: win.to, side: app.side, roster: roster
    });

    var enabled = {};
    Object.keys(app.actorsOff).forEach(function (n) { enabled[n] = false; });
    var shown = S.filter(scoped, { actors: enabled });

    var agg = S.aggregate(shown);

    renderChips(S.aggregate(scoped).actors);
    renderTiles(agg);
    renderLine(shown, agg);
    renderBars(agg);
    renderActions(agg);
    renderDrill(shown);
    renderDiagnostics(roster);
  }

  // ---- character chips (the per-entity on/off filter)

  function renderChips(actors) {
    var host = $('actorChips');
    var names = actors.map(function (a) { return a.name; });
    var same = names.join(' ') === app.visibleActors.join(' ');
    app.visibleActors = names;

    if (!actors.length) {
      if (!same) host.innerHTML = '<span class="muted small">&mdash;</span>';
      $('charLabel').textContent = 'Characters';
      return;
    }

    var on = actors.filter(function (a) { return !app.actorsOff[a.name]; }).length;
    $('charLabel').textContent = 'Characters ' + on + '/' + actors.length;

    function state(chip, name) {
      var inc = !app.actorsOff[name];
      chip.setAttribute('aria-pressed', String(inc));
      chip.title = (inc ? 'Exclude ' : 'Include ') + name;
    }

    // Toggling only changes pressed state. Rebuilding the list for that would
    // drop focus from the chip the user just activated, so the common case
    // updates in place and only a changed cast rebuilds.
    if (same) {
      [].forEach.call(host.querySelectorAll('.chip'), function (chip) {
        state(chip, chip.dataset.actor);
      });
      return;
    }

    host.innerHTML = actors.map(function (a) {
      return '<button type="button" class="chip" data-actor="' + esc(a.name) + '">' +
             '<i style="background:' + colorOf(a.name) + '"></i>' + esc(a.name) + '</button>';
    }).join('');
    [].forEach.call(host.querySelectorAll('.chip'), function (chip) {
      state(chip, chip.dataset.actor);
    });
  }

  // ---- hero + stat tiles

  function renderTiles(agg) {
    $('tTotal').textContent = S.fmtInt(agg.total);
    $('tTotalSub').textContent = agg.start
      ? S.fmtClock(agg.start) + ' → ' + S.fmtClock(agg.end) + ' · ' + S.fmtDuration(agg.duration)
      : 'no events in range';

    var dps = agg.duration > 0 ? agg.total / agg.duration : 0;
    $('tDps').textContent = S.fmtNum(dps, 1);
    $('tDpsSub').textContent = agg.actors.length
      ? agg.actors.length + ' character' + (agg.actors.length === 1 ? '' : 's')
      : '—';

    // `aggregate` returns actors sorted by total damage, so the leader is [0].
    // The dot carries the same hue the character has on every chart.
    var top = agg.actors[0];
    $('tTopDot').style.background = top ? colorOf(top.name) : 'transparent';
    $('tTopName').textContent = top ? top.name : '—';
    $('tTopName').title = top ? top.name : '';
    $('tTopSub').textContent = top
      ? S.fmtNum(top.share * 100, 1) + '% of ' + S.fmtInt(agg.total) + ' damage'
      : '—';

    var best = null;
    agg.actors.forEach(function (a) {
      a.actionList.forEach(function (act) {
        if (!best || act.max > best.max) best = { max: act.max, who: a.name, what: act.name };
      });
    });
    $('tBig').textContent = best ? S.fmtInt(best.max) : '0';
    $('tBigSub').textContent = best ? best.who + ' · ' + best.what : '—';
  }

  // ---- cumulative line chart

  function renderLine(events, agg) {
    // Charted series are capped at the eight fixed slots; anything past that
    // folds into one "Other" line rather than getting a generated hue.
    var named = [], other = [];
    agg.actors.forEach(function (a) {
      (slotOf(a.name) >= 0 ? named : other).push(a.name);
    });

    var model = S.cumulative(events, named.concat(other.length ? ['__other__'] : []), {});

    if (other.length && model.series.length) {
      // Re-bin the tail into the synthetic series.
      var otherSet = {};
      other.forEach(function (n) { otherSet[n] = true; });
      var oi = model.series.length - 1;
      var o = model.series[oi];
      var n = model.times.length, run = 0, j;
      var raw = new Float64Array(n);
      for (var i = 0; i < events.length; i++) {
        var e = events[i];
        if (!e.hit || !e.dmg || !otherSet[e.actor]) continue;
        raw[Math.min(n - 1, Math.floor((e.t - model.t0) / model.step))] += e.dmg;
      }
      for (j = 0; j < n; j++) { run += raw[j]; o.values[j] = run; }
      o.name = 'Other (' + other.length + ')';
    }

    model.series.forEach(function (s, i) {
      s.color = i < named.length ? colorOf(named[i]) : cssVar('--text-muted');
    });
    // Largest total last so the leading line is drawn on top of the pack.
    model.series.sort(function (a, b) {
      return (a.values[a.values.length - 1] || 0) - (b.values[b.values.length - 1] || 0);
    });

    C.line($('lineChart'), model, { empty: 'No damage in the selected range' });
    renderLegend(model.series);
    renderLineTable(model);
  }

  function renderLegend(series) {
    var host = $('lineLegend');
    if (series.length < 2) { host.innerHTML = ''; return; }   // one series: title says it
    host.innerHTML = series.slice().reverse().map(function (s) {
      return '<span class="legend-item"><i style="background:' + s.color + '"></i>' +
             esc(s.name) + '</span>';
    }).join('');
  }

  /* The chart's WCAG-clean twin. Sampled to ~16 rows so it stays readable. */
  function renderLineTable(model) {
    var t = $('lineTable');
    if (!model.times.length) { t.innerHTML = ''; return; }
    var stride = Math.max(1, Math.ceil(model.times.length / 16));
    var idx = [];
    for (var i = 0; i < model.times.length; i += stride) idx.push(i);
    if (idx[idx.length - 1] !== model.times.length - 1) idx.push(model.times.length - 1);

    var cols = model.series.slice().reverse();
    t.innerHTML =
      '<thead><tr><th>Time</th>' +
      cols.map(function (s) { return '<th>' + esc(s.name) + '</th>'; }).join('') +
      '</tr></thead><tbody>' +
      idx.map(function (k) {
        return '<tr><td>' + S.fmtClock(model.times[k]) + '</td>' +
          cols.map(function (s) { return '<td>' + S.fmtInt(s.values[k]) + '</td>'; }).join('') +
          '</tr>';
      }).join('') + '</tbody>';
  }

  // ---- damage by character

  function renderBars(agg) {
    var rows = agg.actors.map(function (a) {
      return {
        label: a.name,
        value: a.total,
        color: colorOf(a.name),
        sub: '<table>' +
             '<tr><td>Damage</td><td>' + S.fmtInt(a.total) + '</td></tr>' +
             '<tr><td>Share</td><td>' + S.fmtNum(a.share * 100, 1) + '%</td></tr>' +
             '<tr><td>DPS</td><td>' + S.fmtNum(a.dps, 1) + '</td></tr>' +
             '<tr><td>Avg / action</td><td>' + S.fmtInt(a.avg) + '</td></tr>' +
             '</table>'
      };
    });
    $('barsWrap').style.height = Math.max(120, rows.length * 34 + 16) + 'px';
    C.bars($('barsChart'), rows, { empty: 'No damage in the selected range' });

    $('actorTable').innerHTML = rows.length
      ? '<thead><tr><th>Character</th><th>Damage</th><th>Share</th><th>DPS</th>' +
        '<th>Actions</th><th>Avg</th><th>Best</th><th>Acc</th></tr></thead><tbody>' +
        agg.actors.map(function (a) {
          return '<tr><td><span class="swatch" style="background:' + colorOf(a.name) + '"></span>' +
            esc(a.name) + '</td>' +
            '<td>' + S.fmtInt(a.total) + '</td>' +
            '<td>' + S.fmtNum(a.share * 100, 1) + '%</td>' +
            '<td>' + S.fmtNum(a.dps, 1) + '</td>' +
            '<td>' + S.fmtInt(a.hits) + '</td>' +
            '<td>' + S.fmtInt(a.avg) + '</td>' +
            '<td>' + S.fmtInt(a.max) + '</td>' +
            '<td>' + S.fmtNum(a.accuracy * 100, 0) + '%</td></tr>';
        }).join('') + '</tbody>'
      : '';
  }

  // ---- action breakdown, grouped by character

  function renderActions(agg) {
    var t = $('actionTable');
    var pane = t.parentNode;
    var keepScroll = pane.scrollTop;
    if (!agg.actors.length) { t.innerHTML = ''; return; }

    var html = '<thead><tr><th>Action</th><th>Hits</th><th>Miss</th><th>Total</th><th>Avg</th>' +
               '<th>Min</th><th>Max</th><th>Share</th></tr></thead><tbody>';

    agg.actors.forEach(function (a) {
      html += '<tr class="group"><td colspan="8">' +
              '<span class="swatch" style="background:' + colorOf(a.name) + '"></span>' +
              esc(a.name) + ' &mdash; ' + S.fmtInt(a.total) + '</td></tr>';

      a.actionList.forEach(function (act) {
        var on = app.drill && app.drill.actor === a.name && app.drill.action === act.name;
        html += '<tr class="sub pick' + (on ? ' on' : '') + '"' +
                ' data-actor="' + esc(a.name) + '" data-action="' + esc(act.name) + '">' +
                '<td>' + esc(act.name) + '</td>' +
                '<td>' + S.fmtInt(act.hits) + '</td>' +
                '<td>' + (act.misses ? S.fmtInt(act.misses) : '—') + '</td>' +
                '<td>' + S.fmtInt(act.total) + '</td>' +
                '<td>' + S.fmtInt(act.avg) + '</td>' +
                '<td>' + S.fmtInt(act.min) + '</td>' +
                '<td>' + S.fmtInt(act.max) + '</td>' +
                '<td>' + S.fmtNum(a.total ? act.total / a.total * 100 : 0, 1) + '%</td></tr>';
      });
    });

    t.innerHTML = html + '</tbody>';
    pane.scrollTop = keepScroll;
  }

  // ---- drill-down

  function renderDrill(events) {
    var card = $('drillCard');
    if (!app.drill) { card.hidden = true; return; }

    var d = S.distribution(events, app.drill.actor, app.drill.action);
    card.hidden = false;
    $('drillTitle').textContent = app.drill.action;
    $('drillSub').textContent = app.drill.actor + ' · ' + S.fmtInt(d.count) +
      ' hit' + (d.count === 1 ? '' : 's') +
      (d.misses ? ' · ' + d.misses + ' miss' + (d.misses === 1 ? '' : 'es') : '') +
      ' · ' + S.fmtInt(d.total) + ' total damage';

    if (!d.count) {
      $('drillTiles').innerHTML = '<div class="tile"><div class="tile-label">No hits in range</div></div>';
      C.histogram($('histChart'), null, {});
      $('histCap').textContent = '';
      $('drillTable').innerHTML = '';
      return;
    }

    var tiles = [
      ['Min', S.fmtInt(d.min)],
      ['Average', S.fmtInt(d.avg)],
      ['Max', S.fmtInt(d.max)],
      ['Median', S.fmtInt(d.median)],
      ['Std dev', S.fmtInt(d.stdev)],
      ['Accuracy', S.fmtNum(d.accuracy * 100, 0) + '%']
    ];
    $('drillTiles').innerHTML = tiles.map(function (t) {
      return '<div class="tile"><div class="tile-label">' + t[0] + '</div>' +
             '<div class="tile-value">' + t[1] + '</div></div>';
    }).join('');

    C.histogram($('histChart'), d, { color: colorOf(app.drill.actor) });
    $('histCap').textContent =
      'Damage distribution · ' + d.bins.length + ' bins · ' +
      'IQR ' + S.fmtInt(d.q1) + '–' + S.fmtInt(d.q3) + ' · ' +
      '90th percentile ' + S.fmtInt(d.p90) +
      (d.crits ? ' · ' + S.fmtNum(d.critRate * 100, 0) + '% crit' : '') +
      (d.bursts ? ' · ' + d.bursts + ' magic burst' : '');

    $('drillTable').innerHTML =
      '<thead><tr><th>Time</th><th>Target</th><th>Damage</th><th>vs avg</th></tr></thead><tbody>' +
      d.events.slice().reverse().map(function (e) {
        var delta = e.dmg - d.avg;
        return '<tr><td>' + S.fmtClock(e.t) + '</td>' +
               '<td>' + esc(e.target || '—') + '</td>' +
               '<td>' + S.fmtInt(e.dmg) + (e.crit ? ' ✦' : '') + '</td>' +
               '<td>' + (delta >= 0 ? '+' : '−') + S.fmtInt(Math.abs(delta)) + '</td></tr>';
      }).join('') + '</tbody>';
  }

  // ---- diagnostics

  function renderDiagnostics(roster) {
    var names = {};
    app.parser.events.forEach(function (e) {
      if (e.actor) names[e.actor] = true;
      if (e.target) names[e.target] = true;
    });
    var list = Object.keys(names).sort();

    $('rosterTable').innerHTML = list.length
      ? '<thead><tr><th>Name</th><th>Classified</th><th></th></tr></thead><tbody>' +
        list.map(function (n) {
          var mob = roster.isMob(n);
          return '<tr><td>' + esc(n) + '</td>' +
                 '<td>' + (mob ? 'Monster' : 'Character') +
                 (roster.manual[n] ? ' (manual)' : '') + '</td>' +
                 '<td><button type="button" class="roster-toggle" data-name="' + esc(n) + '" ' +
                 'data-to="' + (mob ? 'ally' : 'mob') + '">' +
                 (mob ? 'Mark as character' : 'Mark as monster') + '</button></td></tr>';
        }).join('') + '</tbody>'
      : '';

    var up = app.parser.unparsed;
    $('unparsed').textContent = up.length
      ? up.slice(-60).map(function (u) { return 'line ' + u.line + ': ' + u.text; }).join('\n')
      : 'none';
  }

  // ------------------------------------------------------------------ events

  function segHandler(id, key, after) {
    $(id).addEventListener('click', function (ev) {
      var b = ev.target.closest('button[role="radio"]');
      if (!b) return;
      [].forEach.call(this.querySelectorAll('button'), function (x) {
        var on = x === b;
        x.classList.toggle('on', on);
        x.setAttribute('aria-checked', String(on));
      });
      app[key] = b.dataset[key === 'range' ? 'range' : 'side'];
      if (after) after();
      render();
    });
  }

  segHandler('rangeSeg', 'range');
  segHandler('sideSeg', 'side', function () {
    // The exclusion map is keyed by name and the two sides do not share names,
    // so it carries across untouched -- only the drill-down, which names one
    // actor, has to go.
    app.drill = null;
  });

  $('actorChips').addEventListener('click', function (ev) {
    var b = ev.target.closest('.chip');
    if (!b) return;
    var name = b.dataset.actor;
    if (app.actorsOff[name]) delete app.actorsOff[name]; else app.actorsOff[name] = true;
    saveExcluded();
    render();
  });

  /* Bulk include / exclude, scoped to the characters currently on screen. */
  $('pickAll').addEventListener('click', function () {
    (app.visibleActors || []).forEach(function (n) { delete app.actorsOff[n]; });
    saveExcluded();
    render();
  });

  $('pickNone').addEventListener('click', function () {
    (app.visibleActors || []).forEach(function (n) { app.actorsOff[n] = true; });
    saveExcluded();
    app.drill = null;
    render();
  });

  $('resetBtn').addEventListener('click', resetMeter);

  $('actionTable').addEventListener('click', function (ev) {
    var tr = ev.target.closest('tr.pick');
    if (!tr) return;
    var a = tr.dataset.actor, k = tr.dataset.action;
    if (app.drill && app.drill.actor === a && app.drill.action === k) app.drill = null;
    else app.drill = { actor: a, action: k };
    render();
    if (app.drill) $('drillCard').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  });

  $('drillClose').addEventListener('click', function () { app.drill = null; render(); });

  $('rosterTable').addEventListener('click', function (ev) {
    var b = ev.target.closest('.roster-toggle');
    if (!b) return;
    app.parser.roster.setManual(b.dataset.name, b.dataset.to);
    resetColors();
    render();
  });

  $('pauseBtn').addEventListener('click', function () {
    app.paused = !app.paused;
    this.setAttribute('aria-pressed', String(app.paused));
    this.textContent = app.paused ? 'Resume' : 'Pause';
    $('liveDot').className = 'dot ' + (app.paused ? 'stale' : 'live');
  });

  // Toggle, persistence and the button label all live in the shared theme
  // module; the only app-specific part is that the charts must be redrawn,
  // because canvas can't restyle itself the way the DOM does.
  window.FFXITheme.bind({
    button: 'themeBtn',
    storageKey: 'ffxi_dps_theme',
    onChange: function (name) { DPS.popout.theme(name); render(); }
  });

  var resizeTimer;
  window.addEventListener('resize', function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(render, 120);
  });

  // ------------------------------------------------------------------- start

  loadExcluded();

  // After the handlers above, not before: wiring a card for pop-out moves the
  // buttons already in its head into the new controls group, and a listener
  // attached to a button survives being moved but is not re-attached.
  DPS.popout.init({ onRender: render });

  window.DPS.app = app;   // console handle for debugging
  poll();
})();
