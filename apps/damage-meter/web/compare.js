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

  var cmp = {
    view: 'meter',
    by: 'actor',          // 'actor' | 'job'
    a: null,              // { run, name, note }
    b: null,
    note: { a: null, b: null },   // the last error per slot
    open: {},             // row key -> true when its actions are expanded
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
    cmp.view = v === 'compare' ? 'compare' : 'meter';
    var on = cmp.view === 'compare';
    document.body.classList.toggle('view-compare', on);
    $('viewMeter').classList.toggle('on', !on);
    $('viewCompare').classList.toggle('on', on);
    $('viewMeter').setAttribute('aria-pressed', String(!on));
    $('viewCompare').setAttribute('aria-pressed', String(on));
    try { localStorage.setItem(VIEW_KEY, cmp.view); } catch (e) { }
    if (on) render(); else DPS.meter.render();
  }

  function setBy(by) {
    cmp.by = by === 'job' ? 'job' : 'actor';
    cmp.open = {};
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
    cmp.open = {};
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
    var ids = identities(d.rows, ma, mb);
    cmp.last = { ma: ma, mb: mb, d: d };

    renderTiles(ma, mb);
    renderPace(ma, mb);
    renderRows(d.rows, ids);
    renderKinds(d.kinds, ma, mb);
    renderTargets(d.targets);
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

  // ---- cumulative damage chart (the "pace" lines: both runs on one clock)

  function renderPace(ma, mb) {
    var p = K.pace(ma, mb);
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

  $('viewMeter').addEventListener('click', function () { setView('meter'); });
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
      else if (act === 'clear') { cmp[slot] = null; cmp.note[slot] = null; cmp.open = {}; render(); }
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

  var saved = 'meter';
  try { saved = localStorage.getItem(VIEW_KEY) || 'meter'; } catch (e) { }
  setView(saved);

  DPS.compareView = { render: render, load: load, state: cmp, setView: setView };
})();
