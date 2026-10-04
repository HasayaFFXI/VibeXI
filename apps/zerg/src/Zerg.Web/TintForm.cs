namespace Zerg;

/// <summary>
/// The darkening behind a keyed pop-out. A colour key is all-or-nothing per
/// pixel, so a keyed panel's background can only be fully gone -- it cannot be
/// half dark. This window supplies the half: plain black at a uniform alpha,
/// sized to the panel's client area and kept directly beneath it (the panel is
/// OWNED by it, and an owned window always sits above its owner). The panel
/// itself stays at 100%, so its text and bars are solid at any darkness.
///
/// Click-through and never activated: clicks land on whatever is behind it --
/// in practice the panel's own see-through pixels pass straight on to the game.
/// </summary>
sealed class TintForm : Form
{
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;

    public TintForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // Layered from birth (see CreateParams), so it must never sit at exactly
        // 1.0: WinForms drops its layered attributes there, and a layered window
        // without them is not drawn at all.
        Opacity = 0.5;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>0 = clear, 1 = black.</summary>
    public void SetDarkness(double darkness) => Opacity = Math.Clamp(darkness, 0.0, 0.99);
}
