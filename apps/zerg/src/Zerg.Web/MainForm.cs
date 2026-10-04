using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Zerg;

/// <summary>
/// The meter's main window: one WebView2 showing damage-meter/web, with every
/// request answered by <see cref="HostRouter"/>, and the owner of every
/// floating <see cref="PopoutForm"/>.
/// </summary>
sealed class MainForm : Form
{
    // The page's dark background, so the window does not flash white while
    // WebView2 starts.
    static readonly Color Backdrop = Color.FromArgb(20, 20, 26);

    readonly Options options;
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Backdrop };
    readonly List<PopoutForm> popouts = [];
    CoreWebView2Environment? env;
    HostRouter? router;

    public MainForm(Options options)
    {
        this.options = options;
        Text = $"{AppInfo.Name} {AppInfo.Version}";
        BackColor = Backdrop;
        MinimumSize = new Size(480, 360);
        ClientSize = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(web);

        WindowPlacement.Restore(this, "main");
        FormClosing += (_, _) =>
        {
            WindowPlacement.Remember(this, "main");
            // Each one remembers its own placement as it closes.
            foreach (var p in popouts.ToArray()) p.Close();
        };
        Load += async (_, _) => await InitAsync();
    }

    async Task InitAsync()
    {
        try
        {
            env = await CoreWebView2Environment.CreateAsync(null, AppInfo.WebViewDataDir);
            Log.Write($"webview2 runtime {env.BrowserVersionString}, data {AppInfo.WebViewDataDir}");
            await web.EnsureCoreWebView2Async(env);
            Log.Write("webview2 ready");
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException)
        {
            Log.Write("webview2 failed: " + e);
            MessageBox.Show(this, "WebView2 could not start:\n\n" + e.Message, AppInfo.Name,
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = web.CoreWebView2;
        Configure(core);

        IAssets assets = options.WebRoot is null
            ? new EmbeddedAssets()
            : new DiskAssets(options.WebRoot, options.SharedRoot!);
        router = new HostRouter(env, assets, options.EventsDir) { Alpha = ApplyAlpha };
        router.Attach(core);

        await core.AddScriptToExecuteOnDocumentCreatedAsync(HostScript());

        core.DocumentTitleChanged += (_, _) =>
            Text = $"{core.DocumentTitle} — {AppInfo.Name} {AppInfo.Version}" +
                   (options.WebRoot is null ? "" : "  [web from disk]");
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationCompleted += (_, e) =>
            Log.Write(e.IsSuccess ? "page loaded" : $"page failed: {e.WebErrorStatus} (HTTP {e.HttpStatusCode})");

        Log.Write($"navigating to {HostRouter.Origin}, assets {assets.Describe}");
        core.Navigate(HostRouter.Origin);
    }

    /// <summary>Settings every view gets, the main one and each pop-out.</summary>
    public static void Configure(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = AppInfo.Debug;     // F12 in Debug builds only
        core.Settings.IsStatusBarEnabled = false;
        // SmartScreen sends every top-level URL to Microsoft for a reputation
        // verdict. The only pages these views show are our own, served from
        // memory, and anything external is handed to the player's browser -- so
        // the check protects nothing here. It also cost 2 s on every launch:
        // zerg.vibexi has no reputation to look up, and the lookup times out.
        core.Settings.IsReputationCheckingRequired = false;
    }

    /// <summary>The main view stays on the meter. Anything else that tries to
    /// navigate it (a link, a stray redirect) opens in the player's browser.</summary>
    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Links.IsOurs(e.Uri)) return;
        e.Cancel = true;
        Links.OpenExternally(e.Uri);
    }

    /// <summary>
    /// window.open(). A pop-out (about:blank, from popout.js) becomes a native
    /// always-on-top <see cref="PopoutForm"/>; anything else is an external link.
    ///
    /// Setting <c>e.NewWindow</c> is what makes the form's WebView2 *be* the
    /// window the page got back from window.open(), opener and all. It must be
    /// set before that view navigates anywhere, and the view has to be created
    /// asynchronously -- hence the deferral.
    /// </summary>
    async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!Links.IsOurs(e.Uri))
        {
            e.Handled = true;
            Links.OpenExternally(e.Uri);
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var f = e.WindowFeatures;
            Size? size = f.HasSize ? new Size((int)f.Width, (int)f.Height) : null;
            var name = string.IsNullOrEmpty(e.Name) ? "popout" : e.Name;

            var form = new PopoutForm(name, size, this, popouts.Count);
            popouts.Add(form);
            form.FormClosed += (_, _) => popouts.Remove(form);
            form.Show();
            await form.InitAsync(env!, router!);

            e.NewWindow = form.Core;
            e.Handled = true;
            Log.Write($"pop-out {name} opened ({form.ClientSize.Width}x{form.ClientSize.Height})");
        }
        catch (Exception ex)
        {
            // Unhandled, the page gets WebView2's default popup instead: not on
            // top, but the panel still works.
            Log.Write("pop-out failed: " + ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// /api/alpha. popout.js names its window by document.title (the main page's
    /// own title never matches: the pop-out titles end in "— Damage Meter" and
    /// carry the panel name). Returns the matched title, or null for "no such
    /// window yet", which popout.js retries once before falling back to a fade.
    /// </summary>
    string? ApplyAlpha(string title, int percent, Color? key)
    {
        var form = popouts.FirstOrDefault(p => p.DocumentTitle == title);
        if (form is null) return null;
        form.ApplyAlpha(percent, key);
        return form.DocumentTitle;
    }

    static string HostScript()
    {
        using var s = typeof(MainForm).Assembly.GetManifestResourceStream("host.js")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
