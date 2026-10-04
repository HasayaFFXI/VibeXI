# WPF overlay spike

The five OverlaySpike checks (`../SPIKE.md`) redone in native WPF/XAML (.NET 10),
with no WebView2 and no HTML. Throwaway: not part of `Zerg.slnx`.

## Build and run

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build apps/zerg/poc/wpf
dotnet build-server shutdown
apps/zerg/poc/wpf/bin/Debug/net10.0-windows/win-x64/WpfOverlayPoc.exe
```

Options (for scripted testing): `--frameless`, `--transparent` (implies
frameless), `--opacity N`, `--click-through`, `--not-topmost`,
`--hotkey Q` (use Ctrl+Alt+Q if another app already owns Ctrl+Alt+Z).

## What it does

- Grey strip at the top: drag to move (`DragMove`), right-click for the menu.
- Fake meter: clock every 100 ms, five data-bound bars every 500 ms (animated 250 ms).
- Controls: opacity 10-100%, transparent background, frameless, click-through, always on top.
- Readout: ex-style bits, alpha (LWA or per-pixel), DPI, hotkey, CPU, memory, startup time,
  all read back from Win32 twice a second.

Two window shapes, because WPF only does per-pixel alpha with
`AllowsTransparency=True`, which needs `WindowStyle=None` and can't change after
the window is shown:

- **Frameless**: `AllowsTransparency`, per-pixel alpha, `Window.Opacity`.
- **Framed**: an ordinary window; opacity and click-through layer it with
  `SetLayeredWindowAttributes`. A transparent background turns frameless on.

Toggling the frame recreates the window and moves the same view into it.

---

# Spikes S1 and S2 (NATIVE-PLAN.md, phase S)

Run 2026-10-02 on this machine: .NET 10.0.12 WPF, Windows 11 Home, monitor 1
3440×1440 at 100 %, monitor 2 2560×1600 at 150 % (both refresh at ~120 Hz:
WPF's `Rendering` fired 115–131 times a second). Each spike is its own project
in a subfolder, excluded from `WpfOverlayPoc.csproj`. Throwaway, not in
`Zerg.slnx`.

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build apps/zerg/poc/wpf/S1Fluent
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build apps/zerg/poc/wpf/S2Chart
dotnet build-server shutdown
```

## S1: Fluent theme and text

`S1Fluent/FluentSpike.exe`: `Application.ThemeMode` (WPF0001 suppressed), a
command bar, a gallery of every stock control Zerg needs, live tiles, text
samples, and a readout of what Windows actually applied.

Options: `--theme Light|Dark|System`, `--monitor N` (1 = primary),
`--force-mica`, `--snap <dir>` (screen-grabs every tab plus the menus and
popups in Light, Dark and System, then exits), `--report <file>`.

**Result: solid enough. Use it.**

- **Mica comes free.** With `ThemeMode` set, every window gets
  `DWMWA_SYSTEMBACKDROP_TYPE = 2` (Mica), a transparent `Window.Background`,
  and `DWMWA_USE_IMMERSIVE_DARK_MODE` following the theme (0 light, 1 dark).
  No DWM calls of our own are needed for the main window; `--force-mica` was
  never necessary. Corners are rounded by Windows' default.
- **Switching at runtime works**, Light → Dark → System, with the theme
  dictionary, backdrop and caption all following within one frame. System
  follows `AppsUseLightTheme` (dark on this machine).
- **Per-monitor DPI works.** Opened on the 150 % monitor, WPF reported scale
  1.5 and laid out at 1752×1191 px for the same 1166×793 DIP window; text and
  controls are crisp there.
- **Stock controls that are fine as they are:** Button, the accent button
  (`AccentButtonStyle`, follows the system accent `#0078D4`), ToggleButton,
  CheckBox (incl. three-state), Slider, ComboBox and its drop-down, TextBox,
  ProgressBar, Expander, Menu, ContextMenu, ToolTip, scroll bars (thin,
  auto-expanding). Card surfaces from `CardBackgroundFillColorDefaultBrush` /
  `CardStrokeColorDefaultBrush` read well on Mica in both themes.
- **Stock controls that need Zerg's own style:**
  - `RadioButton` has a large minimum width (~120 DIP each), so a row of them
    is far too spread out for Light / Dark / System or by Character / by Job.
    Segmented controls want a styled `ToggleButton` group or `ListBox`.
  - `TabControl` headers are plain text with a faint selected box: too weak
    for the Damage | Healing | Compare switch. WPF has no `NavigationView`
    (only a few of its brush keys exist in the theme). Build the section
    switch as a styled selector.
  - `ListBox` rows are ~45 DIP tall. Confirms the plan: tables are
    `ItemsControl` + `Grid.IsSharedSizeScope`, not `ListBox`/`DataGrid`.
- **The theme's own font is Segoe UI 14, not Segoe UI Variable.** Zerg has to
  set the family itself.
- **"Segoe UI Variable" is not a family WPF can see.** WPF exposes the
  optical sizes as three families, `Segoe UI Variable Display`, `… Text` and
  `… Small`, each with Light, Semilight (350), Normal, SemiBold and Bold.
  Asking for plain `Segoe UI Variable` silently falls back to Segoe UI. Use
  `Text` for body and table text, `Display` for tile figures, each with
  `Segoe UI` as the fallback.
- **Tabular figures must be asked for.** Segoe UI Variable's default digits
  are proportional (14 px: `1111111` 36.7 DIP vs `0000000` 52.8);
  `Typography.NumeralAlignment="Tabular"` makes them equal (52.8 / 52.8), at
  14 px Normal and at 28 px SemiBold. Even Segoe UI's SemiBold face is
  proportional by default (78.9 vs 108.8 at 28 px). Side by side at 4×/s, the
  default row visibly shifts as `1`s come and go; the tabular row doesn't.
  So the plan's rule stands, and applies to every live number, whatever the
  family.
- **Cascadia Mono and Cascadia Code are available** to WPF (57.4 DIP per
  seven digits, tabular by nature), though not in `C:\Windows\Fonts`.
  Consolas is the fallback.
- **Segoe Fluent Icons is installed**; `Segoe MDL2 Assets` is a fallback with
  the same code points.
- **Text formatting:** `Ideal` (the default) is right. `Display` is a little
  crisper at 9–12 px on the 100 % monitor, but at 150 % its glyph spacing
  goes uneven and lines run wider than `Ideal`'s, which is as sharp there
  anyway. Nothing in Zerg should be under
  11 px. On bare Mica, `Auto` rendering already falls back to grayscale; no
  per-element `TextRenderingMode` is needed.
- First frame 1.15–1.23 s after process start (Debug, including enumerating
  all 535 installed fonts for the readout); 226 MB working set for the same
  reason.

## S2: chart cost

`S2Chart/ChartSpike.exe`: `chart.js`'s cumulative `line()` on three
`DrawingVisual` layers (series; grid, labels and markers; crosshair and hover
card), with nice ticks, time ticks, end labels nudged apart, the dashed
"N others" group, the hover card, and the 18-slot palette. Data comes from
`S2Chart/gen-alliance.py`: 18 characters, 30 minutes of swings ending at the
moment it runs (19,118 lines), binned exactly as `stats.cumulative` bins
(~370 points per series).

```bash
python apps/zerg/poc/wpf/S2Chart/gen-alliance.py --out <dir>/Hasaya_2026.10.02.jsonl
ChartSpike.exe --file <file> --strategy rebuild --hz 10 --hover --cache \
  --size 1419x320 --at 3433,-165 --seconds 20 --warmup 5 --out results.jsonl
```

Two ways of moving the live edge were built:
- **rebuild**: every edge update rebuilds every series' `StreamGeometry`
  (frozen) in pixels, as `chart.js` redraws its canvas.
- **retained**: series geometry is built once per data change in data units
  under one shared `MatrixTransform`; an edge update only changes that
  transform, 18 flat tail segments and the furniture layer.

`--cache` puts a `BitmapCache` on the series layer.

**The baseline.** The web build (`dist/Zerg/Zerg.exe`, WebView2) on the same
file, with the session armed at the first event and grouping off, so 18 lines.
Damage section, chart 1419×320 CSS px on the 150 % monitor. CPU is the whole
process tree (Zerg.exe plus 6 WebView2 processes), measured over 20 s. The
crosshair is a synthetic `mousemove` per animation frame (~120/s), sweeping
the plot every 4 s; the page's 10 Hz edge tick keeps running under it.

| | CPU, % of one core | Working set |
|---|---|---|
| **Web**: session running, 10 Hz edge + 4 Hz clock + polls | **24.4** | 533 MB |
| **Web**: the same + crosshair every frame | **137.7** | 619 MB |

**WPF**, same size and place (2129×480 px), chart only, Debug build:

| Strategy | Edge updates | Crosshair | CPU, % of one core | UI-thread draw, ms/s |
|---|---|---|---|---|
| rebuild | 4 Hz | — | **5.1** | 4.8 |
| rebuild | 10 Hz | — | **13.7** | 8.3 |
| rebuild | every frame (~115/s) | — | 102.7 | 85.1 |
| retained | every frame | — | 105.0 | 52.0 |
| rebuild | 10 Hz | every frame | 80.2 | 135.5 |
| rebuild, cached | 10 Hz | every frame | **52.7** | 151.8 |
| rebuild, cached | 4 Hz | every frame | **50.2** | 146.2 |
| retained | every frame | every frame | 124.3 | 143.7 |

**Result: the DrawingVisual chart stays well under the web build, on one
condition: the live edge must not move every frame.**

- At the web build's own cadence (10 Hz) the chart costs 13.7 % of a core
  against the web app's 24.4 %; at 4 Hz, with the clock, 5.1 %. With a
  crosshair moving every frame, 50–53 % against 137.7 %.
- **Animating the edge every frame costs a whole core** at 120 Hz, in either
  strategy. Most of it is WPF's render thread re-tessellating 18 polylines
  of ~370 points each frame (the UI thread's share is under 10 %).
  *Retained* doesn't help: changing a geometry's transform re-tessellates it
  just the same, so it saves UI-thread time and nothing else. It is also not
  worth having: on a 30-minute axis one 250 ms step is 0.3 px. **So the edge
  steps with the 4×/s clock tick**, which also keeps the chart, the Elapsed
  tile and every DPS cell moving together. This changes the plan's "live edge
  animated from `CompositionTarget.Rendering`"; NATIVE-PLAN.md is updated.
  If the first seconds of a pull look steppy in N2 (a 0.25 s step on a 30 s
  axis is ~1.7 % of the width), the cheap fix is stepping faster while the
  session is young, not per-frame drawing.
- **`BitmapCache` on the series layer** cuts the crosshair's cost by a third
  (80 → 53 %): the overlay redraws over a cached bitmap instead of making the
  render thread re-tessellate every line under the card. It renders as
  crisply as uncached at 150 %. Keep it on the series layer only; the layers
  that change every tick gain nothing from it.
- The crosshair numbers are a worst case: a real pointer moves only on mouse
  events, and the overlay need only redraw when the hovered grid index
  changes, which the sweep forces on almost every frame.
- Caveats: the web figures are the whole app (also the clock tick, DOM text
  updates and polls), the WPF figures are the chart alone in a Debug build;
  DWM's composition cost is outside both. Memory is not comparable either
  (210–265 MB for the WPF spike, 533–619 MB for the WebView2 tree).
- For N2: in a floating-size window the hover card for 18 rows is taller than
  the chart and gets cut off (the web build makes its card denser there); the
  card also needs to be opaque, as end labels show through it at 95 %.
