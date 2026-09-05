/*
 * app.js -- polling, state and rendering.
 *
 * The server hands over raw JSONL lines from the addon's event file and nothing
 * else; reading them (lib/source.js) and aggregating them (lib/stats.js) both
 * run here, so a change to either is a browser refresh rather than a server
 * restart.
 */
(function () {
  'use strict';

  var P = DPS.source, S = DPS.stats, C = DPS.chart;
  // Not getElementById: a popped-out card's nodes live in another document, and
  // getElementById only searches its own. lib/popout.js keeps the index.
  var $ = DPS.popout.byId;
  var esc = C.esc;

  var POLL_MS = 250;
  var FIGHT_GAP_MS = 90000;   // silence longer than this starts a new fight

  var app = {
    file: null,
    offset: 0,
    source: P.create(null),
    lines: 0,
    range: 'all',
    chains: 'on',           // 'on' credits skillchain damage, 'off' drops it
    colour: 'job',          // 'job' paints by FFXI job, 'slot' by series palette
    actorsOff: {},          // name -> true when excluded; persisted
    resetAt: null,          // wall-clock of the last meter reset, for the status line
    paused: false,
    drill: null,            // { actor, action }
    slots: {},              // name -> colour slot 0-7, or -1 for "past eight"
    nextSlot: 0,
    jobVariant: {},         // name -> 0,1,2... among characters sharing a job
    seen: [],               // distinct actor names, first-appearance order
    seenSet: {},
    scanned: 0,             // how far into the event list `seen` is built
    visibleActors: [],      // names with a chip right now; scopes All / None
    chipSig: null,          // name+job signature of the built chips
    chipsOpen: true,        // character list expanded; persisted
    lastOk: 0,
    error: null
  };

  // ------------------------------------------------------------------ colors

  /*
   * Colour follows the entity, never its rank or its row: a slot is assigned
   * once and never reassigned, so a character keeps its hue when a filter or a
   * lead change reorders the table.
   *
   * The file's owner always takes slot 0. They are the one character the user
   * looks for first and the one who is on every chart of every session, so
   * their hue is the one that must not drift -- and it otherwise would, since
   * slots go in first-seen order and whether the owner or the tank swings first
   * is luck. Everyone else takes the next free slot in the order they appear.
   *
   * Only characters are slotted. Monsters never appear as an actor anywhere in
   * this app, so spending a slot on one would push a real party member further
   * down the palette for nothing. `seen` still records every name, so a name
   * reclassified into the party by hand picks up the next free slot.
   *
   * There are FFXITheme.SLOTS (18) of them, one per alliance member, and past
   * that the palette wraps and a hue repeats. Nothing here folds a tail into a
   * shared muted "Other": every character gets their own colour, and the legend,
   * the table columns and the hover all name them anyway.
   */
  function scanActors() {
    var ev = app.source.events;
    for (var i = app.scanned; i < ev.length; i++) {
      // The credited name: a pet's colour is its owner's, because its damage
      // is. Otherwise every pet burns an alliance colour slot for a series
      // that is never drawn.
      var n = ev[i].owner || ev[i].actor;
      if (n && !app.seenSet[n]) { app.seenSet[n] = true; app.seen.push(n); }
    }
    app.scanned = ev.length;
  }

  function assignSlots() {
    scanActors();
    var roster = app.source.roster;
    // Before anyone else, so slot 0 is the owner's whoever swung first.
    var owner = roster.owner;
    if (owner && !(owner in app.slots) && !roster.isMob(owner)) app.slots[owner] = app.nextSlot++;
    for (var i = 0; i < app.seen.length; i++) {
      var n = app.seen[i];
      if (n in app.slots) continue;
      if (roster.isMob(n)) continue;
      app.slots[n] = app.nextSlot++;
    }
    // Cheap and unconditional: a job line can land at any time, and a variant
    // index that lags one render behind is a character changing colour a beat
    // after everything else that names them changed.
    assignJobVariants();
  }

  function slotOf(name) {
    if (!(name in app.slots)) app.slots[name] = app.nextSlot++;
    return app.slots[name];
  }

  /*
   * WHICH WARRIOR IS THIS -- the first, the second, the third?
   *
   * A job palette has a hole a categorical one does not: two warriors are one
   * colour, and two identical lines on the cumulative chart cannot be read at
   * all. So each character sharing a job gets a numbered variant of it, and
   * FFXITheme.step turns that number into a lightness move.
   *
   * ORDERED BY COLOUR SLOT, not by damage or by table position. Slots are handed
   * out once in first-seen order and never reassigned, so this ordering is
   * stable for the session -- the same warrior keeps the same shade when the
   * lead changes, a filter moves, or the range control is touched. Ordering it
   * by anything the user can reorder would make the party swap shades under
   * them, which is the exact failure the slot rule exists to prevent.
   */
  function assignJobVariants() {
    var roster = app.source.roster;
    var names = Object.keys(app.slots).sort(function (a, b) {
      return app.slots[a] - app.slots[b];
    });
    var used = {};
    app.jobVariant = {};
    for (var i = 0; i < names.length; i++) {
      var j = roster.jobOf(names[i]);
      if (!j || !j.main || j.main === 'NON') continue;
      var k = used[j.main] || 0;
      app.jobVariant[names[i]] = k;
      used[j.main] = k + 1;
    }
  }

  /* Reclassification changes who counts as a party member, so slots restart. */
  function resetColors() {
    app.slots = {};
    app.nextSlot = 0;
    app.chipSig = null;
    assignSlots();
  }

  // ------------------------------------------------------------- persistence

  var EXCLUDE_KEY = 'ffxi_dps_excluded';
  var CHAIN_KEY = 'ffxi_dps_chains';
  var CHARROW_KEY = 'ffxi_dps_charrow';
  var COLOUR_KEY = 'ffxi_dps_colour';

  function loadExcluded() {
    try {
      var raw = localStorage.getItem(EXCLUDE_KEY);
      if (raw) app.actorsOff = JSON.parse(raw) || {};
    } catch (e) { app.actorsOff = {}; }
  }

  function saveExcluded() {
    try { localStorage.setItem(EXCLUDE_KEY, JSON.stringify(app.actorsOff)); } catch (e) { }
  }

  /* The markup's default is "on", so only the off state has to be restored. */
  function loadChains() {
    try {
      if (localStorage.getItem(CHAIN_KEY) === 'off') app.chains = 'off';
    } catch (e) { }
    syncSeg('chainSeg', 'chains', app.chains);
  }

  /* Same shape again: the markup's default is "job", so only "slot" restores. */
  function loadColour() {
    try {
      if (localStorage.getItem(COLOUR_KEY) === 'slot') app.colour = 'slot';
    } catch (e) { }
    syncSeg('colourSeg', 'colour', app.colour);
  }

  /* Same shape: the markup ships expanded, so only "closed" is restored. */
  function loadCharRow() {
    try {
      if (localStorage.getItem(CHARROW_KEY) === 'closed') app.chipsOpen = false;
    } catch (e) { }
    applyCharRow();
  }

  function applyCharRow() {
    $('charRow').classList.toggle('collapsed', !app.chipsOpen);
    var b = $('charToggle');
    b.setAttribute('aria-expanded', String(app.chipsOpen));
    b.textContent = app.chipsOpen ? 'Hide' : 'Show';
    b.title = (app.chipsOpen ? 'Collapse' : 'Expand') + ' the character list';
  }

  // ------------------------------------------------------------------- reset

  /*
   * Drops every event collected so far and starts counting from the current
   * point in the event file. The read offset is deliberately left alone -- this
   * is the "clear the meter between pulls" button, not a re-read; reloading the
   * page is what replays the whole file from the top.
   *
   * Deliberately kept across a reset:
   *   - colour slots, so a character does not change hue mid-session
   *   - the roster, including any manual override, so monsters stay monsters
   *   - every filter, which is user intent rather than collected data
   */
  function resetMeter() {
    app.source.reset();
    app.scanned = 0;          // `seen` is kept; only the scan cursor rewinds
    app.drill = null;
    app.resetAt = Date.now();
    $('drillCard').hidden = true;
    app.rendered = true;
    render();
    setStatus(app.file || 'waiting for the addon', app.paused ? 'stale' : 'live');
  }

  /*
   * The colour a character is drawn in, everywhere in the app.
   *
   * Via FFXITheme, not a local getComputedStyle: it caches per theme, and both
   * palettes stay in the one stylesheet that drives both apps and both modes.
   *
   * TWO PALETTES, and the switch between them is a real trade rather than a
   * preference:
   *
   *   job    Metrics' own job colours. The party already reads these at a
   *          glance in game -- the warrior is red, the samurai orange -- so the
   *          meter agrees with the parser sitting next to it instead of
   *          inventing a second mapping for the same six people. It is NOT
   *          colourblind-separable (WAR, NIN, RDM and SAM are four reds), and
   *          two characters on the same job differ only by a lightness step.
   *   slot   The --series ramp: eighteen hues solved for maximum worst-case
   *          separation under normal, protan and deutan vision. Every character
   *          is distinct; none of them says anything about the character.
   *
   * Job is the default because recognition beats separation when the panel
   * names everyone anyway -- and it does: legend, table rows, chips and hover
   * all carry the name and the job in text. A character with no job on record
   * falls through to their slot rather than to a made-up colour, so a trust, a
   * pet's owner seen only through their pet, or anyone the party table has not
   * reported yet is still drawn.
   */
  function colorOf(name) {
    if (app.colour === 'job') {
      var j = app.source.roster.jobOf(name);
      var base = j && j.main ? FFXITheme.job(j.main) : '';
      if (base) return FFXITheme.step(base, app.jobVariant[name] || 0);
    }
    return FFXITheme.series(slotOf(name));
  }

  /* "WAR/NIN", or '' when the addon has not reported this character's job. */
  function jobOf(name) {
    return app.source.roster.jobLabel(name);
  }

  /* The same, as a fragment for a table cell or a chip: '—' when unknown. */
  function jobCell(name) {
    var s = jobOf(name);
    return s ? esc(s) : '<span class="muted">—</span>';
  }

  // ------------------------------------------------------------------- fetch

  function poll() {
    if (app.paused) { schedule(); return; }

    var qs = '?offset=' + app.offset + (app.file ? '&file=' + encodeURIComponent(app.file) : '');
    fetch('/api/events' + qs, { cache: 'no-store' })
      .then(function (r) { return r.json(); })
      .then(function (d) {
        app.error = null;
        app.lastOk = Date.now();

        if (!d.file) {
          setStatus('waiting for the addon — is VibeXI loaded?', 'stale');
          schedule(); return;
        }

        if (d.reset || d.file !== app.file) {
          var meta = P.parseFilename(d.file);
          app.file = d.file;
          app.source = P.create(meta.owner);
          app.lines = 0;
          app.slots = {}; app.nextSlot = 0;
          app.seen = []; app.seenSet = {}; app.scanned = 0;
          app.drill = null;
          app.resetAt = null;   // a new file is its own fresh start
          $('drillCard').hidden = true;
        }
        app.offset = d.nextOffset;

        var lines = d.lines || [];
        for (var i = 0; i < lines.length; i++) app.source.feed(lines[i]);
        app.lines += lines.length;

        setStatus(app.file, 'live');
        // Idle polls must not rebuild the tables -- that would reset scroll
        // position and kill text selection once a second for no new data.
        if (lines.length || !app.rendered) { app.rendered = true; render(); }
      })
      .catch(function (e) {
        app.error = e.message || String(e);
        setStatus('server unreachable — is damage-meter.py still running?', 'err');
      })
      .then(schedule);
  }

  function schedule() { setTimeout(poll, POLL_MS); }

  function setStatus(text, cls) {
    $('srcFile').textContent = text;
    $('liveDot').className = 'dot ' + (cls || '');
    var ev = app.source.events.length;
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
    var all = app.source.events;
    var roster = app.source.roster;
    assignSlots();

    var win = windowOf(all);
    // Passing the roster is what drops the monsters' own damage: this meter
    // counts what the party dealt and nothing else.
    var scoped = S.filter(all, {
      from: win.from, to: win.to, roster: roster,
      skillchains: app.chains === 'on'
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
    /* The job rides in the signature, not just the name: a job line can land
       long after the character's first swing, and a chip built before it has to
       be rebuilt when it does. */
    var sig = actors.map(function (a) { return a.name + '\u0001' + jobOf(a.name); }).join('\0');
    var same = sig === app.chipSig;
    app.chipSig = sig;
    app.visibleActors = names;

    if (!actors.length) {
      if (!same) host.innerHTML = '<span class="muted small">&mdash;</span>';
      $('charLabel').textContent = 'Characters';
      $('charHint').textContent = '';
      return;
    }

    var off = actors.filter(function (a) { return app.actorsOff[a.name]; })
                    .map(function (a) { return a.name; });
    $('charLabel').textContent =
      'Characters ' + (actors.length - off.length) + '/' + actors.length;
    // Read while the list is collapsed, so an exclusion can never hide silently.
    // Long lists stop naming names; the count is the part that matters.
    $('charHint').textContent = !off.length ? 'all included'
      : off.length > 4 ? off.length + ' excluded'
      : off.length + ' excluded: ' + off.join(', ');

    function state(chip, name) {
      var inc = !app.actorsOff[name];
      var full = app.source.roster.jobTitle(name);
      chip.setAttribute('aria-pressed', String(inc));
      chip.title = (inc ? 'Exclude ' : 'Include ') + name + (full ? ' · ' + full : '');
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
      var job = jobOf(a.name);
      return '<button type="button" class="chip" data-actor="' + esc(a.name) + '">' +
             '<i style="background:' + colorOf(a.name) + '"></i>' + esc(a.name) +
             (job ? '<b class="chip-job">' + esc(job) + '</b>' : '') + '</button>';
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
    var topJob = top ? jobOf(top.name) : '';
    $('tTopDot').style.background = top ? colorOf(top.name) : 'transparent';
    $('tTopName').textContent = top ? top.name : '—';
    $('tTopName').title = top ? (app.source.roster.jobTitle(top.name) || top.name) : '';
    $('tTopSub').textContent = top
      ? (topJob ? topJob + ' · ' : '') +
        S.fmtNum(top.share * 100, 1) + '% of ' + S.fmtInt(agg.total) + ' damage'
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
    // One line per character, each in that character's own slot colour.
    var names = agg.actors.map(function (a) { return a.name; });

    var model = S.cumulative(events, names, {});

    model.series.forEach(function (s) { s.color = colorOf(s.name); });
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
    // The job goes in the legend as text. It is what makes the job palette safe
    // to default to: two warriors differ by a lightness step in the swatch, and
    // by their name in the very same line.
    host.innerHTML = series.slice().reverse().map(function (s) {
      var job = jobOf(s.name);
      return '<span class="legend-item"><i style="background:' + s.color + '"></i>' +
             esc(s.name) + (job ? '<b class="legend-job">' + esc(job) + '</b>' : '') +
             '</span>';
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
      var full = app.source.roster.jobTitle(a.name);
      return {
        label: a.name,
        value: a.total,
        color: colorOf(a.name),
        sub: '<table>' +
             (full ? '<tr><td>Job</td><td>' + esc(full) + '</td></tr>' : '') +
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
      ? '<thead><tr><th>Character</th><th class="job">Job</th><th>Damage</th><th>Share</th><th>DPS</th>' +
        '<th>Actions</th><th>Avg</th><th>Best</th><th>Acc</th></tr></thead><tbody>' +
        agg.actors.map(function (a) {
          return '<tr><td><span class="swatch" style="background:' + colorOf(a.name) + '"></span>' +
            esc(a.name) + '</td>' +
            '<td class="job" title="' + esc(app.source.roster.jobTitle(a.name)) + '">' +
            jobCell(a.name) + '</td>' +
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
      var job = jobOf(a.name);
      html += '<tr class="group"><td colspan="8">' +
              '<span class="swatch" style="background:' + colorOf(a.name) + '"></span>' +
              esc(a.name) +
              (job ? ' <b class="row-job">' + esc(job) + '</b>' : '') +
              ' &mdash; ' + S.fmtInt(a.total) + '</td></tr>';

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
    app.source.events.forEach(function (e) {
      if (e.actor) names[e.actor] = true;
      if (e.target) names[e.target] = true;
    });
    /*
     * The party members the addon reported a job for, whether or not they ever
     * swung. This is the one panel in the app that lists the party rather than
     * the damage, so it is the only place a white mage who healed all night
     * appears at all -- and the place to look when a name is missing from the
     * charts and you want to know whether the addon ever saw them.
     */
    Object.keys(roster.jobs).forEach(function (n) { names[n] = true; });
    var list = Object.keys(names).sort();

    // The Kind column is the addon's answer, straight off the entity's spawn
    // flags; the Counted column is what this meter does with it. They differ
    // only where the user has overridden one by hand.
    $('rosterTable').innerHTML = list.length
      ? '<thead><tr><th>Name</th><th class="job">Job</th><th>Kind</th><th>Counted</th><th></th></tr></thead><tbody>' +
        list.map(function (n) {
          var mob = roster.isMob(n);
          return '<tr><td>' + esc(n) + '</td>' +
                 '<td class="job" title="' + esc(roster.jobTitle(n)) + '">' + jobCell(n) + '</td>' +
                 '<td>' + esc(roster.kindOf(n)) + '</td>' +
                 '<td>' + (mob ? 'No' : 'Yes') +
                 (roster.manual[n] ? ' (manual)' : '') + '</td>' +
                 '<td><button type="button" class="roster-toggle" data-name="' + esc(n) + '" ' +
                 'data-to="' + (mob ? 'ally' : 'mob') + '">' +
                 (mob ? 'Count this name' : 'Leave this name out') + '</button></td></tr>';
        }).join('') + '</tbody>'
      : '';

    // The addon's own notices: its startup environment probe, and one line per
    // message id it saw and did not recognise. A dropped id is a silent
    // undercount, so it has to be visible somewhere.
    var notes = app.source.meta;
    $('addonMeta').textContent = notes.length
      ? notes.slice(-60).map(function (m) {
          if (m.bad) return 'line ' + m.line + ': not JSON — ' + m.text;
          return 'line ' + m.line + ': ' + JSON.stringify(m.data);
        }).join('\n')
      : 'none';
  }

  // ------------------------------------------------------------------ events

  /* Paints a segmented control from `app[key]`; the data attribute is the key. */
  function syncSeg(id, key, value) {
    [].forEach.call($(id).querySelectorAll('button[role="radio"]'), function (x) {
      var on = x.dataset[key] === value;
      x.classList.toggle('on', on);
      x.setAttribute('aria-checked', String(on));
    });
  }

  function segHandler(id, key, after) {
    $(id).addEventListener('click', function (ev) {
      var b = ev.target.closest('button[role="radio"]');
      if (!b) return;
      app[key] = b.dataset[key];
      syncSeg(id, key, app[key]);
      if (after) after();
      render();
    });
  }

  segHandler('rangeSeg', 'range');
  segHandler('colourSeg', 'colour', function () {
    try { localStorage.setItem(COLOUR_KEY, app.colour); } catch (e) { }
    // The chips carry a colour swatch each and are only rebuilt when their
    // signature changes, which a palette swap does not touch. Drop it so the
    // next render repaints them.
    app.chipSig = null;
  });
  segHandler('chainSeg', 'chains', function () {
    try { localStorage.setItem(CHAIN_KEY, app.chains); } catch (e) { }
    // A drill-down into a skillchain row has no events left to show once the
    // rows are gone, so it closes rather than sitting there empty.
    if (app.chains === 'off' && app.drill && /^Skillchain:/.test(app.drill.action)) {
      app.drill = null;
    }
  });

  $('actorChips').addEventListener('click', function (ev) {
    var b = ev.target.closest('.chip');
    if (!b) return;
    var name = b.dataset.actor;
    if (app.actorsOff[name]) delete app.actorsOff[name]; else app.actorsOff[name] = true;
    saveExcluded();
    render();
  });

  /* Collapsing is pure chrome -- the filter itself is untouched, so no render.
     The label and the hint next to it are written by every render already. */
  $('charToggle').addEventListener('click', function () {
    app.chipsOpen = !app.chipsOpen;
    try { localStorage.setItem(CHARROW_KEY, app.chipsOpen ? 'open' : 'closed'); } catch (e) { }
    applyCharRow();
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
    app.source.roster.setManual(b.dataset.name, b.dataset.to);
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
    onChange: function (name) {
      DPS.popout.theme(name);
      // Both palettes re-step per theme, and the chips only rebuild when their
      // name/job signature changes -- which a theme swap does not touch.
      app.chipSig = null;
      render();
    }
  });

  var resizeTimer;
  window.addEventListener('resize', function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(render, 120);
  });

  // ------------------------------------------------------------------- start

  loadExcluded();
  loadChains();
  loadColour();
  loadCharRow();

  // After the handlers above, not before: wiring a card for pop-out moves the
  // buttons already in its head into the new controls group, and a listener
  // attached to a button survives being moved but is not re-attached.
  DPS.popout.init({ onRender: render });

  window.DPS.app = app;   // console handle for debugging
  poll();
})();
