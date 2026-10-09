// Namespace bootstrap + tiny helpers. Load this first.
//
// Everything is a classic script assigning into a single `window.FFXI` global
// rather than an ES module, deliberately: ES modules are blocked by CORS when a
// page is opened from file://, and being able to double-click a page in pages/
// and have it just work is worth more than import syntax. There is no build step.
window.FFXI = window.FFXI || {};
FFXI.data = FFXI.data || {};
FFXI.lib = FFXI.lib || {};
FFXI.components = FFXI.components || {};

FFXI.core = (function () {
  function clamp(v, lo, hi) { return Math.min(Math.max(v, lo), hi); }

  function randomInt(min, max) { return Math.floor(Math.random() * (max - min + 1)) + min; }

  function escapeHtml(s) {
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
  }

  // Accepts an element or an id string, so component configs can be written
  // either way.
  function resolve(elOrId) {
    if (!elOrId) return null;
    return typeof elOrId === 'string' ? document.getElementById(elOrId) : elOrId;
  }

  // Resolves every value of a config map of elements/ids at once.
  function resolveAll(map) {
    const out = {};
    Object.keys(map || {}).forEach(k => { out[k] = resolve(map[k]); });
    return out;
  }

  function num(elOrId, fallback) {
    const el = resolve(elOrId);
    if (!el) return fallback === undefined ? 0 : fallback;
    const v = +el.value;
    return Number.isFinite(v) ? v : (fallback === undefined ? 0 : fallback);
  }

  function checked(elOrId) {
    const el = resolve(elOrId);
    return !!(el && el.checked);
  }

  // Centres an element inside its nearest scrollable ancestor, leaving the page
  // scroll alone. Element.scrollIntoView() would also scroll every ancestor up to
  // the document, which yanks the page around when a panel re-renders on every
  // field edit.
  function scrollWithin(el) {
    if (!el) return;
    let wrap = el.parentElement;
    while (wrap && wrap !== document.body && wrap.scrollHeight <= wrap.clientHeight) {
      wrap = wrap.parentElement;
    }
    if (!wrap || wrap === document.body || wrap === document.documentElement) return;
    const r = el.getBoundingClientRect();
    const w = wrap.getBoundingClientRect();
    wrap.scrollTop += (r.top - w.top) - (w.height - r.height) / 2;
  }

  // Min/max over a large array. Spreading into Math.min/max overflows the call
  // stack past ~100k entries, which the 200k-trial ceiling reaches.
  function minMax(values) {
    if (!values.length) return { min: 0, max: 0 };
    let min = values[0], max = values[0];
    for (let i = 1; i < values.length; i++) {
      if (values[i] < min) min = values[i];
      if (values[i] > max) max = values[i];
    }
    return { min, max };
  }

  return { clamp, randomInt, escapeHtml, resolve, resolveAll, num, checked, scrollWithin, minMax };
})();
