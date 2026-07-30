/*
 * theme.js -- the bridge between shared-ui/css/ffxi-theme.css and <canvas>.
 *
 * Canvas can't use CSS custom properties, so before this existed each app kept
 * a hand-maintained copy of the palette in its chart code and the two drifted.
 * Everything here reads the live computed value off :root instead, which means
 * the stylesheet is the single source of truth and a light/dark swap needs no
 * JS palette at all.
 *
 * Classic script on window.FFXITheme, never an ES module: ws_calculator pages are
 * opened straight off disk and modules are CORS-blocked on file://.
 *
 *   FFXITheme.v('--blade')      one token, resolved
 *   FFXITheme.chart()           the furniture set chart code needs
 *   FFXITheme.series(slot)      categorical slot 0..7, wrapping
 *   FFXITheme.bind({...})       wire a light/dark toggle button
 *
 * Reads are cached per theme; call FFXITheme.flush() if a stylesheet is swapped
 * at runtime by something other than bind().
 */
(function (global) {
  'use strict';

  var SLOTS = 8;

  var cache = null;
  var cacheKey = null;

  function key() {
    var root = global.document && global.document.documentElement;
    return root ? (root.getAttribute('data-theme') || 'dark') : 'dark';
  }

  function styles() {
    var k = key();
    if (cache && cacheKey === k) return cache;
    cache = getComputedStyle(global.document.documentElement);
    cacheKey = k;
    return cache;
  }

  function flush() { cache = null; cacheKey = null; }

  // One token. `fallback` covers the stylesheet not being loaded at all, which
  // is the only way an empty string comes back.
  function v(name, fallback) {
    var s = styles().getPropertyValue(name);
    s = s ? s.trim() : '';
    return s || fallback || '';
  }

  /*
   * The chart furniture, under the names drawing code actually wants. Both
   * apps' chart layers call this once per draw -- cheap, because getComputedStyle
   * is cached until the theme changes.
   */
  function chart() {
    return {
      surface:   v('--surface',    '#1b1e27'),
      surface2:  v('--surface2',   '#21242f'),
      grid:      v('--grid',       'rgba(255,255,255,0.05)'),
      axis:      v('--axis',       '#2d3140'),
      axisAlt:   v('--axis-alt',   '#3a3f52'),
      ink:       v('--bone',       '#e8e6df'),
      ink2:      v('--mist',       '#8d92a3'),
      muted:     v('--faint',      '#5c6070'),
      blade:     v('--blade',      '#b8433a'),
      bladeDim:  v('--blade-dim',  '#7a352f'),
      bladeFill: v('--blade-fill', 'rgba(184,67,58,0.12)'),
      brass:     v('--brass',      '#c9a227'),
      brassDim:  v('--brass-dim',  'rgba(201,162,39,0.4)'),
      good:      v('--good',       '#6ea67a'),
      critical:  v('--critical',   '#b8433a'),
      font:      v('--font-sans',  'system-ui, sans-serif'),
      mono:      v('--font-mono',  'ui-monospace, Consolas, monospace'),
      display:   v('--font-display', 'Georgia, serif')
    };
  }

  /* Eight fixed categorical slots, in the documented order. Callers that can
     exceed eight entities should fold the tail into one muted "Other" rather
     than relying on the wrap. */
  function series(slot) {
    return v('--series-' + ((slot % SLOTS + SLOTS) % SLOTS + 1), '#3987e5');
  }

  /*
   * A live view of the chart palette: `palette().blade` and `palette.blade` both
   * work, and the property re-reads on access so a theme change needs no
   * re-wiring at the call sites. This is what lets ws_calculator's components keep
   * writing `C.COLORS.blade` unchanged.
   */
  function liveColors() {
    var out = {};
    Object.keys(chart()).forEach(function (k) {
      Object.defineProperty(out, k, {
        enumerable: true,
        get: function () { return chart()[k]; }
      });
    });
    return out;
  }

  /*
   * Wires a light/dark toggle.
   *   button      element or id of the toggle (optional)
   *   storageKey  localStorage key to persist the choice under (optional)
   *   onChange    called with the new theme name after every change
   * Returns { get, set, toggle }.
   */
  function bind(opts) {
    opts = opts || {};
    var doc = global.document;
    var root = doc.documentElement;
    var btn = typeof opts.button === 'string' ? doc.getElementById(opts.button) : opts.button;

    function get() { return root.getAttribute('data-theme') === 'light' ? 'light' : 'dark'; }

    function apply(name, persist, announce) {
      var next = name === 'light' ? 'light' : 'dark';
      root.setAttribute('data-theme', next);
      flush();
      // The button offers the OTHER theme, so it is labelled with that one.
      if (btn) btn.textContent = next === 'dark' ? 'Light' : 'Dark';
      if (persist && opts.storageKey) {
        try { localStorage.setItem(opts.storageKey, next); } catch (e) { /* private mode */ }
      }
      if (announce && opts.onChange) opts.onChange(next);
      return next;
    }

    function set(name) { return apply(name, true, true); }
    function toggle() { return set(get() === 'dark' ? 'light' : 'dark'); }

    var saved = null;
    if (opts.storageKey) {
      try { saved = localStorage.getItem(opts.storageKey); } catch (e) { saved = null; }
    }
    // Restoring the stored choice is not a change: it neither re-persists nor
    // fires onChange, so callers can safely repaint from onChange without it
    // running before the app has anything to paint. The document's own
    // data-theme (dark) stands in when nothing is stored.
    apply(saved === 'light' || saved === 'dark' ? saved : get(), false, false);

    if (btn) btn.addEventListener('click', toggle);
    return { get: get, set: set, toggle: toggle };
  }

  global.FFXITheme = {
    v: v,
    chart: chart,
    series: series,
    liveColors: liveColors,
    bind: bind,
    flush: flush,
    SLOTS: SLOTS
  };
})(window);
