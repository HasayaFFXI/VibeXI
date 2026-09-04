# shared-ui

The design system both FFXI apps render with. One palette, one set of primitives,
one place to change them.

```
css/ffxi-theme.css   tokens + every shared primitive
js/theme.js          window.FFXITheme -- the same tokens, for <canvas>
```

The look is [`../ws_calculator`](../ws_calculator)'s: dark ink field, bone text, blade-red
and brass accents, Shippori Mincho headings, JetBrains Mono for anything numeric.
[`../damage_meter`](../damage_meter) was brought onto it rather than the other way round.

## How each app loads it

**ws_calculator** — plain relative paths, because pages are opened straight off disk:

```html
<link rel="stylesheet" href="../../shared-ui/css/ffxi-theme.css">
<link rel="stylesheet" href="../shared/css/theme.css">
...
<script src="../../shared-ui/js/theme.js"></script>
```

**damage_meter** — `damage-meter.py` mounts this directory at the `/shared/` URL prefix,
so nothing has to be copied into `web/`:

```html
<link rel="stylesheet" href="/shared/css/ffxi-theme.css">
<link rel="stylesheet" href="style.css">
...
<script src="/shared/js/theme.js"></script>
```

The shared sheet always loads **first**; the app's own sheet loads after it and
holds only what no other app would want.

## The rule for what goes where

If a rule could plausibly be wanted by a second app, it belongs in
`css/ffxi-theme.css`. If it names a concept only one app has — the mob-derivation
grid, the log-source indicator — it belongs in that app's own sheet. When an
app-specific rule starts looking generally useful, **move it up here** rather than
copying it into the next app.

## Tokens

Chrome colors, spacing and type are all custom properties on `:root`. Dark is the
default and needs no attribute; light is opt-in via `data-theme="light"` on
`<html>` and is the same identity re-stepped for a pale surface, not an inversion.
ws_calculator never sets the attribute; damage_meter has a toggle.

Two families of color, kept apart on purpose:

- **Chrome** — `--ink`, `--surface`, `--surface2`, `--border`, `--bone`, `--mist`,
  `--faint`, `--dim`, `--blade`, `--brass`, `--good`, `--critical`. This is the
  identity.
- **Series** — `--series-1` … `--series-18`, categorical slots in a fixed,
  accessibility-checked order. 1–8 are the reference eight (blue, orange, aqua,
  yellow, magenta, green, violet, red) and are not to be re-stepped; 9–18 are the
  extension tier, solved against them so no pair involving a new slot is weaker
  than the weakest pair already inside 1–8. These encode *data*, so they are
  deliberately not folded into the blade/brass identity. Assign a slot per entity,
  in order. Eighteen is past what colour alone can carry: a chart using the upper
  tier owes the reader a legend, labels or a table, and past eighteen the wrap
  repeats a hue.

## Primitives

`.wrap` `.wrap.stack` `.grid` `.two-col` `.span2` `.span-all` `.scroll-x`
`.scroll-y` — layout.

`.page-head` (in-flow title block) and `.topbar` / `.topbar.sticky` (full-bleed
app bar) — page chrome. `h1 span` is the blade-red word in a title.

`.card` `.card.wide` `.card-head` `.card-sub` `.card-tools` — the card title
carries the brass tick that is the signature mark of this system, so a card gets
it for free from `<h2>`. `.card-tools` is the controls group on the far side of a
head; put every button for that card in it, or the head's `space-between` will
spread them.

`.field` `.hint` `.checkbox-field`, plus bare `input[type=number|text]` and
`select` — forms.

`button.primary` `button.secondary` `button.ghost` `button.link-btn`
`.segmented` `.chips` / `.chip` — controls.

`.tiles` / `.tile` (dashboard row, left-aligned) and `.stat-row` / `.stat`
(results panel, centred) `.breakdown` — metrics.

`table.data` with `.group` `.sub` `.on` `.current-tier` `.selectable` rows, plus
`.table-wrap` for a framed scroll box — tables.

`.chart-wrap` (`.short` 170 / default 210 / `.mid` 220 / `.tall` 320) `.chart-cap`
`.chart-tip` `.legend` — charts. **The height lives in CSS**, not in the drawing
code: both apps' `setupCanvas` reads the laid-out height.

`details.tableview`, `code`, `pre.raw`, `footer`.

## FFXITheme

Canvas can't use CSS custom properties. Before this module existed, each app kept
a hand-maintained copy of the palette in its chart code and the two drifted. Now
everything reads the live computed value off `:root`, so the stylesheet is the
single source of truth and a theme swap needs no JS palette at all.

```js
FFXITheme.v('--blade')     // one token, resolved, with a fallback
FFXITheme.chart()          // { surface, grid, axis, ink, ink2, muted, blade, brass, ... }
FFXITheme.series(slot)     // categorical slot 0..7
FFXITheme.liveColors()     // same set, but every key is a getter that re-reads
FFXITheme.bind({ button, storageKey, onChange })   // wire a light/dark toggle
FFXITheme.flush()          // drop the cache if a stylesheet is swapped at runtime
```

Reads are cached per theme, so calling `chart()` once per draw is cheap.

`bind()` restores a stored choice **without** firing `onChange` — restoring is not
a change — so a caller can safely repaint from `onChange` without it running
before the app has anything to paint.

Classic script on `window.FFXITheme`, never an ES module: ws_calculator pages are
opened off `file://`, where modules are CORS-blocked.
