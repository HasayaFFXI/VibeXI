using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OverlaySpike;

/// <summary>
/// Phase 0 of apps/zerg/PLAN.md. Three questions, one window:
///   1. Does WebView2 still render when the form is layered (Opacity &lt; 1)?
///   2. Does a TopMost form stay above FFXI in windowed / borderless mode?
///   3. Does a colour-keyed form background plus a transparent WebView2
///      background let the game show through between panels?
/// Plus click-through (WS_EX_TRANSPARENT), toggled by Ctrl+Alt+Z because once
/// it is on, nothing in the window can be clicked.
///
/// Every control exists twice: in the page (spike.html, via postMessage) and on
/// the native grip strip's right-click menu. The native one still works when the
/// page is the real meter, or if WebView2 does not render at all.
/// </summary>
sealed class SpikeForm : Form
{
    const string Host = "spike.zerg";
    const string TestUrl = "https://" + Host + "/spike.html";
    const string MeterUrl = "http://localhost:8731/";

    // A colour nothing draws by accident, so only the deliberately keyed
    // background disappears.
    static readonly Color Key = Color.FromArgb(1, 2, 3);
    static readonly Color Plain = Color.FromArgb(20, 20, 26);

    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly Panel grip = new()
    {
        Dock = DockStyle.Top,
        Height = 14,
        Cursor = Cursors.SizeAll,
        BackColor = Color.FromArgb(70, 70, 86),
    };
    readonly ContextMenuStrip menu = new();

    int opacityPct = 100;
    bool keyed, clickThrough, frameless, hotkeyOk;

    public SpikeForm()
    {
        Text = "Zerg overlay spike";
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(120, 120, 440, 560);
        BackColor = Plain;

        Controls.Add(web);
        Controls.Add(grip);            // added last, so it docks first and takes the top

        grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        };
        BuildMenu();
        grip.ContextMenuStrip = menu;

        Load += async (_, _) => await InitAsync();
    }

    async Task InitAsync()
    {
        var data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VibeXI", "zerg-spike", "WebView2");
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "The WebView2 runtime is not installed.", Text);
            Close();
            return;
        }

        var core = web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(Host, AppContext.BaseDirectory,
            CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnMessage;
        core.NavigationCompleted += (_, _) => Report();
        core.Navigate(TestUrl);
    }

    // ------------------------------------------------------------- the knobs

    void SetOpacity(int pct)
    {
        opacityPct = Math.Clamp(pct, 10, 100);
        ApplyOpacity();
    }

    void ApplyOpacity()
    {
        // Click-through needs a layered window, and WinForms only makes the form
        // layered below 100% (or with a key) -- so hold it a hair under while it's on.
        double o = opacityPct / 100.0;
        if (clickThrough && !keyed && o >= 1.0) o = 0.99;
        Opacity = o;
        ApplyExStyle();
    }

    void SetKeyed(bool on)
    {
        keyed = on;
        if (on)
        {
            BackColor = Key;
            TransparencyKey = Key;
            web.DefaultBackgroundColor = Color.Transparent;
        }
        else
        {
            TransparencyKey = Color.Empty;
            BackColor = Plain;
            web.DefaultBackgroundColor = Color.White;
        }
        ApplyOpacity();
    }

    void SetFrameless(bool on)
    {
        frameless = on;
        FormBorderStyle = on ? FormBorderStyle.None : FormBorderStyle.Sizable;
        ApplyExStyle();     // a style change can rebuild the extended style
    }

    void SetClickThrough(bool on)
    {
        clickThrough = on;
        ApplyOpacity();
    }

    void ApplyExStyle()
    {
        if (!IsHandleCreated) return;
        long ex = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
        ex = clickThrough ? ex | WS_EX_TRANSPARENT | WS_EX_LAYERED : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, (nint)ex);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (clickThrough) cp.ExStyle |= (int)(WS_EX_TRANSPARENT | WS_EX_LAYERED);
            return cp;
        }
    }

    // --------------------------------------------------------- page <-> host

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var m = doc.RootElement;
        bool On() => m.TryGetProperty("on", out var v) && v.GetBoolean();

        switch (m.GetProperty("cmd").GetString())
        {
            case "opacity": SetOpacity(m.GetProperty("value").GetInt32()); break;
            case "keyed": SetKeyed(On()); break;
            case "frameless": SetFrameless(On()); break;
            case "clickThrough": SetClickThrough(On()); break;
            case "topmost": TopMost = On(); break;
            case "meter": web.CoreWebView2.Navigate(MeterUrl); break;
        }
        Report();
    }

    /// <summary>What the window actually is right now, read back from Win32
    /// rather than from our own flags -- that difference is the experiment.</summary>
    void Report()
    {
        long ex = IsHandleCreated ? GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64() : 0;
        Text = $"Zerg spike - {opacityPct}%" + (keyed ? " keyed" : "") +
               (clickThrough ? " click-through" : "") + (TopMost ? "" : " (not on top)");

        if (web.CoreWebView2 is null) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "state",
            opacity = opacityPct,
            formOpacity = Opacity,
            keyed,
            clickThrough,
            frameless,
            topmost = TopMost,
            exStyle = "0x" + ex.ToString("X8"),
            layered = (ex & WS_EX_LAYERED) != 0,
            transparent = (ex & WS_EX_TRANSPARENT) != 0,
            dpi = DeviceDpi,
            hotkey = hotkeyOk,
            runtime = web.CoreWebView2.Environment.BrowserVersionString,
        }));
    }

    void BuildMenu()
    {
        menu.Items.Add("Test page", null, (_, _) => web.CoreWebView2?.Navigate(TestUrl));
        menu.Items.Add("Real meter (needs damage-meter.py running)", null,
            (_, _) => web.CoreWebView2?.Navigate(MeterUrl));
        menu.Items.Add(new ToolStripSeparator());

        var op = new ToolStripMenuItem("Opacity");
        foreach (var pct in new[] { 100, 85, 70, 55, 40, 25 })
            op.DropDownItems.Add(pct + "%", null, (_, _) => { SetOpacity(pct); Report(); });
        menu.Items.Add(op);

        var keyedItem = new ToolStripMenuItem("Keyed background", null,
            (_, _) => { SetKeyed(!keyed); Report(); });
        var framelessItem = new ToolStripMenuItem("Frameless", null,
            (_, _) => { SetFrameless(!frameless); Report(); });
        var clickItem = new ToolStripMenuItem("Click-through  (Ctrl+Alt+Z)", null,
            (_, _) => { SetClickThrough(!clickThrough); Report(); });
        var topItem = new ToolStripMenuItem("Always on top", null,
            (_, _) => { TopMost = !TopMost; Report(); });
        menu.Items.AddRange(new ToolStripItem[] { keyedItem, framelessItem, clickItem, topItem });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());

        menu.Opening += (_, _) =>
        {
            keyedItem.Checked = keyed;
            framelessItem.Checked = frameless;
            clickItem.Checked = clickThrough;
            topItem.Checked = TopMost;
        };
    }

    // ----------------------------------------------------- hotkey (the way out)

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hotkeyOk = RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.Z);
        ApplyExStyle();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId)
        {
            SetClickThrough(!clickThrough);
            Report();
            return;
        }
        base.WndProc(ref m);
    }

    // ------------------------------------------------------------------ Win32

    const int GWL_EXSTYLE = -20;
    const long WS_EX_LAYERED = 0x00080000;
    const long WS_EX_TRANSPARENT = 0x00000020;
    const int WM_NCLBUTTONDOWN = 0x00A1;
    const int HTCAPTION = 2;
    const int WM_HOTKEY = 0x0312;
    const int HotkeyId = 0x5A47;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(nint hWnd, int id);
}
