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

  // The charts' empty state. "No damage in the selected range" named a range
  // control that no longer exists, and the overwhelmingly common reason a chart
  // is empty now is that nobody has pressed Start.
  function emptyText() {
    return armed() ? 'Armed — the clock starts on the first hit, or Cancel to call it off'
         : !started() ? 'Press Start to begin measuring'
         : paused() ? 'Paused — nothing is being counted'
         : 'No damage yet';
  }

  var POLL_MS = 250;
  var TICK_MS = 100;          // cumulative chart's own frame, between polls

  var app = {
    file: null,
    offset: 0,
    source: P.create(null),
    lines: 0,
    // THE MEASUREMENT WINDOW. Start arms it; the first counted event sets its
    // zero, and everything drawn is measured from there. `idleSession()` is the
    // state before any of that, in which the meter deliberately shows nothing
    // at all. See `startSession` and `stats.js`'s session block.
    session: S.idleSession(),
    chains: 'on',           // 'on' credits skillchain damage, 'off' drops it
    actorsOff: {},          // name -> true when excluded; persisted
    drill: null,            // { actor, action }
    lineModel: null,        // last cumulative model, animated by tickLine
    anon: false,            // draw every other character as their job; persisted
    alias: {},              // name -> 'SAM/WAR', the label anon mode draws instead
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
    assignAliases();
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

  /*
   * The label anon mode draws instead of a character's name: their job.
   *
   * The point is a screenshot or a stream. Only the NAME goes -- nothing is
   * filtered, no total moves, and the owner keeps theirs, because a meter that
   * cannot tell you which line is yours is not a meter. `roster.owner` comes
   * off the filename, so it is known before a single line is read.
   *
   * Two characters can share a job exactly as they can share its colour, so the
   * second "SAM/WAR" becomes "SAM/WAR 2". ORDERED BY COLOUR SLOT, for the same
   * reason `assignJobVariants` is: slots are handed out once in first-seen order
   * and never reassigned, so the same samurai keeps the same number all session.
   * Numbering by damage or table position would renumber the party under the
   * user every time the lead changed -- and a name that moves is worse than no
   * name at all, since the whole feature is being able to follow someone
   * anonymously. The first of a pair stays unnumbered, matching the colour
   * variants, where the first also takes the base shade.
   *
   * A character with no job on record -- a trust, a pet's owner seen only
   * through their pet, anyone the party table has not reported yet -- falls back
   * to "Unknown job", numbered the same way. That is the same set `colorOf`
   * falls through to the series ramp for.
   *
   * SLOTS ARE NOT THE WHOLE PARTY. A member who never deals damage -- the white
   * mage -- is never an actor, so they are never slotted, and they still reach
   * the Diagnostics roster through their job line alone. Leaving them out here
   * would put their name back on screen the moment that panel was opened, so
   * every name the roster calls a player is aliased whether or not it ever
   * swung. Those trail the slotted ones in name order, which is stable except
   * in one corner: two characters on the same job, neither yet slotted, and the
   * one sorting second is the one that acts first -- then the pair swaps
   * numbers on that first swing and never again.
   *
   * A PET KEEPS ITS NAME, deliberately. It is not a character and it is not the
   * player, its damage is already credited to its owner, and its name is part
   * of the action string ("Fluffikins: Big Scissors") rather than a name in its
   * own right. If that ever needs hiding it is the action label that has to
   * change, not this map.
   */
  function assignAliases() {
    var roster = app.source.roster;
    var slotted = Object.keys(app.slots).sort(function (a, b) {
      return app.slots[a] - app.slots[b];
    });
    var rest = Object.keys(roster.kinds).filter(function (n) {
      return roster.kinds[n] === 'player' && !(n in app.slots);
    }).sort();

    var names = slotted.concat(rest);
    var used = {};
    app.alias = {};
    for (var i = 0; i < names.length; i++) {
      if (names[i] === roster.owner) continue;   // the one name that stays
      var label = roster.jobLabel(names[i]) || 'Unknown job';
      var k = (used[label] = (used[label] || 0) + 1);
      app.alias[names[i]] = k > 1 ? label + ' ' + k : label;
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
  var ANON_KEY = 'ffxi_dps_anon';

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

  /* Same shape: the markup ships expanded, so only "closed" is restored. */
  function loadCharRow() {
    try {
      if (localStorage.getItem(CHARROW_KEY) === 'closed') app.chipsOpen = false;
    } catch (e) { }
    applyCharRow();
  }

  /* Off is the default, so only "on" has to be restored. It persists because a
     stream stays a stream: switching it back on every session is the one way to
     forget once and put the party's names on air. */
  function loadAnon() {
    try {
      if (localStorage.getItem(ANON_KEY) === 'on') app.anon = true;
    } catch (e) { }
    applyAnon();
  }

  function applyAnon() {
    var b = $('anonBtn');
    b.setAttribute('aria-pressed', String(app.anon));
    b.title = app.anon
      ? 'Names hidden: every character but you is drawn as their job. Click to show them again.'
      : "Replace every other character's name with their job, for a screenshot or a stream";
  }

  function applyCharRow() {
    $('charRow').classList.toggle('collapsed', !app.chipsOpen);
    var b = $('charToggle');
    b.setAttribute('aria-expanded', String(app.chipsOpen));
    b.textContent = app.chipsOpen ? 'Hide' : 'Show';
    b.title = (app.chipsOpen ? 'Collapse' : 'Expand') + ' the character list';
  }

  // ----------------------------------------------------------------- session

  /*
   * Start: ARM the session. The clock does not begin here -- the first event
   * that would actually be counted sets the zero, and its own timestamp is that
   * zero (`stats.firstCounted`, latched in `render`).
   *
   * This is what makes the button safe to press early. Pressed on the pull, on
   * the run in, or while the last buff goes up, the measured window is the same
   * one either way: it opens on the first swing. Pressing at the instant of
   * that swing would be the only way to get the same answer from a press-is-zero
   * stopwatch, and nobody can, so every DPS figure would carry the reaction
   * time as an error in its denominator.
   *
   * Also the Reset button, which no longer exists separately -- pressing Start
   * during a session is how a second pull is measured, and a control that both
   * begins and re-begins the measurement is one control, not two with a subtle
   * distinction between them.
   *
   * The events collected so far are dropped, exactly as Reset dropped them. The
   * session window would have hidden them anyway, so this buys nothing on
   * screen; what it buys is that the poll path stays O(events since Start)
   * rather than growing without bound across a grinding session. The read
   * offset is deliberately left alone -- this is not a re-read, and reloading
   * the page is still what replays the file from the top.
   *
   * Deliberately kept, as they were across a reset:
   *   - colour slots, so a character does not change hue mid-session
   *   - the roster, including any manual override, so monsters stay monsters
   *   - the character exclusions and the skillchain switch, which are user
   *     intent rather than collected data
   */
  function startSession() {
    app.source.reset();
    app.scanned = 0;          // `seen` is kept; only the scan cursor rewinds
    app.drill = null;
    app.session = S.arm();
    $('drillCard').hidden = true;
    app.rendered = true;
    applySession();
    setStatus(app.file || 'waiting for the addon');
    render();
  }

  /*
   * Pause: stop counting damage AND stop the clock.
   *
   * Both halves matter and they are the same decision. Damage landing while
   * paused is dropped (`stats.filter` refuses any event inside a pause span),
   * and the time it landed in is subtracted from the elapsed clock, so the
   * denominator does not run on either. Freeze one without the other and the
   * meter lies in one direction or the other -- a running clock over a frozen
   * numerator reads as a wipe, and a frozen clock over a running numerator
   * reads as a parse.
   *
   * Polling continues while paused, deliberately. The addon keeps writing and
   * the file keeps growing whatever this page does, so stopping the reader only
   * moves the same bytes to a burst on resume; the events are read, kept for
   * Diagnostics, and dropped in the view by their own timestamps. That is the
   * same shape as the monster filter -- collect everything, decide in the view.
   */
  function togglePause() {
    // Nothing to hold while armed: the clock has not started, so there is no
    // running total for a pause to freeze and no elapsed time to subtract.
    if (app.session.startedAt == null) return;
    if (S.sessionRunning(app.session)) S.sessionPause(app.session);
    else S.sessionResume(app.session);
    applySession();
    setStatus(app.file || 'waiting for the addon');
    render();
  }

  /*
   * Cancel an armed session: disarm, and go back to counting nothing.
   *
   * Arming is a statement about a pull that has not happened yet, and a
   * statement made early is one that can turn out to be wrong -- the puller
   * pulls something else, the party resets, someone disconnects. Without this
   * the only way out of an armed meter was to let it catch a hit and then throw
   * that session away, which means the escape hatch was "measure the thing you
   * did not want to measure, then discard it".
   *
   * Idle is exactly the state a new event file lands in, so this is that same
   * transition and nothing more. The events read while armed are deliberately
   * NOT dropped: they are invisible either way (an idle session counts nothing)
   * and the next Start clears them along with everything else. Colour slots are
   * kept for the same reason they survive a Start -- a character changing hue
   * because somebody cancelled a countdown is worse than a stale entry.
   *
   * Only reachable while armed. Once the clock is running the same button is
   * Pause, and a session with damage in it is ended by Start, not by Cancel:
   * "cancel" would then mean discarding a measurement, which is a different and
   * much more destructive act than calling one off before it began.
   */
  function cancelSession() {
    if (!armed()) return;
    app.session = S.idleSession();
    app.drill = null;
    $('drillCard').hidden = true;
    applySession();
    setStatus(app.file || 'waiting for the addon');
    render();
  }

  /*
   * The second session button does two jobs, because it has exactly one to do
   * in each state and they never overlap: while armed there is no clock to hold
   * but there is an arming to call off, and once the clock runs there is no
   * arming left to cancel. One slot, one meaning at a time.
   */
  function secondary() {
    if (armed()) cancelSession(); else togglePause();
  }

  function paused() { return app.session.pausedAt != null; }
  function started() { return app.session.startedAt != null; }
  function armed() { return S.sessionArmed(app.session); }

  /* Every control that reflects session state, in one place: the two buttons
     and the status dot are written from the session and never from each other. */
  /*
   * The session's state as plain data: labels, titles and the class each button
   * wears. Derived once and rendered twice -- into this page's bar, and into
   * every floating window's bar via `DPS.popout.session`.
   *
   * The strings live HERE and not in popout.js, which is loaded first and knows
   * nothing about sessions. That module is handed finished text and applies it,
   * exactly as it is handed a theme name; the alternative is two copies of this
   * copy drifting apart, in two windows, side by side on the same screen.
   */
  function sessionView() {
    var isArmed = armed(), isStarted = started(), isPaused = paused();
    return {
      startText: (isArmed || isStarted) ? 'Restart' : 'Start',
      startTitle: isArmed
        ? 'Armed — the clock starts on the first counted hit. Press again to re-arm.'
        : isStarted
          ? 'Zero the clock and measure a fresh pull from here'
          : 'Arm the meter. The clock starts on the first hit, so pressing early costs nothing.',
      // A state, not a hover: "Restart" alone cannot say whether the clock is
      // running or still waiting, and that is the one thing being watched.
      startClass: isArmed ? 'is-armed' : isStarted ? 'is-running' : 'is-idle',

      pauseText: isArmed ? 'Cancel' : isPaused ? 'Resume' : 'Pause',
      pauseTitle: isArmed
        ? 'Cancel — disarm the meter. Nothing is counted until Start is pressed again.'
        : !isStarted
          ? 'Press Start first'
          : isPaused
            ? 'Resume counting and restart the clock'
            : 'Stop counting damage and stop the clock. Damage dealt while paused is not counted and the paused time is not divided into DPS.',
      pauseClass: isArmed ? 'is-cancel' : !isStarted ? '' : isPaused ? 'is-held' : 'is-live',
      pauseDisabled: !isArmed && !isStarted,
      pausePressed: isPaused,
      // Cancel is not a toggle, and `aria-pressed` on a plain action button
      // announces a two-state control that is not there. The attribute comes
      // off entirely rather than being reported false.
      pauseToggle: !isArmed,

      dot: isArmed ? 'armed' : !isStarted ? '' : isPaused ? 'stale' : 'live'
    };
  }

  /* Write that state onto one pair of buttons. Shared with the pop-out windows
     through `DPS.popout.session`, which calls the same shape on its own nodes. */
  function paintSession(v, start, pause) {
    if (start) {
      start.textContent = v.startText;
      start.title = v.startTitle;
      start.className = 'session-start ' + v.startClass;
    }
    if (pause) {
      pause.textContent = v.pauseText;
      pause.title = v.pauseTitle;
      pause.disabled = v.pauseDisabled;
      if (v.pauseToggle) pause.setAttribute('aria-pressed', String(v.pausePressed));
      else pause.removeAttribute('aria-pressed');
      pause.className = 'session-pause ' + v.pauseClass;
    }
  }

  function applySession() {
    var v = sessionView();
    paintSession(v, $('startBtn'), $('pauseBtn'));
    $('liveDot').className = 'dot ' + v.dot;
    // Every floating window carries the same pair, in the same state.
    DPS.popout.session(v);
  }

  /*
   * Give an armed session its zero, if anything has qualified yet.
   *
   * Runs at the top of render(), which is exactly when it can matter: render is
   * what a new poll's events trigger, so the latch is tested against every
   * event on the same pass that would have drawn it. A filter change also lands
   * here, which is deliberate -- flipping a name back to "player" in
   * Diagnostics can make an already-read event the one that qualifies, and it
   * should then be the zero, because it is now the first thing being counted.
   *
   * `sessionStart` latches once. After that this is a no-op, so no later change
   * can re-date a running session and re-scale every number in it.
   */
  function latchStart(all, roster, enabled) {
    if (!armed()) return;
    var t = S.firstCounted(all, app.session, {
      roster: roster, actors: enabled, skillchains: app.chains === 'on'
    });
    if (t == null) return;
    S.sessionStart(app.session, t);
    applySession();
    setStatus(app.file || 'waiting for the addon');
  }

  /*
   * The colour a character is drawn in, everywhere in the app.
   *
   * Via FFXITheme, not a local getComputedStyle: it caches per theme, and the
   * palette stays in the one stylesheet that drives both apps and both modes.
   *
   * JOB COLOUR, ALWAYS -- Metrics' own mapping. The party already reads these
   * at a glance in game (the warrior is red, the samurai orange), so the meter
   * agrees with the parser sitting next to it instead of inventing a second
   * mapping for the same six people. There is no alternative palette and no
   * control to pick one: one mapping means a character's hue means the same
   * thing in every screenshot, every session and every panel.
   *
   * What that costs, said out loud because it looks like a bug: the job palette
   * is NOT colourblind-separable -- WAR, NIN, RDM and SAM are four reds -- and
   * two characters on the same job differ only by a lightness step. It is
   * acceptable only because the meter never identifies anyone by colour alone:
   * legend, table rows, chips and hover all carry the name and the job in text.
   *
   * The --series ramp survives as the FALLBACK, not as a mode. Four jobs have
   * no Metrics colour (DNC, SCH, GEO, RUN), and a trust, a pet's owner seen
   * only through their pet, or anyone the party table has not reported yet has
   * no job at all. Those get a slot off the ramp rather than a made-up hue.
   */
  function colorOf(name) {
    var j = app.source.roster.jobOf(name);
    var base = j && j.main ? FFXITheme.job(j.main) : '';
    if (base) return FFXITheme.step(base, app.jobVariant[name] || 0);
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

  /*
   * THE NAME AS DRAWN -- every place in this file that puts a character in front
   * of the user goes through here, and nothing else does.
   *
   * Names stay real everywhere they are a key: the exclusion list, the drill-down
   * target, a chip's `data-actor`, the roster override, `colorOf`, the aggregate
   * itself. Only the text does. That is what lets the toggle be nothing but a
   * re-render -- no filter is touched and no total can move.
   */
  function nameOf(name) {
    return (app.anon && app.alias[name]) || name;
  }

  /*
   * True when this character's name has BEEN replaced by their job -- which is
   * the one case where printing the job a second time beside it is noise. Every
   * job badge and both Job columns are suppressed on it.
   */
  function aliased(name) {
    return !!(app.anon && app.alias[name]);
  }

  /* The job badge that rides beside a name, or '' when the name is the job. */
  function jobBadge(name, cls) {
    var j = aliased(name) ? '' : jobOf(name);
    return j ? '<b class="' + cls + '">' + esc(j) + '</b>' : '';
  }

  // ------------------------------------------------------------------- fetch

  function poll() {
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
          // A new file is a new character or a new day; a clock still running
          // from the old one would measure a session that is not this one.
          app.session = S.idleSession();
          applySession();
          $('drillCard').hidden = true;
        }
        app.offset = d.nextOffset;

        var lines = d.lines || [];
        for (var i = 0; i < lines.length; i++) app.source.feed(lines[i]);
        app.lines += lines.length;

        setStatus(app.file);
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

  /*
   * `cls` overrides the dot for a connection problem; without one the dot is
   * the SESSION's, because that is what the user is being told about most of
   * the time -- not started, running, or held.
   */
  function setStatus(text, cls) {
    $('srcFile').textContent = text;
    if (cls) $('liveDot').className = 'dot ' + cls;
    else applySession();
    var ev = app.source.events.length;
    $('srcCount').textContent = S.fmtInt(ev) + ' event' + (ev === 1 ? '' : 's') +
      ' · ' + S.fmtInt(app.lines) + ' lines' +
      // A time of day, and the one place the app still prints one: this says
      // when the pull began in the world, not where anything sits on the axis.
      (started() ? ' · started ' + S.fmtClock(app.session.startedAt)
       : armed() ? ' · armed, waiting for the first hit'
       : ' · not started');
  }

  // ------------------------------------------------------------------ render

  function render() {
    var all = app.source.events;
    var roster = app.source.roster;
    assignSlots();

    var enabled = {};
    Object.keys(app.actorsOff).forEach(function (n) { enabled[n] = false; });

    // Before anything is filtered: an armed session may have just acquired its
    // zero, and the same pass has to draw the event that gave it one.
    latchStart(all, roster, enabled);

    // The one call that knows about wall-clock time. Passing the session drops
    // everything outside it and puts every survivor on the elapsed clock;
    // passing the roster drops the monsters' own damage, because this meter
    // counts what the party dealt and nothing else.
    var scoped = S.filter(all, {
      session: app.session, roster: roster,
      skillchains: app.chains === 'on'
    });

    // No session here: `scoped` is already on the elapsed clock and converting
    // a second time would measure it from itself.
    var shown = S.filter(scoped, { actors: enabled });

    // The elapsed clock, in seconds, is the denominator under every DPS in the
    // app -- not each character's own active window. Read once per render so
    // the party figure and the character column cannot disagree by a frame.
    var secs = S.sessionElapsed(app.session) / 1000;
    var agg = S.aggregate(shown, { duration: secs });

    renderChips(S.aggregate(scoped, { duration: secs }).actors);
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
       be rebuilt when it does. The signature is over the name AS DRAWN, so
       hiding the names rebuilds the chips the same way a job line does. */
    var sig = actors.map(function (a) {
      return nameOf(a.name) + '\u0001' + jobBadge(a.name, 'chip-job');
    }).join('\0');
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
      : off.length + ' excluded: ' + off.map(nameOf).join(', ');

    function state(chip, name) {
      var inc = !app.actorsOff[name];
      var full = app.source.roster.jobTitle(name);
      chip.setAttribute('aria-pressed', String(inc));
      chip.title = (inc ? 'Exclude ' : 'Include ') + nameOf(name) + (full ? ' · ' + full : '');
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

    // `data-actor` stays the real name: it is the key the exclusion list, the
    // colour and the aggregate are all held under, and only the text is hidden.
    host.innerHTML = actors.map(function (a) {
      return '<button type="button" class="chip" data-actor="' + esc(a.name) + '">' +
             '<i style="background:' + colorOf(a.name) + '"></i>' + esc(nameOf(a.name)) +
             jobBadge(a.name, 'chip-job') + '</button>';
    }).join('');
    [].forEach.call(host.querySelectorAll('.chip'), function (chip) {
      state(chip, chip.dataset.actor);
    });
  }

  // ---- hero + stat tiles

  function renderTiles(agg) {
    app.tileAgg = agg;        // what tickClock re-divides between renders
    $('tTotal').textContent = S.fmtInt(agg.total);
    tickClock();

    $('tDpsSub').textContent = agg.actors.length
      ? agg.actors.length + ' character' + (agg.actors.length === 1 ? '' : 's')
      : '—';

    // `aggregate` returns actors sorted by total damage, so the leader is [0].
    // The dot carries the same hue the character has on every chart.
    var top = agg.actors[0];
    // Not beside a name that IS the job: the tile would read "SAM/WAR · SAM/WAR".
    var topJob = top && !aliased(top.name) ? jobOf(top.name) : '';
    $('tTopDot').style.background = top ? colorOf(top.name) : 'transparent';
    $('tTopName').textContent = top ? nameOf(top.name) : '—';
    $('tTopName').title = top ? (app.source.roster.jobTitle(top.name) || nameOf(top.name)) : '';
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
    $('tBigSub').textContent = best ? nameOf(best.who) + ' · ' + best.what : '—';
  }

  // ---- cumulative line chart

  function renderLine(events, agg) {
    // One line per character, each in that character's own colour.
    var names = agg.actors.map(function (a) { return a.name; });

    // `from: 0` is the Start press: the axis covers the same span the DPS
    // beside it is divided by, including any run-up before the first swing.
    var model = S.cumulative(events, names, { from: 0, now: liveEdge() });

    // `real` is kept because the name is still the key -- the legend looks the
    // job up by it -- while `name` is what chart.js prints in its hover card.
    model.series.forEach(function (s) {
      s.color = colorOf(s.name);
      s.real = s.name;
      s.name = nameOf(s.name);
    });
    // Largest total last so the leading line is drawn on top of the pack.
    model.series.sort(function (a, b) {
      return (a.values[a.values.length - 1] || 0) - (b.values[b.values.length - 1] || 0);
    });

    // Kept for tickLine(), which walks this model's live edge forward between
    // renders. It is the same object the chart was drawn from, colours, sort
    // order and all, so an animated frame and a rendered one cannot disagree.
    app.lineModel = model;

    C.line($('lineChart'), model, { empty: emptyText() });
    renderLegend(model.series);
    renderLineTable(model);
  }

  /*
   * The right-hand edge of the chart: the session clock, or null to end at the
   * last event.
   *
   * ALWAYS THE CLOCK ONCE A SESSION HAS STARTED -- running or paused. There is
   * no idle cutoff either; the edge used to stop advancing after 90 s of
   * silence, on the grounds that the fight was over and a flat line out to the
   * present said nothing. Under a session clock it says the thing that matters
   * most: the denominator is still growing, so a flat line is a falling DPS.
   *
   * PAUSED IS NOT AN EXCEPTION, and treating it as one was a bug. Returning
   * null here ended the grid at the newest EVENT, so pressing Pause snapped the
   * chart back to the last swing and threw away the quiet stretch between that
   * swing and the button -- while `sessionElapsed`, frozen at the press, kept
   * counting exactly that stretch. The axis and the DPS beside it were then
   * describing different windows, which is the one thing this chart may never
   * do. `sessionElapsed` is already frozen while paused, so handing it over
   * unconditionally holds the edge still at the moment of the press, which is
   * both correct and what "paused" should look like.
   *
   * Null only before Start: with no zero there is no axis to put an edge on.
   */
  function liveEdge() {
    if (!started()) return null;
    return S.sessionElapsed(app.session);
  }

  /*
   * The cumulative chart's own frame, independent of the poll.
   *
   * Events arrive in clumps -- a weaponskill, then four seconds of nothing --
   * and a chart redrawn only on arrival stands still and then lurches. Moving
   * the live edge to now on a fixed tick makes the same data read as a stream:
   * the lines extend flat through the quiet and the x axis slides continuously.
   *
   * Only the canvas is touched. The legend and the chart's table twin are DOM
   * built by render(), and rebuilding those ten times a second would reset
   * scroll position and drop text selection for no new information -- the same
   * reason poll() does not re-render on an idle response.
   */
  /*
   * The elapsed clock and the party DPS, re-read on every frame.
   *
   * A fixed start makes DPS a function of time even when nothing happens: the
   * numerator holds and the denominator grows, so ten seconds of silence lower
   * it. render() cannot express that -- it runs only when a poll brings new
   * data, and an idle poll deliberately skips it so the tables keep their
   * scroll position and their text selection. So the two figures that move on
   * their own are written here instead, and they are text nodes only: no table
   * is rebuilt and no layout is read.
   *
   * The per-character DPS cells move with it, and MUST: they are divided by the
   * same clock, so a party figure that decayed while the column beside it stood
   * still would simply not add up -- 100.9 in the tile over a column summing to
   * 141.2, both correct, at two different instants. They are rewritten by
   * `data-dps` rather than by rebuilding the table, so scroll position, text
   * selection and the drill-down's selected row all survive.
   */
  function tickClock() {
    var agg = app.tileAgg;
    var ms = S.sessionElapsed(app.session);
    var secs = ms / 1000;

    var el = $('tTotalSub');
    if (el) {
      el.textContent = armed() ? 'armed — starts on the first hit'
        : !started() ? 'not started — press Start'
        : !agg || !agg.actors.length ? S.fmtElapsed(ms) + ' elapsed · no damage yet'
        : S.fmtElapsed(ms) + ' elapsed' + (paused() ? ' · held' : '');
    }

    var d = $('tDps');
    if (d) d.textContent = S.fmtNum(agg && secs > 0 ? agg.total / secs : 0, 1);

    var table = $('actorTable');
    if (!table || !agg) return;
    var totals = {};
    for (var i = 0; i < agg.actors.length; i++) totals[agg.actors[i].name] = agg.actors[i].total;
    [].forEach.call(table.querySelectorAll('[data-dps]'), function (cell) {
      var t = totals[cell.dataset.dps];
      if (t != null) cell.textContent = S.fmtNum(secs > 0 ? t / secs : 0, 1);
    });
  }

  function tickLine() {
    var m = app.lineModel;
    if (!m || !m.live || paused()) return;

    var canvas = $('lineChart');
    if (!canvas) return;
    // ownerDocument, not this one: the card may have been popped out, and a
    // hidden window's canvas is worth no frames.
    var doc = canvas.ownerDocument;
    if (doc.hidden || !canvas.clientWidth) return;
    // Never repaint under the pointer. C.line draws its crosshair over a
    // snapshot taken at draw time, so a frame landing mid-hover would wipe the
    // crosshair and leave the tooltip pointing at nothing.
    if (canvas.matches && canvas.matches(':hover')) return;

    // The session clock, not Date.now(): the model's timeline starts at zero.
    m.times[m.times.length - 1] = S.sessionElapsed(app.session);
    C.line(canvas, m, { empty: emptyText() });
  }

  function renderLegend(series) {
    var host = $('lineLegend');
    if (series.length < 2) { host.innerHTML = ''; return; }   // one series: title says it
    // The job goes in the legend as text. It is what makes the job palette safe
    // to default to: two warriors differ by a lightness step in the swatch, and
    // by their name in the very same line.
    host.innerHTML = series.slice().reverse().map(function (s) {
      return '<span class="legend-item"><i style="background:' + s.color + '"></i>' +
             esc(s.name) + jobBadge(s.real, 'legend-job') + '</span>';
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
        return '<tr><td>' + S.fmtElapsed(model.times[k]) + '</td>' +
          cols.map(function (s) { return '<td>' + S.fmtInt(s.values[k]) + '</td>'; }).join('') +
          '</tr>';
      }).join('') + '</tbody>';
  }

  // ---- damage by character

  function renderBars(agg) {
    var rows = agg.actors.map(function (a) {
      // The Job row goes when the bar's own label is the job -- see jobBadge.
      var full = aliased(a.name) ? '' : app.source.roster.jobTitle(a.name);
      return {
        label: nameOf(a.name),
        value: a.total,
        color: colorOf(a.name),
        sub: '<table>' +
             (full ? '<tr><td>Job</td><td>' + esc(full) + '</td></tr>' : '') +
             '<tr><td>Damage</td><td>' + S.fmtInt(a.total) + '</td></tr>' +
             '<tr><td>Share</td><td>' + S.fmtNum(a.share * 100, 1) + '%</td></tr>' +
             // No DPS row. It is the one figure here that decays with the clock
             // and a hover card is built once per render, so it would drift out
             // of step with the same character's live cell in the table
             // directly below -- which is in this card, always on screen, and
             // ticked. Every other row here is a running total that only a new
             // event can change.
             '<tr><td>Avg / action</td><td>' + S.fmtInt(a.avg) + '</td></tr>' +
             '</table>'
      };
    });
    $('barsWrap').style.height = Math.max(120, rows.length * 34 + 16) + 'px';
    C.bars($('barsChart'), rows, { empty: emptyText() });

    // The Job column is dropped outright while the names are hidden rather than
    // printed twice or blanked to a dash: every character but the owner already
    // has their job in the Character cell, and a dash there would read as "job
    // unknown", which is a different thing entirely. The owner's job is still on
    // their chip, in the legend and on their actions group row.
    var withJob = !app.anon;
    $('actorTable').innerHTML = rows.length
      ? '<thead><tr><th>Character</th>' + (withJob ? '<th class="job">Job</th>' : '') +
        '<th>Damage</th><th>Share</th><th>DPS</th>' +
        '<th>Actions</th><th>Avg</th><th>Best</th><th>Acc</th></tr></thead><tbody>' +
        agg.actors.map(function (a) {
          return '<tr><td><span class="swatch" style="background:' + colorOf(a.name) + '"></span>' +
            esc(nameOf(a.name)) + '</td>' +
            (withJob
              ? '<td class="job" title="' + esc(app.source.roster.jobTitle(a.name)) + '">' +
                jobCell(a.name) + '</td>'
              : '') +
            '<td>' + S.fmtInt(a.total) + '</td>' +
            '<td>' + S.fmtNum(a.share * 100, 1) + '%</td>' +
            '<td data-dps="' + esc(a.name) + '">' + S.fmtNum(a.dps, 1) + '</td>' +
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
      var badge = jobBadge(a.name, 'row-job');
      html += '<tr class="group"><td colspan="8">' +
              '<span class="swatch" style="background:' + colorOf(a.name) + '"></span>' +
              esc(nameOf(a.name)) +
              (badge ? ' ' + badge : '') +
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
    $('drillSub').textContent = nameOf(app.drill.actor) + ' · ' + S.fmtInt(d.count) +
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
        return '<tr><td>' + S.fmtElapsed(e.t) + '</td>' +
               '<td>' + esc(nameOf(e.target) || '—') + '</td>' +
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
    // Sorted on what is PRINTED. Sorting hidden names by the name they are
    // hiding leaves them sitting in their own alphabetical slot, which both
    // reads as a broken sort and narrows down who they are.
    var list = Object.keys(names).sort(function (a, b) {
      var x = nameOf(a), y = nameOf(b);
      return x < y ? -1 : x > y ? 1 : 0;
    });

    // The Kind column is the addon's answer, straight off the entity's spawn
    // flags; the Counted column is what this meter does with it. They differ
    // only where the user has overridden one by hand.
    // Hidden here too, and the Job column goes with it for the same reason it
    // does in the character table. This panel is the one that lists the party
    // rather than the damage, so leaving it out of the rule would put every
    // name back on screen the moment somebody opened it on stream. The override
    // buttons still carry the real name in `data-name`.
    var withJob = !app.anon;
    $('rosterTable').innerHTML = list.length
      ? '<thead><tr><th>Name</th>' + (withJob ? '<th class="job">Job</th>' : '') +
        '<th>Kind</th><th>Counted</th><th></th></tr></thead><tbody>' +
        list.map(function (n) {
          var mob = roster.isMob(n);
          return '<tr><td>' + esc(nameOf(n)) + '</td>' +
                 (withJob
                   ? '<td class="job" title="' + esc(roster.jobTitle(n)) + '">' + jobCell(n) + '</td>'
                   : '') +
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

  $('startBtn').addEventListener('click', startSession);
  $('pauseBtn').addEventListener('click', secondary);

  /* Nothing but a re-render: no filter moves, no total changes, and the chips
     rebuild because their signature is over the name as drawn. */
  $('anonBtn').addEventListener('click', function () {
    app.anon = !app.anon;
    try { localStorage.setItem(ANON_KEY, app.anon ? 'on' : 'off'); } catch (e) { }
    applyAnon();
    render();
  });

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
  loadCharRow();
  loadAnon();
  applySession();

  // After the handlers above, not before: wiring a card for pop-out moves the
  // buttons already in its head into the new controls group, and a listener
  // attached to a button survives being moved but is not re-attached.
  DPS.popout.init({
    onRender: render,
    // The floating panels are laid over the game so a pull can be run without
    // leaving it, which is exactly when reaching back to the page to press
    // Start is the one thing you cannot do. So they carry the controls too.
    session: { start: startSession, pause: secondary, paint: paintSession }
  });

  window.DPS.app = app;   // console handle for debugging
  poll();
  setInterval(tickLine, TICK_MS);
  // The clock is a second-resolution figure, so it is written on its own
  // slower interval rather than ten times per second alongside the canvas.
  setInterval(tickClock, 250);
})();
