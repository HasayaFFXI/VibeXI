/*
 * host.js -- Zerg's only adaptation of the damage meter's front end.
 *
 * Injected by MainForm before any of the page's own scripts run. The page is
 * apps/damage-meter/web, unmodified; everything Zerg needs to change about how
 * it behaves goes here, as a feature the page already understands.
 * Never fork a file from web/ to get a Zerg-only behaviour (PLAN.md, Decision 2).
 */
(function () {
  'use strict';

  /*
   * Pop-outs. popout.js's "Pop Out" is Document Picture-in-Picture, and the
   * button is disabled outright where that API is missing. Zerg can do better
   * than PiP -- a native always-on-top window that can be colour-keyed, and as
   * many at once as the player wants -- but only through window.open(), which
   * MainForm turns into a PopoutForm.
   *
   * So this does not remove the API, it provides it: requestWindow() is a
   * window.open(). popout.js then sees PiP present, keeps the button enabled
   * and in its "always on top" mode, and moves the card into the window it gets
   * back exactly as it would into a PiP window.
   *
   * The window is named after the card being popped out (dpsPanel_<key>, the
   * same names popout.js's own window mode uses), so each panel remembers its
   * own position. requestWindow() is not told the card, but it is always called
   * synchronously from the Pop Out button's click, so the event in progress
   * says which card it is. A call from anywhere else (the console's
   * DPS.popout.place) just gets a numbered name.
   */
  var unnamed = 0;

  function cardKey() {
    var ev = window.event;
    var el = ev && ev.target;
    var card = el && el.closest ? el.closest('[data-popout]') : null;
    return card ? card.getAttribute('data-popout') : null;
  }

  /*
   * The punch-out. popout.js's keyed mode paints the page background exactly
   * #010203 and asks for that colour to be keyed out. WebView2 draws through
   * DirectComposition, like Chrome, and a colour key never touches pixels it
   * drew -- measured in Zerg on 2026-10-02: the game showed through at 85%, but
   * dimmed by an opaque near-black background, not punched through it.
   *
   * What the key DOES drop is the form's own background, wherever the page is
   * transparent (PLAN.md Phase 0). So in keyed mode the page background goes
   * transparent instead; PopoutForm paints #010203 behind the view and keys
   * that out. The cards keep their own backgrounds and stay solid. Outside
   * keyed mode (html without .key-bg) this rule matches nothing.
   */
  var KEYED_CSS =
    'html.key-bg, html.key-bg body, html.key-bg .pop-doc' +
    ' { background: transparent !important; }';

  function dressForZerg(win) {
    try {
      var d = win.document;
      if (d.getElementById('zerg-keyed')) return;     // a reused window
      var s = d.createElement('style');
      s.id = 'zerg-keyed';
      s.textContent = KEYED_CSS;
      (d.head || d.documentElement).appendChild(s);
    } catch (e) { /* the panel still works, just without the punch-out */ }
  }

  var shim = {
    window: null,
    requestWindow: function (opts) {
      opts = opts || {};
      var key = cardKey() || ('panel' + (++unnamed));
      var w = Math.round(+opts.width) || 460;
      var h = Math.round(+opts.height) || 320;
      var win = window.open('', 'dpsPanel_' + key, 'popup=yes,width=' + w + ',height=' + h);
      if (!win) return Promise.reject(new Error('Zerg could not open the window.'));
      dressForZerg(win);
      return Promise.resolve(win);
    }
  };

  try {
    Object.defineProperty(window, 'documentPictureInPicture',
                          { value: shim, configurable: true });
  } catch (e) { /* not fatal: the browser's own PiP stays, without Zerg's window */ }
})();
