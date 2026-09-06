/*
 * popout.js -- gives every chart/table card its own OS window.
 *
 * The card's real DOM is *moved* into the child window (adoptNode), never
 * cloned. That is the whole trick: `render()` in app.js keeps writing to the
 * same nodes it always did, so nothing in the render path has to know a panel
 * is somewhere else. The one thing that does break is `getElementById`, which
 * only searches its own document -- so `byId()` consults an index of the ids
 * that are currently out of the page, and app.js uses it as its `$`.
 *
 * Two window kinds, only one of which the UI offers:
 *
 *   'focus'   documentPictureInPicture.requestWindow(). Always-on-top, which
 *             is what "Keep in focus" means -- the only button on a card, and
 *             the only mode a user can reach. Chromium only, and the browser
 *             allows exactly ONE at a time: asking for a second closes the
 *             first, which lands here as an ordinary close and docks that panel
 *             back into the page.
 *   'window'  window.open(). A plain browser window that cannot be raised above
 *             other applications, which is why it is no longer offered. It stays
 *             reachable through DPS.popout.place(key, 'window') because it is
 *             what the always-on-top request degrades to where PiP is missing,
 *             and because it is the only way to exercise the move machinery in
 *             an embedded webview -- see the iframe recipe in CLAUDE.md.
 *
 * Classic script on DPS.popout, same as the rest of web/lib.
 */
(function (global) {
  'use strict';

  var DPS = global.DPS || (global.DPS = {});
  var doc = global.document;

  var PIP = !!(global.documentPictureInPicture &&
               typeof global.documentPictureInPicture.requestWindow === 'function');

  var FOCUS_HINT = PIP
    ? 'Float this window above other applications. Only one panel at a time; ' +
      'the previous one returns to the page.'
    : 'Needs a Chromium browser (Chrome or Edge) — no other browser lets a page ' +
      'raise a window above other applications.';

  var panels = {};        // key -> panel record
  var index = {};         // element id -> element, while it lives outside `doc`
  var onRender = function () { };
  var watchdog = null;

  /*
   * The session controls, handed in by app.js: `{ start, pause, paint }`.
   *
   * This module builds the two buttons into every floating window's bar and
   * knows nothing else about them -- not what a session is, not what the labels
   * say, not which state is which. `paint` is app.js's own renderer and
   * `sessionView` is the state it renders, pushed in through `session()` the
   * same way a theme name is pushed through `theme()`.
   *
   * `sessionView` is REMEMBERED, not just forwarded, because a window opened
   * later has to be dressed correctly at birth: it missed every push that came
   * before it existed.
   */
  var sessionApi = null;
  var sessionView = null;

  /*
   * The two live figures -- elapsed and total damage -- pushed in by app.js on
   * its own tick.
   *
   * Separate from `sessionView` because they move on a different clock: the view
   * changes only when a button is pressed, these change four times a second and
   * would otherwise repaint two buttons' labels, titles and classes every time.
   * Remembered for the same reason the view is -- a window opened mid-pull has
   * to be born showing the right numbers, not zeroes until the next tick.
   *
   * Both text, both finished: this module formats nothing and knows of neither
   * a clock nor a damage total, exactly as it knows nothing about sessions.
   */
  var readoutView = { clock: '00:00', state: 'idle', total: '0' };

  // Per-panel opacity (15..100) and background punch-out, remembered across
  // sessions. See "opacity" below for what each one actually does.
  var ALPHA_KEY = 'ffxi_dps_alpha';
  var KEYBG_KEY = 'ffxi_dps_keybg';
  var alphas = readMap(ALPHA_KEY);
  var keys = readMap(KEYBG_KEY);

  /*
   * What a panel opens at with nothing else to go on. Not 100: these windows
   * exist to be laid over the game, so opaque is the wrong thing to start from
   * -- a panel that comes up already see-through says what it is for without
   * the user having to find the slider first.
   *
   * The main page's "Pop-out opacity" config overwrites it and DEFAULT_KEY
   * remembers that choice; DEFAULT_ALPHA is only the value before anyone has
   * said otherwise.
   */
  var DEFAULT_ALPHA = 85;
  var DEFAULT_KEY = 'ffxi_dps_alpha_default';
  var fallbackAlpha = readDefaultAlpha();

  function readMap(k) {
    try { return JSON.parse(global.localStorage.getItem(k) || '{}') || {}; }
    catch (e) { return {}; }
  }
  function saveAlphas() {
    try { global.localStorage.setItem(ALPHA_KEY, JSON.stringify(alphas)); } catch (e) { }
  }
  function saveKeys() {
    try { global.localStorage.setItem(KEYBG_KEY, JSON.stringify(keys)); } catch (e) { }
  }
  /* Clamps for itself rather than calling clampAlpha, which is the one place
     that cannot: clampAlpha's own fallback is the value being read here. */
  function readDefaultAlpha() {
    var v;
    try { v = Math.round(+global.localStorage.getItem(DEFAULT_KEY)); } catch (e) { }
    if (!isFinite(v) || !v) return DEFAULT_ALPHA;
    return Math.max(15, Math.min(100, v));
  }

  // ------------------------------------------------------------------ lookup

  /*
   * The drop-in replacement for document.getElementById. Ids inside a popped-out
   * card are registered on the way out and dropped on the way back, so this is
   * only ever a map hit for the handful of nodes actually in a child window.
   */
  function byId(id) {
    return index[id] || doc.getElementById(id);
  }

  function indexIds(root, on) {
    var nodes = root.querySelectorAll('[id]');
    for (var i = -1; i < nodes.length; i++) {
      var n = i < 0 ? root : nodes[i];
      if (!n.id) continue;
      if (on) index[n.id] = n; else delete index[n.id];
    }
  }

  function each(fn) {
    Object.keys(panels).forEach(function (k) { fn(panels[k]); });
  }

  // ------------------------------------------------------------------ set-up

  function mkBtn(dc, label, title) {
    var b = dc.createElement('button');
    b.type = 'button';
    b.className = 'ghost';
    b.textContent = label;
    if (title) b.title = title;
    return b;
  }

  /* A slot for "the browser blocked that" messages. One per controls group,
     because the card head is hidden exactly when the placeholder is showing. */
  function addNote(tools) {
    var n = doc.createElement('span');
    n.className = 'pop-note';
    n.setAttribute('role', 'status');
    n.hidden = true;
    tools.appendChild(n);
    return n;
  }

  /*
   * The controls group in a card head. Any buttons already sitting directly in
   * the head (the drill-down's Close) are pulled into it so they stay grouped
   * with the new ones instead of being pushed to the far side by the head's
   * space-between. A card without a head -- Diagnostics is a bare <details> --
   * gets a free-floating group pinned to its top-right corner.
   */
  function toolsFor(card) {
    var head = card.querySelector('.card-head');
    var existing;

    if (head) {
      var found = head.querySelector('.card-tools');
      if (found) return found;
      existing = [].filter.call(head.children, function (c) { return c.tagName === 'BUTTON'; });
    } else {
      existing = [];
    }

    var tools = doc.createElement('div');
    tools.className = 'card-tools' + (head ? '' : ' free');

    if (head) head.appendChild(tools);
    else card.insertBefore(tools, card.firstChild);

    // Appended after the pop-out button is added, so ordering is
    // Keep in focus / Close rather than the reverse.
    tools.retake = function () {
      existing.forEach(function (b) { tools.appendChild(b); });
    };
    return tools;
  }

  function build(key, card, title) {
    var p = {
      key: key,
      card: card,
      title: title,
      mode: 'docked',     // docked | window | focus
      win: null,
      obs: null,
      alpha: clampAlpha(alphas[key]),
      // On by default: a window laid over a game is wanted see-through, and a
      // dimmed background is a poor second to no background.
      keyBg: keys[key] !== false,
      // The card's own heading, which the child window's title bar takes over --
      // the drill-down rewrites its h2 with the action name, so the window has
      // to follow rather than keep the static label it was opened with.
      h2: card.querySelector('.card-head h2') || null
    };

    p.tools = toolsFor(card);
    p.focusBtn = mkBtn(doc, 'Keep in focus', FOCUS_HINT);
    p.focusBtn.setAttribute('aria-pressed', 'false');
    p.focusBtn.disabled = !PIP;
    p.tools.appendChild(p.focusBtn);
    if (p.tools.retake) { p.tools.retake(); delete p.tools.retake; }

    p.notes = [addNote(p.tools)];

    // Turning it off docks the panel. There is no plain-window mode to fall back
    // to any more, so the toggle is straight between "on the page" and "floating
    // above everything" -- the same two states "Bring back" moves between.
    p.focusBtn.addEventListener('click', function () {
      place(p, p.mode === 'focus' ? 'docked' : 'focus');
    });

    buildPlaceholder(p);
    return p;
  }

  /*
   * What stands in for the card while it is away. It carries "Bring back" and
   * nothing else: a panel can only be away by being kept in focus, so a second
   * "Keep in focus" toggle here would be a differently-worded button doing
   * exactly what "Bring back" already does.
   */
  function buildPlaceholder(p) {
    var ph = doc.createElement('section');
    ph.className = 'card popout-ph';
    ph.innerHTML =
      '<div class="ph-head"><div>' +
      '<h2 class="inline"></h2>' +
      '<p class="card-sub">Showing in its own window.</p>' +
      '</div></div>';
    ph.querySelector('h2').textContent = p.title;

    var tools = doc.createElement('div');
    tools.className = 'card-tools';
    p.phBack = mkBtn(doc, 'Bring back', 'Return this panel to the page');
    tools.appendChild(p.phBack);
    ph.querySelector('.ph-head').appendChild(tools);
    p.notes.push(addNote(tools));

    p.phBack.addEventListener('click', function () { place(p, 'docked'); });

    p.ph = ph;
  }

  // ---------------------------------------------------------- child document

  /* Stylesheets are re-linked by their *resolved* href: the child is about:blank,
     so a relative "style.css" would resolve against nothing useful. */
  function copyStyles(d) {
    var links = [];
    [].forEach.call(doc.querySelectorAll('link[rel~="stylesheet"]'), function (l) {
      var c = d.createElement('link');
      c.rel = 'stylesheet';
      c.href = l.href;
      d.head.appendChild(c);
      links.push(c);
    });
    [].forEach.call(doc.querySelectorAll('style'), function (s) {
      var c = d.createElement('style');
      c.textContent = s.textContent;
      d.head.appendChild(c);
    });
    return links;
  }

  /* Chart height is laid out by CSS and read back by chart.js's setup(), so a
     draw before the sheet lands measures zero. Wait, then let the caller draw. */
  function sheetsReady(links) {
    return Promise.all(links.map(function (l) {
      return new Promise(function (done) {
        if (l.sheet) { done(); return; }
        l.addEventListener('load', function () { done(); });
        l.addEventListener('error', function () { done(); });
        setTimeout(done, 2000);
      });
    }));
  }

  function themeName() {
    return doc.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
  }

  // ----------------------------------------------------------------- opacity

  /*
   * A floating panel is laid over the game, so it has to be seen through -- and
   * that is not something the page can do to itself. A browser window is opaque:
   * CSS `opacity` fades the document against the *browser's* backdrop, never onto
   * the desktop, and it does not even fade the page background, which propagates
   * to the window canvas and is painted outside the faded layer. Fading is a look,
   * not transparency.
   *
   * Only the OS can do it, so the local server does, over `GET /api/alpha`:
   *
   *   alpha     WS_EX_LAYERED + LWA_ALPHA on the window, from the slider. The
   *             whole window goes translucent, chrome, panel and background alike.
   *   punch-out LWA_COLORKEY on top of it, with the document background painted
   *             exactly KEY. Those pixels are dropped entirely, so the background
   *             is *gone* rather than dim and the numbers stay crisp over the
   *             game. It also makes the background click-through, which is what
   *             an overlay wants; the window's own title bar still drags it.
   *
   * `css-alpha` remains as the fallback for when none of that is available (no
   * server, wrong OS), and it is honest about what it is: the bar says "fade
   * only", because what shows through is the browser, not the game.
   *
   * The window is found by WHERE IT IS, not by its title -- a Document PiP
   * window's caption belongs to Chrome, not to the page. Hence the geometry in
   * the query string.
   */
  var KEY = '010203';     // punch-out colour: near-black, so text fringes on a
                          // dark panel stay dark. Matches no palette token.

  function clampAlpha(v) {
    v = Math.round(+v);
    if (!isFinite(v) || !v) return fallbackAlpha;
    return Math.max(15, Math.min(100, v));
  }

  /*
   * The main page's config, and the only opacity control reachable without a
   * window already being open.
   *
   * Setting it also drops every panel's own slider value. An override would
   * otherwise quietly outrank the config -- set once, sessions ago, on a window
   * that is not on screen to show what it is doing -- and the config would look
   * broken on exactly the panel the user was watching. One control, one
   * meaning: this is what the pop-out windows are, and a window's own slider
   * adjusts that one window from there on.
   */
  function setDefaultAlpha(v) {
    fallbackAlpha = clampAlpha(v);
    try { global.localStorage.setItem(DEFAULT_KEY, String(fallbackAlpha)); } catch (e) { }

    alphas = {};
    saveAlphas();

    each(function (p) {
      p.alpha = fallbackAlpha;
      if (p.alphaSlider) p.alphaSlider.value = String(p.alpha);
      if (p.alphaOut) p.alphaOut.textContent = p.alpha + '%';
      if (p.win && !p.win.closed) applyAlpha(p);
    });
    return fallbackAlpha;
  }

  function alphaControl(p, d) {
    var wrap = d.createElement('span');
    wrap.className = 'pop-alpha';

    var sl = d.createElement('input');
    sl.type = 'range';
    sl.min = '15';
    sl.max = '100';
    sl.step = '1';
    sl.value = String(p.alpha);
    sl.setAttribute('aria-label', 'Window opacity');
    sl.title = 'Window opacity — drag left to see the game through this window.';

    var out = d.createElement('span');
    out.className = 'pop-alpha-val';
    out.textContent = p.alpha + '%';

    sl.addEventListener('input', function () {
      p.alpha = clampAlpha(sl.value);
      out.textContent = p.alpha + '%';
      alphas[p.key] = p.alpha;
      saveAlphas();
      applyAlpha(p);
    });

    var warn = d.createElement('span');
    warn.className = 'pop-alpha-warn';
    warn.textContent = 'fade only';
    warn.hidden = true;

    wrap.appendChild(sl);
    wrap.appendChild(out);
    wrap.appendChild(warn);
    p.alphaWrap = wrap;
    p.alphaSlider = sl;
    p.alphaOut = out;
    p.alphaWarn = warn;
    return wrap;
  }

  /*
   * Start and Pause, in a floating window's own bar.
   *
   * The reason these exist out here at all: a panel is floated over the game so
   * the pull can be run without leaving it, and reaching back to the page to
   * press Start is the one thing that cannot be done from there. A meter you
   * have to alt-tab to start is a meter that gets started late, and late is an
   * error divided into every DPS figure it then reports.
   *
   * Built only when app.js supplied the handlers, so the module still works --
   * and still tests -- on a page with no session controls at all.
   */
  function sessionControl(p, d) {
    if (!sessionApi) return null;

    /* `.pop-session` moved OUT to this wrapper and off the segmented pair. It is
       what `.pop-bar > *:not(.pop-session)` exempts from the bar's fade, and the
       clock belongs inside that exemption for the same reason the buttons do:
       a panel is floated over the game to be read at a glance, and the elapsed
       time at 45% over a battle scene is not a readout. */
    var wrap = d.createElement('div');
    wrap.className = 'pop-session';

    var seg = d.createElement('div');
    seg.className = 'segmented session';

    var start = d.createElement('button');
    start.type = 'button';
    start.className = 'session-start';
    start.textContent = 'Start';
    start.addEventListener('click', function () { sessionApi.start(); });

    var pause = d.createElement('button');
    pause.type = 'button';
    pause.className = 'session-pause';
    pause.textContent = 'Pause';
    pause.addEventListener('click', function () { sessionApi.pause(); });

    seg.appendChild(start);
    seg.appendChild(pause);
    wrap.appendChild(seg);

    // To the RIGHT of the buttons: the pair is the fixed hit target the user
    // aims at without looking, and a readout that changes width would move it.
    // Elapsed first, then the total -- the same order, and the same two figures,
    // as the first two tiles on the page.
    var clk = d.createElement('span');
    clk.className = 'pop-clock';
    clk.title = 'Elapsed session time';
    wrap.appendChild(clk);

    var tot = d.createElement('span');
    tot.className = 'pop-total';
    tot.title = 'Total damage this session';
    wrap.appendChild(tot);

    p.startBtn = start;
    p.pauseBtn = pause;
    p.clockEl = clk;
    p.totalEl = tot;
    paintPanel(p);
    paintReadout(p);
    return wrap;
  }

  /* One panel's pair, brought up to the current state. `paint` overwrites each
     button's className, so nothing may be hung on the buttons themselves --
     the floating-bar sizing is selected through the `.pop-session` wrapper. */
  function paintPanel(p) {
    if (!sessionApi || !sessionView || !p.startBtn) return;
    sessionApi.paint(sessionView, p.startBtn, p.pauseBtn);
  }

  /* Push a new session state to every window that is currently open. */
  function session(view) {
    sessionView = view;
    each(function (p) {
      if (p.win && !p.win.closed) paintPanel(p);
    });
  }

  /* One panel's pair of figures. The session state rides as a class on the clock
     so a held one looks held -- a number that has merely stopped moving is
     indistinguishable from one moving slowly, and that is the whole question
     being asked of it. The total carries no state: it is the same number
     whether the clock is running or not. */
  function paintReadout(p) {
    if (p.clockEl) {
      p.clockEl.textContent = readoutView.clock;
      p.clockEl.className = 'pop-clock is-' + readoutView.state;
    }
    if (p.totalEl) p.totalEl.textContent = readoutView.total;
  }

  /* The live figures, pushed from app.js's tick as `{ clock, state, total }`.
     `state` is 'idle', 'live' or 'held'; both strings arrive finished, exactly
     as the session's labels do. */
  function readout(view) {
    readoutView = view;
    each(function (p) {
      if (p.win && !p.win.closed) paintReadout(p);
    });
  }

  /* Everything the server needs to find this window: the centre of it in screen
     coordinates, its size to check the hit against, and the pixel ratio, since
     these numbers are CSS pixels and the desktop may not be at 100%. */
  function geometry(win) {
    var w = win.outerWidth || win.innerWidth || 0;
    var h = win.outerHeight || win.innerHeight || 0;
    return '&x=' + Math.round((win.screenX || 0) + w / 2) +
           '&y=' + Math.round((win.screenY || 0) + h / 2) +
           '&w=' + Math.round(w) + '&h=' + Math.round(h) +
           '&dpr=' + (win.devicePixelRatio || 1);
  }

  /* Debounced, because dragging a range fires per pixel and each call is a
     round trip that ends in a window-manager call. */
  function applyAlpha(p, retry) {
    var win = p.win;
    if (!win || win.closed) return;
    var root = win.document.documentElement;
    root.style.setProperty('--pop-alpha', (p.alpha / 100).toFixed(3));
    root.style.setProperty('--pop-key', '#' + KEY);
    // Only once the OS path is known to have failed: fading first and undoing it
    // a beat later is a visible flash on every window that works properly.
    if (p.osAlpha === false) root.classList.add('css-alpha');

    clearTimeout(p.alphaTimer);
    p.alphaTimer = setTimeout(function () {
      if (!p.win || p.win.closed || !global.fetch) { osAlpha(p, false); return; }
      global.fetch('/api/alpha?title=' + encodeURIComponent(p.win.document.title) +
                   '&value=' + p.alpha + geometry(p.win) +
                   (p.keyBg ? '&key=' + KEY : ''), { cache: 'no-store' })
        .then(function (r) { return r.json(); })
        .then(function (j) {
          var hit = !!(j && j.ok && j.applied > 0);
          p.osWindow = j && j.window;
          p.osMethod = j && j.method;
          // A miss on the first go is usually the window not being where it says
          // it is yet, so give it one more before conceding to the fade.
          if (!hit && !retry) { setTimeout(function () { applyAlpha(p, true); }, 700); return; }
          osAlpha(p, hit);
        })
        .catch(function () { osAlpha(p, false); });
    }, 60);
  }

  function osAlpha(p, on) {
    p.osAlpha = on;
    if (!p.win || p.win.closed) return;
    var root = p.win.document.documentElement;
    // The window is genuinely translucent now, so fading the document as well
    // would darken it twice over.
    root.classList.toggle('css-alpha', !on);
    // Never paint the punch-out colour unless it is actually being punched out:
    // unkeyed, it is just a near-black window.
    root.classList.toggle('key-bg', on && !!p.keyBg);

    if (p.alphaWarn) {
      p.alphaWarn.hidden = on;
      p.alphaWarn.title =
        'The window itself could not be made transparent, so the slider is only ' +
        'fading the panel — what shows through is the browser, not the game. ' +
        'Most often the meter\'s server is an older copy still running: restart it.';
    }
  }

  function dress(p, win) {
    var d = win.document;
    var body = d.querySelector('.pop-body');
    if (body) {                                     // already dressed
      p.barTitle = d.querySelector('.pop-title');
      p.alphaWrap = d.querySelector('.pop-alpha');
      p.alphaSlider = p.alphaWrap ? p.alphaWrap.querySelector('input') : null;
      p.alphaOut = d.querySelector('.pop-alpha-val');
      p.alphaWarn = d.querySelector('.pop-alpha-warn');
      p.startBtn = d.querySelector('.pop-session .session-start');
      p.pauseBtn = d.querySelector('.pop-session .session-pause');
      p.clockEl = d.querySelector('.pop-session .pop-clock');
      p.totalEl = d.querySelector('.pop-session .pop-total');
      paintPanel(p);                                // may have moved on since
      paintReadout(p);
      return { body: body, links: [] };
    }

    d.documentElement.setAttribute('data-theme', themeName());
    d.documentElement.lang = 'en';
    d.title = p.title + ' — Damage Meter';

    var links = copyStyles(d);

    var bar = d.createElement('header');
    bar.className = 'pop-bar';
    p.barTitle = d.createElement('h1');
    var h = p.barTitle;
    h.className = 'pop-title';
    h.textContent = p.title;
    var acts = d.createElement('div');
    acts.className = 'pop-actions';
    // 'Dock', not 'Dock back': this bar is one line over a game screen.
    var back = mkBtn(d, 'Dock', 'Return this panel to the main window');
    back.className = 'ghost tiny';
    back.addEventListener('click', function () { place(p, 'docked'); });
    acts.appendChild(alphaControl(p, d));
    acts.appendChild(back);
    bar.appendChild(h);
    // Between the title and the chrome: the controls keep a fixed place, and the
    // title is the thing that gives way when the window is narrow.
    var sess = sessionControl(p, d);
    if (sess) bar.appendChild(sess);
    bar.appendChild(acts);

    body = d.createElement('main');
    body.className = 'wrap stack pop-body';
    var empty = d.createElement('p');
    empty.className = 'pop-empty';
    empty.textContent = 'Nothing here right now — this panel has nothing to show ' +
                        'until it is opened on the main page.';
    empty.hidden = true;
    body.appendChild(empty);

    d.body.className = 'pop-doc';
    d.body.appendChild(bar);
    d.body.appendChild(body);

    // The canvases must re-measure themselves after the user resizes the window.
    var timer;
    win.addEventListener('resize', function () {
      clearTimeout(timer);
      timer = setTimeout(onRender, 120);
    });
    // Closing the window is the same as docking, whether the user did it or the
    // browser did (a second "Keep in focus" displaces the first).
    win.addEventListener('pagehide', function () { if (p.win === win) dock(p); });

    return { body: body, links: links };
  }

  // ------------------------------------------------------------------- moves

  function measure(p) {
    var r = p.card.getBoundingClientRect();
    return {
      w: Math.min(1400, Math.max(420, Math.round(r.width) + 48)),
      h: Math.min(1000, Math.max(320, Math.round(r.height) + 108))
    };
  }

  function openWindow(p, mode, size) {
    if (mode === 'focus') {
      return global.documentPictureInPicture.requestWindow({
        width: size.w, height: size.h
      });
    }
    return Promise.resolve(
      global.open('', 'dpsPanel_' + p.key, 'popup=yes,width=' + size.w + ',height=' + size.h));
  }

  function place(p, mode) {
    if (mode === 'focus' && !PIP) mode = 'window';
    if (p.mode === mode) return Promise.resolve();
    if (mode === 'docked') { dock(p); return Promise.resolve(); }

    var prev = p.win;
    var prevMode = p.mode;
    var size = measure(p);

    return openWindow(p, mode, size).then(function (win) {
      if (!win) throw new Error('The browser blocked the pop-out window.');

      p.win = win;
      p.mode = mode;
      var out = dress(p, win);

      if (!p.ph.parentNode) {          // first move out: swap the placeholder in
        indexIds(p.card, true);
        p.card.parentNode.insertBefore(p.ph, p.card);
        p.card.parentNode.removeChild(p.card);
        showOwnTools(p, false);
        watchHidden(p);
        startWatchdog();
      }
      out.body.appendChild(win.document.adoptNode(p.card));

      // The old window's pagehide is ignored now that p.win has moved on.
      if (prev && prev !== win && !prev.closed) { try { prev.close(); } catch (e) { } }

      clearNote(p);
      syncTitle(p);
      sync(p);
      p.osAlpha = undefined;      // a new window is opaque until the server says otherwise
      applyAlpha(p);
      onRender();
      return sheetsReady(out.links).then(function () { onRender(); });
    }).catch(function (e) {
      // Nothing was moved unless the window opened, so putting the record back
      // is the whole rollback. A blocked popup and a refused PiP both land here,
      // and the browser's own wording is kept -- "blocked" and "not supported"
      // want different fixes from the user, and only it knows which happened.
      p.win = prev;
      p.mode = prevMode;
      sync(p);
      warn(p, (mode === 'focus'
        ? 'Could not open an always-on-top window: '
        : 'Could not open the pop-out window: ') + (e && e.message ? e.message : String(e)));
    });
  }

  function dock(p) {
    var win = p.win;
    p.win = null;
    p.mode = 'docked';
    clearTimeout(p.alphaTimer);
    p.alphaWrap = p.alphaSlider = p.alphaOut = p.alphaWarn = null;
    p.osAlpha = undefined;

    if (p.ph.parentNode) {
      if (p.card.ownerDocument !== doc) doc.adoptNode(p.card);
      p.ph.parentNode.replaceChild(p.card, p.ph);
      indexIds(p.card, false);
      showOwnTools(p, true);
      if (p.obs) { p.obs.disconnect(); p.obs = null; }
      if (p.titleObs) { p.titleObs.disconnect(); p.titleObs = null; }
    }
    if (win && !win.closed) { try { win.close(); } catch (e) { } }

    sync(p);
    onRender();
  }

  /*
   * Only this module's own button is hidden while the panel is away -- the
   * placeholder is driving now. Whatever else the card head already held (the
   * drill-down's Close) travels with the card and stays usable there, so the
   * group as a whole must not be hidden.
   */
  function showOwnTools(p, on) {
    p.focusBtn.hidden = !on;
  }

  function sync(p) {
    p.focusBtn.setAttribute('aria-pressed', String(p.mode === 'focus'));
  }

  function warn(p, msg) {
    p.notes.forEach(function (n) { n.textContent = msg; n.hidden = false; });
    clearTimeout(p.noteTimer);
    p.noteTimer = setTimeout(function () { clearNote(p); }, 8000);
  }

  function clearNote(p) {
    clearTimeout(p.noteTimer);
    p.notes.forEach(function (n) { n.hidden = true; n.textContent = ''; });
  }

  /*
   * A card that hides itself -- the drill-down, when no action is selected --
   * must not leave a ghost box on the page or a blank child window.
   */
  function watchHidden(p) {
    p.obs = new MutationObserver(function () { mirrorHidden(p); });
    p.obs.observe(p.card, { attributes: true, attributeFilter: ['hidden'] });
    mirrorHidden(p);

    if (p.h2) {
      p.titleObs = new MutationObserver(function () { syncTitle(p); });
      p.titleObs.observe(p.h2, { childList: true, characterData: true, subtree: true });
    }
  }

  /* The window bar shows the card's heading, and the card's own copy is hidden
     by CSS while it is out there -- one title per window, and it tracks a
     drill-down being pointed at a different action. */
  function syncTitle(p) {
    if (!p.win || p.win.closed || !p.barTitle) return;
    var t = p.h2 ? p.h2.textContent.trim() : p.title;
    p.barTitle.textContent = t || p.title;
    p.win.document.title = (t || p.title) + ' — Damage Meter';
  }

  function mirrorHidden(p) {
    var hidden = !!p.card.hidden;
    p.ph.hidden = hidden;
    if (p.win && !p.win.closed) {
      var e = p.win.document.querySelector('.pop-empty');
      if (e) e.hidden = !hidden;
    }
  }

  /* pagehide is not guaranteed for a window the user closes from the OS chrome,
     so a slow poll backs it up. It runs only while something is popped out. */
  function startWatchdog() {
    if (watchdog) return;
    watchdog = setInterval(function () {
      var any = false;
      each(function (p) {
        if (!p.win) return;
        if (p.win.closed) dock(p); else any = true;
      });
      if (!any) { clearInterval(watchdog); watchdog = null; }
    }, 1000);
  }

  function closeAll() {
    each(function (p) {
      if (p.win && !p.win.closed) { try { p.win.close(); } catch (e) { } }
    });
  }

  // -------------------------------------------------------------------- init

  /*
   * Scans for [data-popout] cards and wires each one. Call once, after the
   * app's own listeners are attached -- the head's existing buttons get moved
   * into the tools group, and a listener already on a button survives the move.
   */
  function init(opts) {
    opts = opts || {};
    if (opts.onRender) onRender = opts.onRender;
    if (opts.session) sessionApi = opts.session;

    [].forEach.call(doc.querySelectorAll('[data-popout]'), function (card) {
      var key = card.getAttribute('data-popout');
      var h2 = card.querySelector('h2');
      var title = card.getAttribute('data-popout-title') ||
                  (h2 ? h2.textContent : key);
      panels[key] = build(key, card, String(title).trim());
    });

    bindConfig();
    global.addEventListener('pagehide', closeAll);
    return panels;
  }

  /*
   * The opacity config in the page's top bar. Optional: this module is wired by
   * scanning for [data-popout] cards, and a page that carries those but not the
   * control still runs off the stored default.
   */
  function bindConfig() {
    var sl = doc.getElementById('popAlpha');
    var out = doc.getElementById('popAlphaVal');
    if (!sl) return;

    sl.value = String(fallbackAlpha);
    if (out) out.textContent = fallbackAlpha + '%';

    sl.addEventListener('input', function () {
      var v = setDefaultAlpha(sl.value);
      if (out) out.textContent = v + '%';
    });
  }

  /* The child windows carry their own <html data-theme>, so the toggle has to
     reach them; colours themselves still resolve off the main document's :root. */
  function theme(name) {
    each(function (p) {
      if (p.win && !p.win.closed) {
        p.win.document.documentElement.setAttribute('data-theme', name);
      }
    });
  }

  DPS.popout = {
    init: init,
    byId: byId,
    theme: theme,
    session: session,
    readout: readout,
    place: function (key, mode) { return panels[key] ? place(panels[key], mode) : null; },
    dock: function (key) { if (panels[key]) dock(panels[key]); },
    /* Read or set a panel's opacity (15..100) without the slider -- the console
       handle for checking that /api/alpha is reaching the window. */
    alpha: function (key, v) {
      var p = panels[key];
      if (!p) return null;
      if (v == null) return p.alpha;
      p.alpha = clampAlpha(v);
      alphas[key] = p.alpha;
      saveAlphas();
      if (p.alphaSlider) { p.alphaSlider.value = String(p.alpha); }
      if (p.alphaOut) { p.alphaOut.textContent = p.alpha + '%'; }
      applyAlpha(p);
      return p.alpha;
    },
    /* Read or set whether a panel punches its background out (LWA_COLORKEY).
       On by default, and the bar no longer carries a button for it -- this is
       the way back if a driver composites the layered window as solid black. */
    keyBg: function (key, v) {
      var p = panels[key];
      if (!p) return null;
      if (v == null) return p.keyBg;
      p.keyBg = !!v;
      keys[key] = p.keyBg;
      saveKeys();
      applyAlpha(p);
      return p.keyBg;
    },
    /* Read or set what a panel opens at (15..100) -- what the page's config
       drives. Setting it resets every panel to it; see setDefaultAlpha. */
    defaultAlpha: function (v) {
      return v == null ? fallbackAlpha : setDefaultAlpha(v);
    },
    closeAll: closeAll,
    supportsFocus: PIP,
    panels: panels
  };
})(window);
