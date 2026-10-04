using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Zerg.Core;

namespace Zerg;

/// <summary>
/// Answers every request the page makes to https://zerg.vibexi/, in process.
/// This is what replaces damage-meter.py's HTTP server: same URLs, same
/// response bodies, no socket, no port, no firewall prompt.
/// </summary>
sealed class HostRouter(CoreWebView2Environment env, IAssets assets, string eventsDir)
{
    public const string Host = "zerg.vibexi";
    public const string Origin = "https://" + Host + "/";

    sealed record Reply(int Status, string Reason, string ContentType, byte[] Body)
    {
        public static Reply Json(byte[] body, int status = 200, string reason = "OK") =>
            new(status, reason, "application/json; charset=utf-8", body);

        public static Reply Json(object value, int status = 200, string reason = "OK") =>
            Json(JsonSerializer.SerializeToUtf8Bytes(value), status, reason);

        public static Reply Text(int status, string reason) =>
            new(status, reason, "text/plain; charset=utf-8", Encoding.ASCII.GetBytes($"{status} {reason}"));
    }

    /// <summary>
    /// Applies a pop-out's opacity (10..100) and optional colour key; returns
    /// the window it landed on, or null if no window has that title. Set by
    /// MainForm, which owns the pop-outs.
    /// </summary>
    public Func<string, int, Color?, string?>? Alpha { get; init; }

    /// <summary>
    /// /api/alpha, same query and reply shape as damage-meter.py's. Zerg owns
    /// its windows, so it finds one by title alone: x, y, w, h and dpr are what
    /// the Python server needs to find a Chrome window it does not own, and are
    /// accepted and ignored here. The key, which Chrome never honoured, works.
    /// </summary>
    Reply AlphaReply(string query)
    {
        var q = Query.Parse(query);
        var title = Query.First(q, "title") ?? "";
        var percent = (int)Math.Clamp(Query.Int(Query.First(q, "value"), 100), 10, 100);

        // RRGGBB, as popout.js sends it.
        Color? key = null;
        var raw = Query.First(q, "key") ?? "";
        if (raw.Length == 6 && int.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            key = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        var hit = Alpha?.Invoke(title, percent, key);
        if (AppInfo.Debug) Log.Write($"alpha '{title}' {percent}% key={raw} -> {(hit is null ? "no window" : "applied")}");
        return Reply.Json(new
        {
            ok = true,
            applied = hit is null ? 0 : 1,
            supported = Alpha is not null,
            method = hit is null ? "" : key is null ? "form" : "form+key",
            window = hit ?? "",
        });
    }

    public void Attach(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnRequest;
    }

    void OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        if (e.Request.Method == "GET" && uri.AbsolutePath == "/api/events")
        {
            _ = EventsAsync(e, uri.Query);
            return;
        }

        // Everything else is in memory or tiny, so it is answered synchronously,
        // without a deferral.
        Reply reply;
        try { reply = Route(e.Request.Method, uri); }
        catch (Exception ex) { reply = Failed(e, ex); }
        if (AppInfo.Debug) Log.Write($"{reply.Status} {e.Request.Uri} ({reply.Body.Length} bytes)");
        e.Response = Respond(reply);
    }

    /// <summary>
    /// The event file can be up to 8 MiB a read, so it is read off the UI
    /// thread under a deferral. The response itself must be created on the UI
    /// thread; `await` returns there because this event is raised there.
    /// Completed exactly once, in the finally: disposing a deferral also
    /// completes it, so `using` plus Complete() throws 0x8000000E.
    /// </summary>
    async Task EventsAsync(CoreWebView2WebResourceRequestedEventArgs e, string query)
    {
        var deferral = e.GetDeferral();
        try
        {
            Reply reply;
            try { reply = Reply.Json(await Task.Run(() => EventsApi.FromQuery(eventsDir, query).ToJson())); }
            catch (Exception ex) { reply = Failed(e, ex); }
            e.Response = Respond(reply);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>The poll loop must survive whatever one request does -- same
    /// contract as the Python handler's catch-all.</summary>
    static Reply Failed(CoreWebView2WebResourceRequestedEventArgs e, Exception ex)
    {
        Log.Write($"{e.Request.Uri}: {ex}");
        return Reply.Json(new { ok = false, error = ex.Message }, 500, "Internal Server Error");
    }

    CoreWebView2WebResourceResponse Respond(Reply reply) =>
        env.CreateWebResourceResponse(
            new MemoryStream(reply.Body), reply.Status, reply.Reason,
            $"Content-Type: {reply.ContentType}\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate");

    Reply Route(string method, Uri uri)
    {
        if (method != "GET") return Reply.Text(405, "Method Not Allowed");

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        switch (path)
        {
            case "/api/alpha":
                return AlphaReply(uri.Query);

            case "/api/version":
                return Reply.Json(new { ok = true, version = AppInfo.Version });
        }

        var body = assets.Get(path);
        // The type comes from the file the path resolves to, not the URL: "/"
        // is index.html, and served as octet-stream it becomes a download.
        return body is null
            ? Reply.Text(404, "Not Found")
            : new Reply(200, "OK", AssetPath.MimeOf(AssetPath.Split(path).Rel), body);
    }
}
