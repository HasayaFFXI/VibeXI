# OverlaySpike: the Phase 0 proof of concept

**Code:** [`../spike/OverlaySpike/`](../spike/OverlaySpike/)
**Status:** done, 2026-10-02. Throwaway: not in `Zerg.slnx`, not shipped, never
built by the publish step.

## Why it exists

Before writing any of Zerg, one question could have changed the whole design:
**can a WebView2 page in a WinForms window be a usable overlay over FFXI?**
WebView2 renders through its own composition path, and Windows' layered-window
tricks (window opacity, colour keys) are known to misbehave with it. If they
didn't work, Zerg would have needed WPF or a different hosting mode from the
start. The spike answers that in an afternoon instead of discovering it after
Phase 3 (`../PLAN.md`, Phase 0).

It tests five things, all at once, in one small window:

| # | Question | Mechanism under test |
|---|---|---|
| 1 | Does WebView2 still render, and keep repainting, when the window is translucent? | `Form.Opacity` < 1 (makes the window `WS_EX_LAYERED`) |
| 2 | Does the window stay above the game? | `Form.TopMost` |
| 3 | Can the background vanish while the panels stay solid? | `Form.TransparencyKey` + transparent WebView2 background + transparent page body |
| 4 | Can clicks pass through to the game? | `WS_EX_TRANSPARENT \| WS_EX_LAYERED` |
| 5 | Can it be frameless and still be moved? | `FormBorderStyle.None` + a native drag strip |

## Result

**All five passed**, checked by the user over FFXI running in windowed mode.
That settled WinForms as the host (`../PLAN.md`, Decision 1).

One correction learned later, in Phase 3: check 3 passed because the spike's
page made its **body transparent**, so the pixels being colour-keyed were the
*form's own background* showing through. A colour key never drops pixels that
WebView2 itself draws (it composites like Chrome). The real pop-outs had to copy
the spike exactly (transparent page, key-coloured form behind it) rather than
paint the key colour in CSS. See `../CLAUDE.md`, "Transparency".

## Files

```
spike/OverlaySpike/
  OverlaySpike.csproj   WinForms exe, net10.0-windows, x64, WebView2 1.0.4258.31
  Program.cs            starts SpikeForm
  SpikeForm.cs          the window, every knob, and the Win32 calls
  spike.html            the test page: a fake meter, the controls, a live state readout
```

## Running it

```bash
dotnet build apps/zerg/spike/OverlaySpike
apps/zerg/spike/OverlaySpike/bin/Debug/net10.0-windows/OverlaySpike.exe
```

Run FFXI in **windowed or borderless** mode. Nothing can draw over exclusive
fullscreen. The window opens at (120, 120), 440×560, always on top.

The build shows one MSB3277 warning (WindowsBase 4.0 vs 5.0). It comes from the
WebView2 package referencing its WPF assembly. It's harmless here; the real
`Zerg.csproj` removes that reference.

## What's on screen

The window has two parts:

- **A grey grip strip** (14 px) across the top. It's a native WinForms panel,
  not part of the page. Drag it to move the window; right-click it for the menu.
  Because it's native, it keeps working even if the page fails to render, or
  after "Open real meter" replaces the test page.
- **The test page** (`spike.html`), in three cards:
  1. **Fake meter.** A clock ticking every 100 ms and five bars changing every
     500 ms. If a translucent window stops repainting, this freezes visibly.
     Small and tiny text samples are there to judge readability at low opacity.
  2. **Controls.** An opacity slider (10–100%) and checkboxes for keyed
     background, frameless, click-through and always-on-top. There's also an
     **Open real meter** button, which loads `http://localhost:8731/`. That
     needs `apps/damage-meter/damage-meter.py` running.
  3. **What the window really is.** The host reads the window's actual state
     back from Win32 and shows it, rather than echoing its own flags: the real
     `Form.Opacity`, the extended style bits (layered, transparent), topmost,
     DPI, whether the hotkey registered, and the WebView2 runtime version. The
     gap between "what we asked for" and "what Windows did" is the experiment.

**The right-click menu** on the grip offers the same controls plus a few more:
- Test page / Real meter
- Opacity presets (100, 85, 70, 55, 40, 25%)
- Keyed background, Frameless, Click-through, Always on top
- Exit

**Ctrl+Alt+Z** toggles click-through from anywhere. It's a global hotkey
because once click-through is on, nothing in the window can be clicked.

## How each knob works (`SpikeForm.cs`)

- **Opacity:** `Form.Opacity = pct / 100`. Below 1.0, WinForms makes the window
  layered and sets a uniform alpha.
- **Keyed background:** the form's `BackColor` and `TransparencyKey` are both
  set to `#010203` (a colour nothing draws by accident), and the WebView2's
  `DefaultBackgroundColor` is set to transparent. The page then switches its
  `body` background to transparent (`body.keyed`). Wherever the page is
  transparent, the form's key colour shows through, and Windows drops those
  pixels. The cards keep their own backgrounds, so they stay solid.
- **Click-through:** `WS_EX_TRANSPARENT | WS_EX_LAYERED` via
  `SetWindowLongPtr`, also added in `CreateParams` so a style refresh doesn't
  strip it. A window must be layered for this to work, and WinForms only layers
  below 100% (or with a key), so opacity is held at 99% while click-through is on.
- **Frameless:** `FormBorderStyle.None`. The grip strip moves the window with
  `ReleaseCapture` + `WM_NCLBUTTONDOWN` / `HTCAPTION`, which makes Windows
  treat the drag as a title-bar drag. To resize, turn the frame back on.
- **Page ↔ host:** the page sends `{cmd, value|on}` with
  `chrome.webview.postMessage`; the host applies it and posts the window's real
  state back (`type: "state"`), which fills the readout card.

WebView2's profile goes to `%LOCALAPPDATA%\VibeXI\zerg-spike\WebView2`, separate
from Zerg's own. The page is served from the exe's folder through
`SetVirtualHostNameToFolderMapping` at `https://spike.zerg/`.

## What carried into Zerg

- Uniform opacity and `TopMost` on a WinForms window holding WebView2:
  `PopoutForm`.
- The colour-key arrangement (transparent page over a key-coloured form):
  `PopoutForm` plus the style that `host.js` injects into each pop-out.
- Click-through and frameless were proven but **not built yet**. They're on the
  ideas list in `../CLAUDE.md` and in N8 of `../NATIVE-PLAN.md`.

Under the native WPF plan (`../NATIVE-PLAN.md`), panels use
`AllowsTransparency` windows with real per-pixel alpha, so none of these
workarounds will be needed once N4 lands. The spike can be deleted then.
