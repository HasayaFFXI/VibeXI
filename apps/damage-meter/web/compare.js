/*
 * compare.js -- the Compare section: two exported parses, A against B.
 *
 * All DOM writing for that section, and nothing else. The counting is
 * lib/compare.js (DOM-free), which in turn is nothing but stats.filter and
 * stats.aggregate run once per parse -- so a figure here is the figure the
 * meter prints when that parse is imported.
 *
 * Loaded after app.js. It borrows three things from it, through DPS.meter and
 * DPS.app, and owns none of the meter's state:
 *   - the Open dialog Import uses (same picker id, so the same folder)
 *   - the skillchain switch, which is ONE setting: the toggle here clicks the
 *     filter bar's button, so the two can never disagree
 *   - Hide names, read off DPS.app.anon
 *
 * Nothing here is persisted except which section is on screen and the Compare
 * by choice. The parses themselves are a megabyte or two each and live only in
 * the page; a reload empties both slots, exactly as it empties the meter.
 */
(function () {
  'use strict';

  var P = DPS.source, S = DPS.stats, C = DPS.chart, K = DPS.compare;
  var esc = C.esc;
  function $(id) { return document.getElementById(id); }

  var VIEW_KEY = 'ffxi_dps_view';
  var BY_KEY = 'ffxi_dps_cmpby';
  var MODE_KEY = 'ffxi_dps_cmpmode';
  var VIEWS = { damage: true, healing: true, compare: true };

  var cmp = {
    view: 'damage',       // 'damage' | 'healing' | 'compare'
    mode: 'damage',       // what Compare compares: 'damage' | 'healing'
    by: 'actor',          // 'actor' | 'job'
    a: null,              // { run, name, note }
    b: null,
    note: { a: null, b: null },   // the last error per slot
    open: {},             // row key -> true when its actions are expanded
    healOpen: {},         // the same, for the Healing table
    last: null            // the last { ma, mb, d } drawn, for the console
  };

  // ------------------------------------------------------------------- view

  /*
   * Which section is on screen. The meter keeps polling underneath Compare, so
   * a pull being measured is not interrupted by looking at an old one -- it is
   * only redrawn on the way back, because a canvas laid out while hidden
   * measures zero.
   */
  function setView(v) {
    // 'meter' is what this key held before the Healing section existed.
    if (v === 'meter' || !VIEWS[v]) v = 'damage';
    cmp.view = v;
    // app.js reads it to decide whose chips the filter bar lists.
    DPS.app.view = v;
    ['damage', 'healing', 'compare'].forEach(function (k) {
      document.body.classList.toggle('view-' + k, k === v);
      var b = $('view' + k.charAt(0).toUpperCase() + k.slice(1));
      b.classList.toggle('on', k === v);
      b.setAttribute('aria-pressed', String(k === v));
    });
    try { localStorage.setItem(VIEW_KEY, v); } catch (e) { }
    if (v === 'compare') render(); else DPS.meter.render();
  }

  /* Damage or Healing: which of the two the comparison is about. Everything
     else -- the slots, Compare by, Swap -- is shared, so this is a re-render. */
  function setMode(mode) {
    cmp.mode = mode === 'healing' ? 'healing' : 'damage';
    $('cmpModeDamage').classList.toggle('on', cmp.mode === 'damage');
    $('cmpModeHealing').classList.toggle('on', cmp.mode === 'healing');
    $('cmpModeDamage').setAttribute('aria-pressed', String(cmp.mode === 'damage'));
    $('cmpModeHealing').setAttribute('aria-pressed', String(cmp.mode === 'healing'));
    $('compareView').dataset.mode = cmp.mode;
    try { localStorage.setItem(MODE_KEY, cmp.mode); } catch (e) { }
    render();
  }

  function setBy(by) {
    cmp.by = by === 'job' ? 'job' : 'actor';
    cmp.open = {}; cmp.healOpen = {};
    $('cmpByActor').classList.toggle('on', cmp.by === 'actor');
    $('cmpByJob').classList.toggle('on', cmp.by === 'job');
    $('cmpByActor').setAttribute('aria-pressed', String(cmp.by === 'actor'));
    $('cmpByJob').setAttribute('aria-pressed', String(cmp.by === 'job'));
    try { localStorage.setItem(BY_KEY, cmp.by); } catch (e) { }
    render();
  }

  function chainsOn() { return DPS.app.chains === 'on'; }

  function applyChains() {
    var b = $('cmpChainBtn'), on = chainsOn();
    b.setAttribute('aria-pressed', String(on));
    b.title = on
      ? 'Skillchains counted, credited to whoever closed them -- the same switch as the meter\'s. Click to leave them out.'
      : 'Skillchains left out of both runs. Click to count them again.';
  }

  // ------------------------------------------------------------------ loading

  /* Text of an exported parse -> a slot. Errors stay on the slot they belong to. */
  function load(slot, name, text) {
    var r;
    try { r = P.importParse(text); }
    catch (e) { cmp.note[slot] = e.message || String(e); render(); return; }
    cmp[slot] = { run: r, name: name.replace(/\.json$/i, ''), skipped: r.skipped };
    cmp.note[slot] = null;
    cmp.open = {}; cmp.healOpen = {};
    render();
  }

  function openInto(slot) {
    DPS.meter.openFile().then(function (f) { if (f) load(slot, f.name, f.text); },
      function (e) { cmp.note[slot] = 'Could not open: ' + (e && e.message || e); render(); });
  }

  /*
   * The parse on the meter right now, as a slot. Round-tripped through the
   * export format on purpose: that is what makes it measure exactly as it would
   * if it had been exported and opened here, and it is a snapshot -- the live
   * meter keeps counting, this copy does not. A running clock is frozen at the
   * press, as a Pause would freeze it.
   */
  function useCurrent(slot) {
    var app = DPS.app, sn = app.session;
    if (sn.startedAt == null) return;
    var frozen = {
      armedAt: sn.armedAt, startedAt: sn.startedAt,
      spans: sn.spans.slice(), pausedAt: sn.pausedAt != null ? sn.pausedAt : Date.now()
    };
    var doc = P.exportParse(app.source, frozen, {
      file: app.imported ? app.imported.file : app.file,
      keep: function (e) { return S.sessionAt(frozen, e.t) != null; }
    });
    var name = app.imported ? app.imported.name
      : 'Current session ' + S.fmtClock(Date.now());
    load(slot, name, P.stringifyParse(doc));
  }

  function canUseCurrent() {
    return !!DPS.app && DPS.app.session.startedAt != null;
  }

  // ---------------------------------------------------------------- formatting

  function fmtPct(x, dp) { return x == null ? '—' : S.fmtNum(x * 100, dp == null ? 1 : dp) + '%'; }
  function fmtWhole(x) { return x == null ? '—' : S.fmtInt(x); }
  function fmtDps(x) { return x == null ? '—' : S.fmtNum(x, 1); }

  /*
   * A delta, printed with its sign as TEXT and its direction as a colour -- the
   * colour is never the only carrier, so a reader who cannot tell the green from
   * the red still reads "+" and "−".
   *
   *   kind 'pct'   relative change: "+12.3%", the absolute difference beneath
   *   kind 'pt'    a percentage already, so the change is in points: "+2.1 pt"
   *   good  +1 when up is better, -1 when down is, 0 when it is neither
   */
  function deltaCell(a, b, kind, fmt, good) {
    if (a == null || b == null) return '<span class="cmp-d is-na">—</span>';
    var dd = b - a;
    var dir = Math.abs(dd) < 1e-9 ? 0 : dd > 0 ? 1 : -1;
    var cls = !dir || !good ? 'is-flat' : dir * good > 0 ? 'is-up' : 'is-down';
    var sign = dir > 0 ? '+' : dir < 0 ? '−' : '±';
    var main, sub = '';
    if (kind === 'pt') {
      main = sign + S.fmtNum(Math.abs(dd) * 100, 1) + ' pt';
    } else if (kind === 'time') {
      main = sign + S.fmtDuration(Math.abs(dd));
    } else {
      main = a ? sign + S.fmtNum(Math.abs(dd / a) * 100, 1) + '%' : (dir ? 'new' : '±0');
      sub = sign + fmt(Math.abs(dd));
    }
    return '<span class="cmp-d ' + cls + '">' + main +
      (sub ? '<small>' + sub + '</small>' : '') + '</span>';
  }

  /* A over B, in one cell. The two ticks carry the runs' colours. */
  function ab(a, b) {
    return '<span class="cmp-ab"><span class="ra">' + a + '</span><span class="rb">' + b + '</span></span>';
  }

  /* Two bars in one cell, scaled to `max`, each with its figure. */
  function bars(a, b, max, fmt) {
    function one(v, cls) {
      var w = max > 0 && v ? Math.max(0.5, v / max * 100) : 0;
      return '<span class="cmp-bar ' + cls + '"><i style="width:' + w.toFixed(2) + '%"></i>' +
             '<b>' + (v == null ? '—' : fmt(v)) + '</b></span>';
    }
    return '<div class="cmp-bars">' + one(a, 'ra') + one(b, 'rb') + '</div>';
  }

  // -------------------------------------------------------------- identities

  /*
   * A row's colour, job and drawn name. By character the job comes off whichever
   * run the character was in, B first (the newer run is the one being judged);
   * by job the key IS the job.
   *
   * Hide names draws EVERY character as their job, numbered when two share one.
   * There is no owner here: two files can belong to two different people, and
   * neither is "you" in a comparison. Ordered by the row order, which is fixed
   * for a given pair of files.
   */
  function identities(rows, ma, mb) {
    var ra = ma.run.source.roster, rb = mb.run.source.roster;
    var ids = {}, used = {};
    rows.forEach(function (r, i) {
      var id = { color: '', jobA: '', jobB: '', label: r.key, members: '' };
      if (cmp.by === 'job') {
        var j = r.key === K.UNKNOWN_JOB ? '' : FFXITheme.job(r.key);
        id.color = j || FFXITheme.series(i);
        var names = [];
        [ma, mb].forEach(function (m) {
          var mm = m.members[r.key];
          if (mm) mm.names.forEach(function (n) { if (names.indexOf(n) < 0) names.push(n); });
        });
        id.members = DPS.app.anon ? names.length + ' character' + (names.length === 1 ? '' : 's')
                                  : names.join(', ');
      } else {
        id.jobA = r.a ? ra.jobLabel(r.key) : '';
        id.jobB = r.b ? rb.jobLabel(r.key) : '';
        var jb = r.b ? rb.jobOf(r.key) : null, ja = r.a ? ra.jobOf(r.key) : null;
        var jm = (jb && jb.main !== 'NON' && jb.main) || (ja && ja.main !== 'NON' && ja.main) || '';
        id.color = (jm && FFXITheme.job(jm)) || FFXITheme.series(i);
        if (DPS.app.anon) {
          var lab = id.jobB || id.jobA || 'Unknown job';
          var k = (used[lab] = (used[lab] || 0) + 1);
          id.label = k > 1 ? lab + ' ' + k : lab;
          id.hidden = true;
        }
      }
      ids[r.key] = id;
    });
    return ids;
  }

  // ------------------------------------------------------------------ render

  function render() {
    if (cmp.view !== 'compare') return;
    applyChains();
    renderSlot('a');
    renderSlot('b');
    $('cmpSwap').disabled = !(cmp.a || cmp.b);

    var ready = cmp.a && cmp.b;
    $('cmpBody').hidden = !ready;
    if (!ready) { cmp.last = null; return; }

    var opts = { skillchains: chainsOn(), by: cmp.by };
    var ma = K.measure(cmp.a.run, opts);
    var mb = K.measure(cmp.b.run, opts);
    var d = K.diff(ma, mb);
    // One identity pass over damage rows AND healers, so a healer who also
    // dealt damage carries the same colour and the same hidden-name label in
    // both tables.
    var seenKey = {};
    d.rows.forEach(function (r) { seenKey[r.key] = true; });
    var ids = identities(d.rows.concat(d.heals.filter(function (r) { return !seenKey[r.key]; })), ma, mb);
    cmp.last = { ma: ma, mb: mb, d: d };

    // Only the mode on screen is drawn; the other's cards are hidden by the
    // stylesheet and a canvas laid out hidden would measure zero anyway.
    if (cmp.mode === 'healing') {
      renderHealTiles(ma, mb);
      renderPace(ma, mb);
      renderHealing(d.heals, ids, ma, mb);
      renderTotals('cmpHealSpells', 'Heal', d.healSpells, ma, mb);
      renderTotals('cmpHealTargets', 'Target', d.healTargets, ma, mb, nameFor(ids));
    } else {
      renderTiles(ma, mb);
      renderPace(ma, mb);
      renderRows(d.rows, ids);
      renderKinds(d.kinds, ma, mb);
      renderTargets(d.targets);
    }
  }

  /* A target name as drawn: hidden behind its job like any other character's
     when Hide names is on and the target is someone in the table. */
  function nameFor(ids) {
    return function (n) { return ids[n] ? ids[n].label : n; };
  }

  // ---- the two slots

  function renderSlot(slot) {
    var host = $(slot === 'a' ? 'cmpSlotA' : 'cmpSlotB');
    var s = cmp[slot];
    var tag = '<span class="cmp-tag r' + slot + '">' + slot.toUpperCase() + '</span>';
    var role = slot === 'a' ? 'Baseline' : 'Compared';
    var note = cmp.note[slot]
      ? '<p class="cmp-note bad">' + esc(cmp.note[slot]) + '</p>' : '';
    var cur = canUseCurrent();
    var curBtn = '<button type="button" class="ghost tiny" data-act="current"' +
      (cur ? ' title="Snapshot the parse on the meter right now into this slot"'
           : ' disabled title="Nothing on the meter yet -- Start a session, or Import a parse"') +
      '>Use current</button>';

    host.classList.toggle('is-empty', !s);
    if (!s) {
      host.innerHTML =
        '<div class="cmp-slot-head">' + tag + '<span class="cmp-role">' + role + '</span></div>' +
        '<div class="cmp-drop">' +
          '<p>Drop an exported parse here</p>' +
          '<div class="cmp-slot-actions">' +
            '<button type="button" class="ghost tiny" data-act="open">Open&hellip;</button>' + curBtn +
          '</div>' +
        '</div>' + note;
      return;
    }

    var r = s.run, sn = r.session, src = r.source;
    var secs = S.sessionElapsed(sn, sn.pausedAt) / 1000;
    var start = new Date(sn.startedAt);
    var party = Object.keys(src.roster.kinds).filter(function (n) {
      return src.roster.kinds[n] === 'player';
    }).length;
    host.innerHTML =
      '<div class="cmp-slot-head">' + tag + '<span class="cmp-role">' + role + '</span>' +
        '<div class="cmp-slot-actions">' +
          '<button type="button" class="ghost tiny" data-act="open">Replace&hellip;</button>' + curBtn +
          '<button type="button" class="ghost tiny" data-act="clear">Clear</button>' +
        '</div></div>' +
      '<h3 class="cmp-name" title="' + esc(r.file || s.name) + '">' + esc(s.name) + '</h3>' +
      '<dl class="cmp-meta">' +
        '<div><dt>Started</dt><dd>' + start.toLocaleDateString('en-CA') + ' ' + S.fmtClock(sn.startedAt) + '</dd></div>' +
        '<div><dt>Length</dt><dd>' + S.fmtStopwatch(secs * 1000) + '</dd></div>' +
        '<div><dt>Party</dt><dd>' + party + ' characters</dd></div>' +
      '</dl>' +
      (s.skipped ? '<p class="cmp-note">' + S.fmtInt(s.skipped) + ' unreadable record' +
                   (s.skipped === 1 ? '' : 's') + ' skipped</p>' : '') + note;
  }

  // ---- tiles

  function renderTiles(ma, mb) {
    var tiles = [
      { label: 'Total damage', a: ma.total, b: mb.total, fmt: S.fmtInt, kind: 'pct', good: 1, hero: true },
      { label: 'Length', a: ma.duration, b: mb.duration,
        fmt: function (s) { return S.fmtStopwatch(s * 1000); }, kind: 'time', good: 0 },
      { label: 'Party DPS', a: ma.dps, b: mb.dps, fmt: fmtDps, kind: 'pct', good: 1 },
      { label: 'Accuracy', a: ma.accuracy, b: mb.accuracy, fmt: fmtPct, kind: 'pt', good: 1,
        tip: 'Party melee and ranged swings that connected, out of all attempted -- the character table\'s Accuracy, for everyone' },
      { label: 'WS damage', a: ma.wsTotal, b: mb.wsTotal, fmt: fmtWhole, kind: 'pct', good: 1,
        sub: function (m) { return S.fmtInt(m.wsCount) + ' WS · avg ' + fmtWhole(m.wsAvg); } },
      { label: 'Skillchain damage', a: ma.scTotal, b: mb.scTotal, fmt: fmtWhole, kind: 'pct', good: 1,
        sub: function (m) { return chainsOn() ? S.fmtInt(m.scCount) + ' chains' : 'switched off'; } }
    ];
    drawTiles(tiles, ma, mb);
  }

  function drawTiles(tiles, ma, mb) {
    $('cmpTiles').innerHTML = tiles.map(function (t) {
      function line(m, v, r) {
        return '<div class="cmp-tv"><span class="cmp-tag r' + r + '">' + r.toUpperCase() + '</span>' +
               '<span class="cmp-num">' + (v == null ? '—' : t.fmt(v)) + '</span>' +
               (t.sub ? '<span class="cmp-tsub">' + t.sub(m) + '</span>' : '') + '</div>';
      }
      return '<div class="tile' + (t.hero ? ' hero' : '') + '"' + (t.tip ? ' title="' + esc(t.tip) + '"' : '') + '>' +
        '<div class="tile-label">' + t.label + '</div>' +
        line(ma, t.a, 'a') + line(mb, t.b, 'b') +
        '<div class="cmp-tdelta">' + deltaCell(t.a, t.b, t.kind, t.fmt, t.good) + '</div></div>';
    }).join('');
  }

  /* Healing mode's tiles. A side whose parse has no heal lines at all (made
     before addon 0.3.0) is unmeasured, not zero, so it prints a dash. */
  function renderHealTiles(ma, mb) {
    function v(m, k) { return m.hasHeals ? m.heal[k] : null; }
    function hps(m) { return m.hasHeals && m.duration > 0 ? m.heal.total / m.duration : null; }
    var tiles = [
      { label: 'Total healing', a: v(ma, 'total'), b: v(mb, 'total'), fmt: S.fmtInt, kind: 'pct', good: 1, hero: true,
        tip: 'Every cure, waltz and healing ability. Pet heals are not included' },
      { label: 'Length', a: ma.duration, b: mb.duration,
        fmt: function (s) { return S.fmtStopwatch(s * 1000); }, kind: 'time', good: 0 },
      { label: 'Party HPS', a: hps(ma), b: hps(mb), fmt: fmtDps, kind: 'pct', good: 1 },
      { label: 'Casts', a: v(ma, 'casts'), b: v(mb, 'casts'), fmt: S.fmtInt, kind: 'pct', good: 0,
        tip: 'One per cast, however many people it reached' },
      { label: 'Avg / cast', a: v(ma, 'avg'), b: v(mb, 'avg'), fmt: fmtWhole, kind: 'pct', good: 1 },
      { label: 'Biggest heal', a: v(ma, 'max'), b: v(mb, 'max'), fmt: S.fmtInt, kind: 'pct', good: 1,
        tip: 'The most one target was healed for by one cast. Pet heals not included' },
    ];
    drawTiles(tiles, ma, mb);
  }

  // ---- cumulative chart (the "pace" lines: both runs on one clock)

  function renderPace(ma, mb) {
    var heal = cmp.mode === 'healing';
    $('cmpPaceTitle').textContent = heal ? 'Cumulative healing' : 'Cumulative damage';
    $('cmpPaceSub').textContent = heal
      ? 'Party healing since each run’s first hit, on one clock. Pet heals not included. Hover to read both runs at the same moment.'
      : 'Party damage since each run’s first hit, on one clock. Hover to read both runs at the same moment.';
    var p = K.pace(ma, mb, { field: heal ? 'healEvents' : 'events' });
    // An unmeasured side draws no line rather than a flat one at zero.
    if (heal) {
      [['a', ma], ['b', mb]].forEach(function (x) {
        if (!x[1].hasHeals) for (var i = 0; i < p[x[0]].length; i++) p[x[0]][i] = NaN;
      });
    }
    var ca = FFXITheme.series(0), cb = FFXITheme.series(1);
    var model = {
      times: p.times,
      series: [
        { name: 'A · ' + cmp.a.name, values: p.a, color: ca },
        { name: 'B · ' + cmp.b.name, values: p.b, color: cb }
      ]
    };
    C.line($('cmpPace'), model, { empty: 'Nothing to compare' });

    $('cmpPaceLegend').innerHTML = model.series.map(function (s) {
      return '<span class="legend-item"><i style="background:' + s.color + '"></i>' + esc(s.name) + '</span>';
    }).join('');

    // The chart's table twin, sampled to ~16 rows like the meter's.
    var n = p.times.length, stride = Math.max(1, Math.ceil(n / 16)), idx = [];
    for (var i = 0; i < n; i += stride) idx.push(i);
    if (idx[idx.length - 1] !== n - 1) idx.push(n - 1);
    function v(x) { return isFinite(x) ? S.fmtInt(x) : '—'; }
    $('cmpPaceTable').innerHTML = '<thead><tr><th>Time</th><th>A</th><th>B</th><th>B − A</th></tr></thead><tbody>' +
      idx.map(function (k) {
        var a = p.a[k], b = p.b[k];
        var df = isFinite(a) && isFinite(b) ? (b >= a ? '+' : '−') + S.fmtInt(Math.abs(b - a)) : '—';
        return '<tr><td>' + S.fmtElapsed(p.times[k]) + '</td><td>' + v(a) + '</td><td>' + v(b) +
               '</td><td>' + df + '</td></tr>';
      }).join('') + '</tbody>';
  }

  // ---- characters (or jobs)

  var ROW_COLS = 11;
  // In the run, but the party table never reported their job -- usually a
  // member of another party in the alliance. Not borrowed from the other run.
  var UNREPORTED = '<span title="In this run, but their job was never reported">?</span>';

  function renderRows(rows, ids) {
    var job = cmp.by === 'job';
    $('cmpRowsTitle').textContent = job ? 'By job' : 'By character';
    $('cmpRowsSub').textContent = job
      ? 'Every character on a main job, combined. A character whose job was not reported falls under Unknown job. Each figure shows A over B; select a row to compare its actions.'
      : 'Matched by name. Each figure shows A over B; a dash is a character who was not in that run. Select a row to compare its actions.';

    var max = 0;
    rows.forEach(function (r) { max = Math.max(max, r.a ? r.a.total : 0, r.b ? r.b.total : 0); });

    function get(x, k) { return x ? x[k] : null; }

    var html = '<thead><tr>' +
      '<th>' + (job ? 'Job' : 'Character') + '</th>' +
      (job ? '<th class="job">Characters</th>' : '<th class="job">Job</th>') +
      '<th class="cmp-barcol">Damage</th><th>Δ</th>' +
      '<th title="Damage divided by that run\'s session clock">DPS</th><th>Δ</th>' +
      '<th title="Melee and ranged swings that connected, out of all attempted">Accuracy</th><th>Δ</th>' +
      '<th title="Average damage of the weaponskills that dealt damage">WS Avg</th><th>Δ</th>' +
      '<th title="Share of that run\'s party damage">Share</th>' +
      '</tr></thead><tbody>';

    rows.forEach(function (r) {
      var id = ids[r.key], a = r.a, b = r.b;
      var open = !!cmp.open[r.key];
      var jobCell = job
        ? '<span class="cmp-members">' + esc(id.members) + '</span>'
        : (id.hidden ? '<span class="muted">—</span>'
          : ab(a ? (id.jobA ? esc(id.jobA) : UNREPORTED) : '—',
               b ? (id.jobB ? esc(id.jobB) : UNREPORTED) : '—'));
      html += '<tr class="pick' + (open ? ' on' : '') +
        '" data-key="' + esc(r.key) + '" aria-expanded="' + open + '">' +
        '<td><span class="cmp-caret">' + (open ? '▾' : '▸') + '</span>' +
          '<span class="swatch" style="background:' + id.color + '"></span>' + esc(id.label) + '</td>' +
        '<td class="job">' + jobCell + '</td>' +
        '<td class="cmp-barcol">' + bars(get(a, 'total'), get(b, 'total'), max, S.fmtInt) + '</td>' +
        '<td>' + deltaCell(a ? a.total : (b ? 0 : null), b ? b.total : (a ? 0 : null), 'pct', S.fmtInt, 1) + '</td>' +
        '<td>' + ab(fmtDps(get(a, 'dps')), fmtDps(get(b, 'dps'))) + '</td>' +
        '<td>' + deltaCell(get(a, 'dps'), get(b, 'dps'), 'pct', fmtDps, 1) + '</td>' +
        '<td>' + ab(fmtPct(get(a, 'autoAcc')), fmtPct(get(b, 'autoAcc'))) + '</td>' +
        '<td>' + deltaCell(get(a, 'autoAcc'), get(b, 'autoAcc'), 'pt', fmtPct, 1) + '</td>' +
        '<td>' + ab(fmtWhole(get(a, 'wsAvg')), fmtWhole(get(b, 'wsAvg'))) + '</td>' +
        '<td>' + deltaCell(get(a, 'wsAvg'), get(b, 'wsAvg'), 'pct', fmtWhole, 1) + '</td>' +
        '<td>' + ab(fmtPct(get(a, 'share')), fmtPct(get(b, 'share'))) + '</td>' +
        '</tr>';
      if (open) html += '<tr class="cmp-detail"><td colspan="' + ROW_COLS + '">' + actionTable(r) + '</td></tr>';
    });
    $('cmpTable').innerHTML = html + '</tbody>';
  }

  /* One row's actions, paired -- the drill-down, for a gear or job change. */
  function actionTable(r) {
    var acts = K.actions(r);
    if (!acts.length) return '<p class="muted small">No actions.</p>';
    function g(x, k) { return x ? x[k] : null; }
    function acc(x) { return x && x.tries ? x.accuracy : null; }
    return '<table class="data cmp-acts"><thead><tr>' +
      '<th>Action</th><th>Uses</th><th>Accuracy</th><th>Δ</th><th>Avg</th><th>Δ</th>' +
      '<th>Max</th><th>Total</th><th>Δ</th></tr></thead><tbody>' +
      acts.map(function (x) {
        return '<tr><td>' + esc(x.key) + '</td>' +
          '<td>' + ab(fmtWhole(g(x.a, 'swings')), fmtWhole(g(x.b, 'swings'))) + '</td>' +
          '<td>' + ab(fmtPct(acc(x.a)), fmtPct(acc(x.b))) + '</td>' +
          '<td>' + deltaCell(acc(x.a), acc(x.b), 'pt', fmtPct, 1) + '</td>' +
          '<td>' + ab(x.a && x.a.hits ? S.fmtInt(x.a.avg) : '—', x.b && x.b.hits ? S.fmtInt(x.b.avg) : '—') + '</td>' +
          '<td>' + deltaCell(x.a && x.a.hits ? x.a.avg : null, x.b && x.b.hits ? x.b.avg : null, 'pct', S.fmtInt, 1) + '</td>' +
          '<td>' + ab(fmtWhole(g(x.a, 'max')), fmtWhole(g(x.b, 'max'))) + '</td>' +
          '<td>' + ab(fmtWhole(g(x.a, 'total')), fmtWhole(g(x.b, 'total'))) + '</td>' +
          '<td>' + deltaCell(x.a ? x.a.total : 0, x.b ? x.b.total : 0, 'pct', S.fmtInt, 1) + '</td></tr>';
      }).join('') + '</tbody></table>';
  }

  // ---- healing

  /*
   * Healing, A over B, one row per healer (or job). Figures are
   * `stats.healing`'s -- Metrics' Healing column and its casts --
   * and pet heals sit in a column of their own as Metrics keeps them. A party
   * row leads. A parse from before the addon recorded healing says so rather
   * than reading as a run with no healing in it.
   */
  function renderHealing(rows, ids, ma, mb) {
    var t = $('cmpHeal'), empty = $('cmpHealEmpty');
    $('cmpHealTitle').textContent = cmp.by === 'job' ? 'By job' : 'By healer';
    var missing = [];
    if (!ma.hasHeals) missing.push('A');
    if (!mb.hasHeals) missing.push('B');
    var note = missing.length
      ? (missing.length === 2 ? 'Neither parse has' : 'Parse ' + missing[0] + ' has no') +
        ' healing lines — healing is recorded by the VibeXI addon 0.3.0 and later' +
        (missing.length === 2 ? '.' : ', so its side reads as a dash.')
      : '';
    if (!rows.length) {
      t.innerHTML = '';
      empty.hidden = false;
      empty.textContent = note || 'No healing in either run.';
      return;
    }
    empty.hidden = !note;
    empty.textContent = note;

    // A side with no heal lines at all is unmeasured, not zero.
    function side(m, x, k) {
      if (!m.hasHeals) return null;
      return x ? x[k] : 0;
    }
    var max = 0;
    rows.forEach(function (r) {
      max = Math.max(max, r.a ? r.a.total : 0, r.b ? r.b.total : 0);
    });
    var job = cmp.by === 'job';

    function line(cls, label, a, b, key) {
      var open = key != null && !!cmp.healOpen[key];
      return '<tr class="' + cls + (open ? ' on' : '') + '"' +
        (key != null ? ' data-key="' + esc(key) + '" aria-expanded="' + open + '"' : '') + '>' +
        '<td>' + label + '</td>' +
        '<td class="cmp-barcol">' + bars(a.total, b.total, max, S.fmtInt) + '</td>' +
        '<td>' + deltaCell(a.total, b.total, 'pct', S.fmtInt, 1) + '</td>' +
        '<td>' + ab(fmtWhole(a.casts), fmtWhole(b.casts)) + '</td>' +
        '<td>' + ab(fmtWhole(a.avg), fmtWhole(b.avg)) + '</td>' +
        '<td>' + deltaCell(a.avg, b.avg, 'pct', fmtWhole, 1) + '</td>' +
        '<td>' + ab(fmtWhole(a.max), fmtWhole(b.max)) + '</td>' +
        '<td>' + ab(fmtWhole(a.petTotal), fmtWhole(b.petTotal)) + '</td>' +
        '</tr>';
    }
    function pick(m, x) {
      return {
        total: side(m, x, 'total'), casts: side(m, x, 'casts'),
        avg: x && m.hasHeals ? x.avg : null, max: side(m, x, 'max'),
        petTotal: side(m, x, 'petTotal')
      };
    }

    var html = '<thead><tr><th>' + (job ? 'Job' : 'Healer') + '</th>' +
      '<th class="cmp-barcol">Healing</th><th>Δ</th>' +
      '<th title="One per cast, however many people it reached">Casts</th>' +
      '<th title="Healing divided by casts">Avg</th><th>Δ</th>' +
      '<th title="The most one target was healed for by one cast">Biggest</th>' +
      '<th title="Heals by the character&#39;s pet. Not part of Healing">Pet</th>' +
      '</tr></thead><tbody>';
    html += line('group', 'Party', pick(ma, ma.heal), pick(mb, mb.heal), null);

    rows.forEach(function (r) {
      var id = ids[r.key];
      var open = !!cmp.healOpen[r.key];
      html += line('pick',
        '<span class="cmp-caret">' + (open ? '▾' : '▸') + '</span>' +
        '<span class="swatch" style="background:' + id.color + '"></span>' + esc(id.label),
        pick(ma, r.a), pick(mb, r.b), r.key);
      if (open) html += '<tr class="cmp-detail"><td colspan="8">' + healActionTable(r, ma, mb) + '</td></tr>';
    });
    t.innerHTML = html + '</tbody>';
  }

  function healActionTable(r, ma, mb) {
    var acts = K.healActions(r);
    if (!acts.length) return '<p class="muted small">No heals.</p>';
    function g(m, x, k) { return !m.hasHeals ? null : x ? x[k] : 0; }
    return '<table class="data cmp-acts"><thead><tr>' +
      '<th>Heal</th><th>Casts</th><th>Total</th><th>Δ</th><th>Avg</th><th>Δ</th>' +
      '<th>Max</th></tr></thead><tbody>' +
      acts.map(function (x) {
        var avgA = x.a && ma.hasHeals ? x.a.avg : null, avgB = x.b && mb.hasHeals ? x.b.avg : null;
        return '<tr><td>' + esc(x.key) + '</td>' +
          '<td>' + ab(fmtWhole(g(ma, x.a, 'casts')), fmtWhole(g(mb, x.b, 'casts'))) + '</td>' +
          '<td>' + ab(fmtWhole(g(ma, x.a, 'total')), fmtWhole(g(mb, x.b, 'total'))) + '</td>' +
          '<td>' + deltaCell(g(ma, x.a, 'total'), g(mb, x.b, 'total'), 'pct', S.fmtInt, 1) + '</td>' +
          '<td>' + ab(fmtWhole(avgA), fmtWhole(avgB)) + '</td>' +
          '<td>' + deltaCell(avgA, avgB, 'pct', S.fmtInt, 1) + '</td>' +
          '<td>' + ab(fmtWhole(g(ma, x.a, 'max')), fmtWhole(g(mb, x.b, 'max'))) + '</td></tr>';
      }).join('') + '</tbody></table>';
  }

  // ---- damage type and target

  function renderKinds(kinds, ma, mb) {
    var max = 0;
    kinds.forEach(function (k) { max = Math.max(max, k.a, k.b); });
    $('cmpKinds').innerHTML = kinds.length
      ? '<thead><tr><th>Type</th><th class="cmp-barcol">Damage</th><th>Δ</th><th>Share</th></tr></thead><tbody>' +
        kinds.map(function (k) {
          return '<tr><td>' + esc(k.label) + '</td>' +
            '<td class="cmp-barcol">' + bars(k.a, k.b, max, S.fmtInt) + '</td>' +
            '<td>' + deltaCell(k.a, k.b, 'pct', S.fmtInt, 1) + '</td>' +
            '<td>' + ab(fmtPct(ma.total ? k.a / ma.total : null), fmtPct(mb.total ? k.b / mb.total : null)) + '</td></tr>';
        }).join('') + '</tbody>'
      : '';
  }

  /* A two-run totals table -- By heal, and healing By target. A side whose
     parse has no heal lines prints dashes rather than zeroes. */
  function renderTotals(tableId, label, rows, ma, mb, nameFn) {
    var max = 0;
    rows.forEach(function (k) { max = Math.max(max, k.a, k.b); });
    $(tableId).innerHTML = rows.length
      ? '<thead><tr><th>' + label + '</th><th class="cmp-barcol">Healing</th><th>Δ</th></tr></thead><tbody>' +
        rows.map(function (k) {
          var a = ma.hasHeals ? k.a : null, b = mb.hasHeals ? k.b : null;
          return '<tr><td>' + esc((nameFn ? nameFn(k.key) : k.key) || '—') + '</td>' +
            '<td class="cmp-barcol">' + bars(a, b, max, S.fmtInt) + '</td>' +
            '<td>' + deltaCell(a, b, 'pct', S.fmtInt, 1) + '</td></tr>';
        }).join('') + '</tbody>'
      : '';
  }

  function renderTargets(targets) {
    var max = 0;
    targets.forEach(function (k) { max = Math.max(max, k.a, k.b); });
    $('cmpTargets').innerHTML = targets.length
      ? '<thead><tr><th>Target</th><th class="cmp-barcol">Damage</th><th>Δ</th></tr></thead><tbody>' +
        targets.map(function (k) {
          return '<tr><td>' + esc(k.key || '—') + '</td>' +
            '<td class="cmp-barcol">' + bars(k.a, k.b, max, S.fmtInt) + '</td>' +
            '<td>' + deltaCell(k.a, k.b, 'pct', S.fmtInt, 1) + '</td></tr>';
        }).join('') + '</tbody>'
      : '';
  }

  // ------------------------------------------------------------------ events

  $('viewDamage').addEventListener('click', function () { setView('damage'); });
  $('viewHealing').addEventListener('click', function () { setView('healing'); });
  $('cmpModeDamage').addEventListener('click', function () { setMode('damage'); });
  $('cmpModeHealing').addEventListener('click', function () { setMode('healing'); });
  $('viewCompare').addEventListener('click', function () { setView('compare'); });
  $('cmpByActor').addEventListener('click', function () { setBy('actor'); });
  $('cmpByJob').addEventListener('click', function () { setBy('job'); });

  // One setting, two buttons: this clicks the filter bar's, whose handler
  // flips app.chains, stores it and redraws the meter; this view then redraws.
  $('cmpChainBtn').addEventListener('click', function () {
    $('chainBtn').click();
    render();
  });

  // Registered after app.js's own handler, so app.anon has already flipped.
  $('anonBtn').addEventListener('click', render);

  $('cmpSwap').addEventListener('click', function () {
    var t = cmp.a; cmp.a = cmp.b; cmp.b = t;
    t = cmp.note.a; cmp.note.a = cmp.note.b; cmp.note.b = t;
    render();
  });

  ['a', 'b'].forEach(function (slot) {
    var host = $(slot === 'a' ? 'cmpSlotA' : 'cmpSlotB');
    host.addEventListener('click', function (ev) {
      var btn = ev.target.closest('button[data-act]');
      if (!btn || btn.disabled) return;
      var act = btn.dataset.act;
      if (act === 'open') openInto(slot);
      else if (act === 'current') useCurrent(slot);
      else if (act === 'clear') { cmp[slot] = null; cmp.note[slot] = null; cmp.open = {}; cmp.healOpen = {}; render(); }
    });
    host.addEventListener('dragover', function (ev) {
      ev.preventDefault();
      ev.dataTransfer.dropEffect = 'copy';
      host.classList.add('is-over');
    });
    host.addEventListener('dragleave', function (ev) {
      if (!host.contains(ev.relatedTarget)) host.classList.remove('is-over');
    });
    host.addEventListener('drop', function (ev) {
      ev.preventDefault();
      host.classList.remove('is-over');
      var f = ev.dataTransfer.files && ev.dataTransfer.files[0];
      if (f) f.text().then(function (text) { load(slot, f.name, text); });
    });
  });

  $('cmpHeal').addEventListener('click', function (ev) {
    var tr = ev.target.closest('tr.pick');
    if (!tr) return;
    var k = tr.dataset.key;
    if (cmp.healOpen[k]) delete cmp.healOpen[k]; else cmp.healOpen[k] = true;
    render();
  });

  $('cmpTable').addEventListener('click', function (ev) {
    var tr = ev.target.closest('tr.pick');
    if (!tr) return;
    var k = tr.dataset.key;
    if (cmp.open[k]) delete cmp.open[k]; else cmp.open[k] = true;
    render();
  });

  var resizeTimer;
  window.addEventListener('resize', function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(render, 120);
  });

  // ------------------------------------------------------------------- start

  try { if (localStorage.getItem(BY_KEY) === 'job') cmp.by = 'job'; } catch (e) { }
  $('cmpByActor').classList.toggle('on', cmp.by === 'actor');
  $('cmpByJob').classList.toggle('on', cmp.by === 'job');
  $('cmpByActor').setAttribute('aria-pressed', String(cmp.by === 'actor'));
  $('cmpByJob').setAttribute('aria-pressed', String(cmp.by === 'job'));

  try { if (localStorage.getItem(MODE_KEY) === 'healing') cmp.mode = 'healing'; } catch (e) { }
  $('cmpModeDamage').classList.toggle('on', cmp.mode === 'damage');
  $('cmpModeHealing').classList.toggle('on', cmp.mode === 'healing');
  $('cmpModeDamage').setAttribute('aria-pressed', String(cmp.mode === 'damage'));
  $('cmpModeHealing').setAttribute('aria-pressed', String(cmp.mode === 'healing'));
  $('compareView').dataset.mode = cmp.mode;

  var saved = 'damage';
  try { saved = localStorage.getItem(VIEW_KEY) || 'meter'; } catch (e) { }
  setView(saved);

  DPS.compareView = { render: render, load: load, state: cmp, setView: setView, setMode: setMode };
})();
