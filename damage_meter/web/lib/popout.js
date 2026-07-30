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
 * Two window kinds, because the web only offers one always-on-top surface:
 *
 *   'window'  window.open(). Dragged and resized anywhere, but a normal
 *             browser window: it cannot be raised above other applications.
 *   'focus'   documentPictureInPicture.requestWindow(). Always-on-top, which
 *             is what "Keep in focus" means. Chromium only, and the browser
 *             allows exactly ONE at a time -- asking for a second closes the
 *             first, which lands here as an ordinary close and docks that panel
 *             back into the page.
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

    // Appended after the pop-out buttons are added, so ordering is
    // Pop out / Keep in focus / Close rather than the reverse.
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
      // The card's own heading, which the child window's title bar takes over --
      // the drill-down rewrites its h2 with the action name, so the window has
      // to follow rather than keep the static label it was opened with.
      h2: card.querySelector('.card-head h2') || null
    };

    p.tools = toolsFor(card);
    p.popBtn = mkBtn(doc, 'Pop out', 'Open this panel in its own window');
    p.focusBtn = mkBtn(doc, 'Keep in focus', FOCUS_HINT);
    p.focusBtn.setAttribute('aria-pressed', 'false');
    p.focusBtn.disabled = !PIP;
    p.tools.appendChild(p.popBtn);
    p.tools.appendChild(p.focusBtn);
    if (p.tools.retake) { p.tools.retake(); delete p.tools.retake; }

    p.notes = [addNote(p.tools)];

    p.popBtn.addEventListener('click', function () {
      place(p, p.mode === 'docked' ? 'window' : 'docked');
    });
    p.focusBtn.addEventListener('click', function () {
      place(p, p.mode === 'focus' ? 'window' : 'focus');
    });

    buildPlaceholder(p);
    return p;
  }

  /*
   * What stands in for the card while it is away. It holds the "Keep in focus"
   * toggle because requestWindow() needs a user gesture *in this document* --
   * a click inside the child window cannot grant it.
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
    p.phFocus = mkBtn(doc, 'Keep in focus', FOCUS_HINT);
    p.phFocus.setAttribute('aria-pressed', 'false');
    p.phFocus.disabled = !PIP;
    p.phBack = mkBtn(doc, 'Bring back', 'Return this panel to the page');
    tools.appendChild(p.phFocus);
    tools.appendChild(p.phBack);
    ph.querySelector('.ph-head').appendChild(tools);
    p.notes.push(addNote(tools));

    p.phFocus.addEventListener('click', function () {
      place(p, p.mode === 'focus' ? 'window' : 'focus');
    });
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

  function dress(p, win) {
    var d = win.document;
    var body = d.querySelector('.pop-body');
    if (body) {                                     // already dressed
      p.flag = d.querySelector('.pop-flag');
      p.barTitle = d.querySelector('.pop-title');
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
    p.flag = d.createElement('span');
    p.flag.className = 'pop-flag';
    p.flag.textContent = 'kept in focus';
    p.flag.hidden = true;
    var back = mkBtn(d, 'Dock back', 'Return this panel to the main window');
    back.addEventListener('click', function () { place(p, 'docked'); });
    acts.appendChild(p.flag);
    acts.appendChild(back);
    bar.appendChild(h);
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
    p.flag = null;

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
   * Only this module's own two buttons are hidden while the panel is away --
   * the placeholder is driving now. Whatever else the card head already held
   * (the drill-down's Close) travels with the card and stays usable there, so
   * the group as a whole must not be hidden.
   */
  function showOwnTools(p, on) {
    p.popBtn.hidden = !on;
    p.focusBtn.hidden = !on;
  }

  function sync(p) {
    var out = p.mode !== 'docked';
    p.popBtn.textContent = out ? 'Bring back' : 'Pop out';
    p.popBtn.setAttribute('aria-pressed', String(out));
    p.focusBtn.setAttribute('aria-pressed', String(p.mode === 'focus'));
    p.phFocus.setAttribute('aria-pressed', String(p.mode === 'focus'));
    if (p.flag) p.flag.hidden = p.mode !== 'focus';
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

    [].forEach.call(doc.querySelectorAll('[data-popout]'), function (card) {
      var key = card.getAttribute('data-popout');
      var h2 = card.querySelector('h2');
      var title = card.getAttribute('data-popout-title') ||
                  (h2 ? h2.textContent : key);
      panels[key] = build(key, card, String(title).trim());
    });

    global.addEventListener('pagehide', closeAll);
    return panels;
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
    place: function (key, mode) { return panels[key] ? place(panels[key], mode) : null; },
    dock: function (key) { if (panels[key]) dock(panels[key]); },
    closeAll: closeAll,
    supportsFocus: PIP,
    panels: panels
  };
})(window);
