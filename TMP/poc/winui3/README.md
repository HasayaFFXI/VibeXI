# WinUI 3 overlay spike

The OverlaySpike checks from `../SPIKE.md`, redone in native WinUI 3 / XAML
(no WebView2, no HTML). Throwaway; not in `Zerg.slnx`.

Windows App SDK 2.5.1, .NET 10, **unpackaged** (`WindowsPackageType=None`) and
**Windows App SDK self-contained**, so the exe runs from `bin` with no MSIX
install and no separate runtime installer (the .NET 10 desktop runtime is still
needed).

## Build and run

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build apps/zerg/poc/winui3
dotnet build-server shutdown
apps/zerg/poc/winui3/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUiSpike.exe
```

Start-up options, for scripted checks: `--opacity N`, `--method 0|1|2`
(XAML whole window / XAML background tint / Win32 `SetLayeredWindowAttributes`),
`--transparent`, `--frameless`, `--clickthrough`, `--notopmost`, `--pos X,Y`,
`--dwm blur|extend|none` (default `blur`), `--nobackdrop`.

## How transparency works here

WinUI 3 windows in this SDK still have a GDI redirection bitmap
(`WS_EX_NOREDIRECTIONBITMAP` is off), so a transparent `SystemBackdrop` alone
shows **black**, and `DwmExtendFrameIntoClientArea(-1)` shows **white**. What
works is the old trick: `DwmEnableBlurBehindWindow` with an empty region (DWM
honours the alpha channel but blurs nothing), plus a custom `SystemBackdrop`
whose brush is fully transparent (`TransparentBackdrop.cs`), plus a transparent
`Root` background. Then the gaps between cards are see-through and the cards
stay solid.

Ctrl+Alt+Z toggles click-through. If another spike (WPF/Electron) is running
it may already own the hotkey; the readout says so.
