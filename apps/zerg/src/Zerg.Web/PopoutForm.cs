using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Zerg;

/// <summary>
/// One floating panel. popout.js calls window.open(); MainForm answers with one
/// of these, and its WebView2 becomes that window -- so window.opener and
/// same-origin scripting hold, which popout.js needs because it MOVES the card's
/// DOM in here with adoptNode.
///
/// Always on top, off the taskbar. In keyed mode (popout.js's default) the
/// page background is punched out completely and a <see cref="TintForm"/>
/// behind the panel supplies the darkening, at the slider's value, while the
/// panel stays fully opaque -- dark tint, solid text. Without the key the slider
/// fades the whole window instead, as in Chrome.
/// </summary>
sealed class PopoutForm : Form
{
    static readonly Color Backdrop = Color.FromArgb(20, 20, 26);

    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Backdrop };
    readonly TintForm tint = new();

    /// <summary>The window.open() name, "dpsPanel_line" etc. Stable across a
    /// drill-down retitling the window, so it keys the remembered placement.</summary>
    public string WindowName { get; }

    public CoreWebView2 Core => web.CoreWebView2;

    /// <summary>What popout.js identifies the window by in /api/alpha.</summary>
    public string DocumentTitle => web.CoreWebView2?.DocumentTitle ?? "";

    string PlacementKey => "popout:" + WindowName;

    public PopoutForm(string windowName, Size? contentSize, Form near, int cascade)
    {
        WindowName = windowName;
        Text = "Damage Meter";
        TopMost = true;
        ShowInTaskbar = false;
        // A thin caption with a close box, resizable, kept out of Alt+Tab: this is
        // a panel over a game, not an application window.
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        BackColor = Backdrop;
        MinimumSize = new Size(200, 100);
        StartPosition = FormStartPosition.Manual;
        Controls.Add(web);

        // window.open sizes are CSS pixels; the form is per-monitor DPI aware.
        double scale = DeviceDpi / 96.0;
        ClientSize = contentSize is { } s
            ? new Size((int)(s.Width * scale), (int)(s.Height * scale))
            : new Size((int)(460 * scale), (int)(320 * scale));

        if (!WindowPlacement.Restore(this, PlacementKey))
        {
            // First time for this panel: near the meter's top-right, cascaded so
            // two new panels do not open exactly on top of each other.
            var step = (int)(28 * scale) * cascade;
            Location = new Point(near.Right - Width - (int)(24 * scale) - step, near.Top + (int)(96 * scale) + step);
        }
        FormClosing += (_, _) => WindowPlacement.Remember(this, PlacementKey);

        // Owned by the tint, so it always sits directly above it; the tint
        // follows the panel's client area wherever the panel goes.
        Owner = tint;
        Move += (_, _) => SyncTint();
        Resize += (_, _) => SyncTint();
        FormClosed += (_, _) => tint.Close();
    }

    void SyncTint()
    {
        if (tint.Visible && WindowState == FormWindowState.Normal)
            tint.Bounds = RectangleToScreen(ClientRectangle);
    }

    public async Task InitAsync(CoreWebView2Environment env, HostRouter router)
    {
        await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2;
        MainForm.Configure(core);
        // The child is about:blank, but popout.js links the stylesheets by their
        // resolved https://zerg.vibexi/ hrefs, and those requests come from THIS
        // view -- so it needs the router too, or the panel loads unstyled.
        router.Attach(core);

        core.DocumentTitleChanged += (_, _) => Text = core.DocumentTitle;
        // popout.js docks a panel by calling win.close().
        core.WindowCloseRequested += (_, _) => Close();
        core.NavigationStarting += (_, e) =>
        {
            if (Links.IsOurs(e.Uri)) return;
            e.Cancel = true;
            Links.OpenExternally(e.Uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Links.OpenExternally(e.Uri);
        };
    }

    /// <summary>
    /// The /api/alpha request: the slider (10..100) and a colour key or none.
    ///
    /// With the key: the page background is transparent (host.js), and the
    /// form's background is the key colour, so it is dropped completely. The
    /// slider then sets the TINT's darkness, and the panel stays at 100% --
    /// the text, bars and lines are never faded.
    ///
    /// Without it (DPS.popout.keyBg(key, false), the escape hatch): no tint, and
    /// the slider fades the whole window, background and text alike.
    /// </summary>
    public void ApplyAlpha(int percent, Color? key)
    {
        double value = Math.Clamp(percent, 10, 100) / 100.0;
        if (key is { } k)
        {
            BackColor = k;
            TransparencyKey = k;
            web.DefaultBackgroundColor = Color.Transparent;
            Opacity = 1.0;
            tint.SetDarkness(value);
            if (!tint.Visible) tint.Show();
            SyncTint();
        }
        else
        {
            tint.Hide();
            TransparencyKey = Color.Empty;
            BackColor = Backdrop;
            web.DefaultBackgroundColor = Backdrop;
            Opacity = value;
        }
    }
}
