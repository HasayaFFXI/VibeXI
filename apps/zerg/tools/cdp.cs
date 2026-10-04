// Minimal Chrome DevTools Protocol client for checking Zerg's WebView2 from a
// script: read page state, click things, screenshot, watch the network.
// A .NET 10 file-based app -- no project, nothing to install:
//
//   dotnet run apps/zerg/tools/cdp.cs -- <port> list
//   dotnet run apps/zerg/tools/cdp.cs -- <port> eval <urlFilter> "<js expression>"
//   dotnet run apps/zerg/tools/cdp.cs -- <port> shot <urlFilter> <out.png>
//   dotnet run apps/zerg/tools/cdp.cs -- <port> netlog <urlFilter> <url> <seconds>
//
// Zerg has to be started with the port open, for that run only:
//   WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333
// <urlFilter> picks the target by URL substring: "zerg.vibexi" is the main page.
// `eval` awaits promises and returns by value. `shot` is the page's own render,
// NOT the screen: it cannot show colour keying, opacity or the tint window.
//
// File-based apps have reflection JSON switched off, hence JsonObject throughout.
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var port = args[0];
var cmd = args[1];
using var http = new HttpClient();
var targets = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json"))!.AsArray();

if (cmd == "list")
{
    foreach (var t in targets) Console.WriteLine($"{t!["type"]}\t{t["title"]}\t{t["url"]}");
    return;
}

var filter = args[2];
var target = targets.First(t => t!["type"]!.GetValue<string>() == "page" &&
                                t["url"]!.GetValue<string>().Contains(filter));
using var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri(target!["webSocketDebuggerUrl"]!.GetValue<string>()), default);
int nextId = 0;

async Task<JsonNode> Call(string method, JsonObject prms)
{
    int id = ++nextId;
    var msg = Encoding.UTF8.GetBytes(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = prms }.ToJsonString());
    await ws.SendAsync(msg, WebSocketMessageType.Text, true, default);
    var buf = new byte[1 << 20];
    while (true)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do { r = await ws.ReceiveAsync(buf, default); ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
        var node = JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray()))!;
        if (node["id"]?.GetValue<int>() == id) return node;
    }
}

if (cmd == "netlog")
{
    // netlog <filter> <url> <seconds>: navigate and print every Network/Log event.
    await Call("Network.enable", new JsonObject());
    await Call("Log.enable", new JsonObject());
    var start = DateTime.UtcNow;
    int nav = ++nextId;
    await ws.SendAsync(Encoding.UTF8.GetBytes(new JsonObject { ["id"] = nav, ["method"] = "Page.navigate",
        ["params"] = new JsonObject { ["url"] = args[3] } }.ToJsonString()), WebSocketMessageType.Text, true, default);
    var until = DateTime.UtcNow.AddSeconds(int.Parse(args[4]));
    var buf = new byte[1 << 20];
    while (DateTime.UtcNow < until)
    {
        using var cts = new CancellationTokenSource(until - DateTime.UtcNow);
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        try { do { r = await ws.ReceiveAsync(buf, cts.Token); ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage); }
        catch (OperationCanceledException) { break; }
        var node = JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray()))!;
        var m = node["method"]?.GetValue<string>();
        if (m is null) { if (node["id"]?.GetValue<int>() == nav) Console.WriteLine("navigate -> " + node["result"]?.ToJsonString()); continue; }
        if (m is "Network.dataReceived") continue;
        var p = node["params"]!;
        var s = m switch
        {
            "Network.requestWillBeSent" => $"{p["request"]?["url"]}",
            "Network.responseReceived" => $"{p["response"]?["status"]} {p["response"]?["url"]} headers={p["response"]?["headers"]?.ToJsonString()}",
            "Network.loadingFailed" => $"{p["errorText"]} canceled={p["canceled"]} blocked={p["blockedReason"]} cors={p["corsErrorStatus"]?.ToJsonString()}",
            "Log.entryAdded" => $"{p["entry"]?["level"]} {p["entry"]?["text"]}",
            _ => "",
        };
        Console.WriteLine($"{(int)(DateTime.UtcNow - start).TotalMilliseconds,6} ms  {m}: {s}");
    }
}
else if (cmd == "eval")
{
    var res = await Call("Runtime.evaluate", new JsonObject { ["expression"] = args[3], ["awaitPromise"] = true, ["returnByValue"] = true });
    Console.WriteLine(res["result"]?["result"]?["value"]?.ToJsonString() ?? res.ToJsonString());
}
else if (cmd == "shot")
{
    var res = await Call("Page.captureScreenshot", new JsonObject { ["format"] = "png" });
    File.WriteAllBytes(args[3], Convert.FromBase64String(res["result"]!["data"]!.GetValue<string>()));
    Console.WriteLine("saved " + args[3]);
}
