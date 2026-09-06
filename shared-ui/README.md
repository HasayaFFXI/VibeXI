# shared-ui

The design system both FFXI apps render with. One palette, one set of primitives,
one place to change them.

```
css/ffxi-theme.css   tokens + every shared primitive
js/theme.js          window.FFXITheme -- the same tokens, for <canvas>
```

This lives at the repo root, a sibling of `apps/` rather than inside it: it is
not an app, and both apps under `apps/` reach it by relative path, so its
position is load-bearing. See the paths below.

The look is [`../apps/ws-calculator`](../apps/ws-calculator)'s: dark ink field, bone text, blade-red
and brass accents, Shippori Mincho headings, JetBrains Mono for anything numeric.
[`../apps/damage-meter`](../apps/damage-meter) was brought onto it rather than the other way round.

## How each app loads it

**apps/ws-calculator** — plain relative paths, because pages are opened straight
off disk; three levels up from `pages/`:

```html
<link rel="stylesheet" href="../../../shared-ui/css/ffxi-theme.css">
<link rel="stylesheet" href="../shared/css/theme.css">
...
<script src="../../../shared-ui/js/theme.js"></script>
```

**apps/damage-meter** — `damage-meter.py` resolves this directory as
`HERE.parent.parent / 'shared-ui'` and mounts it at the `/shared/` URL prefix, so
nothing has to be copied into `web/`:

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
ws-calculator never sets the attribute; damage-meter has a toggle.

Three families of color, kept apart on purpose:

- **Chrome** — `--ink`, `--surface`, `--surface2`, `--border`, `--bone`, `--mist`,
  `--faint`, `--dim`, `--blade`, `--brass`, `--good`, `--critical`. This is the
  identity. Each accent that can be used as a *fill* has a text colour graded
  against it — `--on-blade`, `--on-good`, `--on-brass` — because all three invert
  between the tiers (bright on dark, dark on light) and text picked for one tier
  is unreadable in the other. Never put `--bone` on a filled accent.
- **Series** — `--series-1` … `--series-18`, categorical slots in a fixed,
  accessibility-checked order. 1–8 are the reference eight (blue, orange, aqua,
  yellow, magenta, green, violet, red) and are not to be re-stepped; 9–18 are the
  extension tier, solved against them so no pair involving a new slot is weaker
  than the weakest pair already inside 1–8. These encode *data*, so they are
  deliberately not folded into the blade/brass identity. Assign a slot per entity,
  in order. Eighteen is past what colour alone can carry: a chart using the upper
  tier owes the reader a legend, labels or a table, and past eighteen the wrap
  repeats a hue.
- **Jobs** — `--job-war` … `--job-pup`, the colours Metra's Metrics addon paints
  FFXI jobs in, converted from its `Res.Colors.Jobs` and, in the dark tier,
  otherwise untouched. Their whole value is that a party already reads them at a
  glance in game, so re-stepping them for contrast would destroy the only
  property they have. They are **not** colourblind-separable — WAR, NIN, RDM and
  SAM are four reds — so use them only where the thing they colour is also named
  in text, and offer the series ramp as the way out. The light tier is a re-step,
  because Metrics paints onto the game's 3D scene and half of it vanishes on a
  pale card; hue is preserved, lightness moves. Four jobs (DNC, SCH, GEO, RUN)
  have no token at all, because Metrics never gave them one.

Both data families are graded for **marks** — dots, swatches, chart lines — not
for body text. A label next to a mark stays in `--bone` / `--mist`.

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
`.segmented` `.chips` / `.chip` — controls. `.segmented button:disabled` is an
option that is not available *yet*, as opposed to one that is merely off: it
keeps its slot, so a control does not change width as options come and go.

A `.segmented` group whose buttons set their own fill must not declare `color` on
its own `.segmented … button` rule — that selector outweighs both `:disabled` and
any state class, and silently wins them.

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
FFXITheme.series(slot)     // categorical slot 0..17, wrapping
FFXITheme.job('WAR')       // that job's colour, or '' if it has none
FFXITheme.step(color, k)   // the k-th variant of one colour; k = 0 is itself
FFXITheme.liveColors()     // same set, but every key is a getter that re-reads
FFXITheme.bind({ button, storageKey, onChange })   // wire a light/dark toggle
FFXITheme.flush()          // drop the cache if a stylesheet is swapped at runtime
```

Reads are cached per theme, so calling `chart()` once per draw is cheap.

`job()` returns the **empty string** for a job with no token rather than a
fallback hue, on purpose: the caller almost always has something better to fall
back to — a series slot for that entity — and a default here would take that
decision away from it.

`step()` exists for the one thing the job palette cannot do on its own: two
characters can be the same job, and two identical lines on a chart are not a
chart. Each step is a fixed lightness move, and the first one goes **away from
the page** — lighter in dark mode, darker in light — so a variant is never the
harder one to see. The series ramp never needs it.

`bind()` restores a stored choice **without** firing `onChange` — restoring is not
a change — so a caller can safely repaint from `onChange` without it running
before the app has anything to paint.

Classic script on `window.FFXITheme`, never an ES module: ws-calculator pages are
opened off `file://`, where modules are CORS-blocked.
