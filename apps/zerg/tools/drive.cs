// Drives the native Zerg window from a script, for checking a change in the
// running app: grab what is on screen, list and press controls through UI
// Automation. A .NET 10 file-based app -- no project, nothing to install:
//
//   dotnet run --file apps/zerg/tools/drive.cs -- front <target>
//   dotnet run --file apps/zerg/tools/drive.cs -- rect  <target>
//   dotnet run --file apps/zerg/tools/drive.cs -- snap  <target> <out.png>
//   dotnet run --file apps/zerg/tools/drive.cs -- names <target>
//   dotnet run --file apps/zerg/tools/drive.cs -- text  <target>
//   dotnet run --file apps/zerg/tools/drive.cs -- click <target> <name>
//   dotnet run --file apps/zerg/tools/drive.cs -- where <target> <name>
//   dotnet run --file apps/zerg/tools/drive.cs -- set   <target> <name> <number>
//   dotnet run --file apps/zerg/tools/drive.cs -- type  <target> <name> <text>
//   dotnet run --file apps/zerg/tools/drive.cs -- enter <target>
//   dotnet run --file apps/zerg/tools/drive.cs -- keys  <target> <text>
//   dotnet run --file apps/zerg/tools/drive.cs -- scroll <target> <percent>
//   dotnet run --file apps/zerg/tools/drive.cs -- hover <target> <x> <y>
//   dotnet run --file apps/zerg/tools/drive.cs -- press <target> <x> <y>
//   dotnet run --file apps/zerg/tools/drive.cs -- drag  <target> <x1> <y1> <x2> <y2>
//   dotnet run --file apps/zerg/tools/drive.cs -- windows <pid> [all]
//   dotnet run --file apps/zerg/tools/drive.cs -- fg
//   dotnet run --file apps/zerg/tools/drive.cs -- chord <keys>
//   dotnet run --file apps/zerg/tools/drive.cs -- at    <x> <y> [right]
//   dotnet run --file apps/zerg/tools/drive.cs -- tray  <target> <rect|pick|menu>
//   dotnet run --file apps/zerg/tools/drive.cs -- zoom  <in.png> <x> <y> <w> <h> <times> <out.png>
//
// <target> is a process id (its main window) or h<hwnd> for any other window,
// e.g. a dialog. `snap` copies the screen inside the window's frame, so it shows
// Mica and anything covering the window: run `front` first. `front` restores a
// maximized window. `text` prints everything on screen that has a name, table
// cells and list rows included, one per line in drawing order. `click` finds
// the first element with that UI Automation name that can be pressed (or the
// nearest such ancestor of the first match), and selects, toggles or invokes
// it, or folds an expander open or shut. A task dialog's buttons ignore
// Invoke: `front` it and send Enter instead. `set` gives a slider (or anything
// with a range) a value; `where` prints the middle of the first element with
// that name, in a grab's own pixels, for `hover`, `press` and `drag` (a drag
// may end outside the window it began in: that is how a file is dropped from
// an Explorer window onto Zerg); `type` gives a text field its text and `enter` brings
// a window to the front and presses Enter in it, which together work an Open
// dialog (`windows` finds it; its box is "File name:"). A Save dialog does not
// believe `type`: it shows the text and saves under the name it had. `keys`
// brings the window to the front and types the text as a keyboard would, into
// whatever has the focus, which in a new Save dialog is the name box with its
// suggestion selected. `scroll` moves the first area that scrolls up and
// down to that far down, 0 to 100. `hover` moves the real mouse pointer to a
// point given in a grab's own pixels (from the frame's top left), which is the
// only way to see a hover card or a tooltip as the mouse brings it up: it
// takes the pointer from the user, so say so. `press` clicks the real left
// button at such a point and `drag` holds it from one point to another, which
// is how a floating panel is checked: UI Automation presses a button without
// a click, so it cannot show whether a click takes the keyboard from the
// window in front, or whether a panel follows its bar. `windows` lists every
// visible top-level window of a process as h<hwnd> with its title, frame and
// whether it is a no-activate tool window: a floating panel is not the
// process's main window, so this is how to get its <target>. `fg` prints the
// window in front. `zoom` cuts a rectangle out of a grab and enlarges it
// without smoothing, to see whether an edge is crisp. `windows <pid> all`
// lists hidden windows too, marked: the main window minimized to the tray is
// one, and then <pid> no longer finds it. `chord` presses a key with its
// modifiers held ("Ctrl+Alt+Z"), to whatever has the keyboard: the way to
// press a global hot key. `at` clicks the real mouse at a point of the screen
// (not of a window), the right button if asked. `tray` is about the icon a
// window owns in the notification area: `rect` asks the shell where it is,
// in screen pixels, which also says whether there is one (an icon tucked
// away in the overflow has a place only while the overflow is open); `pick`
// and `menu` tell the window what the shell tells it for a click and for a
// right-click, without the shell.
//
// The process is per-monitor DPI aware, so frame rectangles and the screen
// agree on every monitor (PowerShell's own capture does not).
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
// File-based apps default to AOT, which WPF refuses (NETSDK1168).
#:property PublishAot=false
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

SetProcessDpiAwarenessContext(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
// A minus sign, a dash and a delta are not in the console's own code page,
// and `text` is compared with the reference character for character.
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
if (args.Length > 0 && args[0] == "fg")
{
    nint front = GetForegroundWindow();
    GetWindowThreadProcessId(front, out uint owner);
    Console.WriteLine($"h{front} pid {owner} '{TitleOf(front)}'");
    return 0;
}
if (args.Length > 1 && args[0] == "chord")
{
    // Modifiers down in order, the key down and up, modifiers up in reverse.
    var held = new List<byte>();
    foreach (var part in args[1].Split('+'))
    {
        var p = part.Trim().ToUpperInvariant();
        held.Add(p switch
        {
            "CTRL" => (byte)0x11,
            "ALT" => (byte)0x12,
            "SHIFT" => (byte)0x10,
            "WIN" => (byte)0x5B,
            _ when p.Length > 1 && p[0] == 'F' => (byte)(0x70 + int.Parse(p[1..]) - 1),
            _ => (byte)p[0],
        });
    }
    foreach (var vk in held)
    {
        keybd_event(vk, 0, 0, 0);
        Thread.Sleep(30);
    }
    held.Reverse();
    foreach (var vk in held)
    {
        keybd_event(vk, 0, 2, 0);
        Thread.Sleep(30);
    }
    Console.WriteLine("pressed " + args[1]);
    return 0;
}
if (args.Length > 2 && args[0] == "at")
{
    bool right = args.Length > 3 && args[3] == "right";
    SetCursorPos(int.Parse(args[1]), int.Parse(args[2]));
    Thread.Sleep(150);
    mouse_event(right ? 8u /* RIGHTDOWN */ : 2u /* LEFTDOWN */, 0, 0, 0, 0);
    Thread.Sleep(60);
    mouse_event(right ? 16u /* RIGHTUP */ : 4u /* LEFTUP */, 0, 0, 0, 0);
    Console.WriteLine("pressed");
    return 0;
}
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: drive.cs <front|rect|snap|names|text|click|where|set|type|enter|keys|scroll|hover|press|drag|tray|windows|fg|chord|at> <pid|h<hwnd>> [args]");
    return 2;
}
if (args[0] == "windows")
{
    uint pid = uint.Parse(args[1]);
    bool all = args.Length > 2 && args[2] == "all";
    EnumWindows((h, _) =>
    {
        GetWindowThreadProcessId(h, out uint p);
        bool visible = IsWindowVisible(h);
        // Hidden ones only when asked for, and then only those with a title:
        // every process has a crowd of nameless helper windows.
        if (p != pid || !(visible || (all && TitleOf(h).Length > 0))) return true;
        var f = Frame(h);
        long ex = GetWindowLongPtr(h, -20 /* GWL_EXSTYLE */).ToInt64();
        Console.WriteLine($"h{h} '{TitleOf(h)}' {f.L},{f.T} {f.R - f.L}x{f.B - f.T}" +
                          ((ex & 0x80) != 0 ? " tool" : "") + ((ex & 0x08000000) != 0 ? " noactivate" : "") +
                          ((ex & 0x8) != 0 ? " topmost" : "") + ((ex & 0x80000) != 0 ? " layered" : "") +
                          ((ex & 0x20) != 0 ? " clickthrough" : "") + (visible ? "" : " hidden"));
        return true;
    }, 0);
    return 0;
}
if (args[0] == "zoom")
{
    var src = new BitmapImage(new Uri(Path.GetFullPath(args[1])));
    var cut = new CroppedBitmap(src, new Int32Rect(int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5])));
    int times = int.Parse(args[6]), cw = cut.PixelWidth, ch = cut.PixelHeight;
    var from = new byte[cw * ch * 4];
    new FormatConvertedBitmap(cut, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(from, cw * 4, 0);
    var to = new byte[cw * times * ch * times * 4];
    for (int y = 0; y < ch * times; y++)
        for (int x = 0; x < cw * times; x++)
            Buffer.BlockCopy(from, (y / times * cw + x / times) * 4, to, (y * cw * times + x) * 4, 4);
    var big = BitmapSource.Create(cw * times, ch * times, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, to, cw * times * 4);
    var png = new PngBitmapEncoder();
    png.Frames.Add(BitmapFrame.Create(big));
    using var output = File.Create(args[7]);
    png.Save(output);
    Console.WriteLine($"{args[7]} {cw * times}x{ch * times}");
    return 0;
}
nint hwnd = args[1].StartsWith('h')
    ? nint.Parse(args[1][1..])
    : Process.GetProcessById(int.Parse(args[1])).MainWindowHandle;

switch (args[0])
{
    case "front":
        ShowWindow(hwnd, 9 /* SW_RESTORE */);
        // Tapping Alt lets SetForegroundWindow through the foreground lock. Not
        // when the window is in front already: the tap would go to it, open its
        // system menu, and UI Automation sees only the frame until that closes.
        if (GetForegroundWindow() != hwnd)
        {
            keybd_event(0x12, 0, 0, 0);
            keybd_event(0x12, 0, 2, 0);
        }
        Console.WriteLine(SetForegroundWindow(hwnd));
        break;

    case "rect":
        var r = Frame(hwnd);
        Console.WriteLine($"{r.L},{r.T} {r.R - r.L}x{r.B - r.T}");
        break;

    case "snap":
        Snap(hwnd, args[2]);
        break;

    case "names":
        foreach (AutomationElement e in AutomationElement.FromHandle(hwnd)
                     .FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition))
        {
            try { Console.WriteLine($"{e.Current.ControlType.ProgrammaticName} '{e.Current.Name}'"); }
            catch (ElementNotAvailableException) { }
        }
        break;

    case "text":
        // Everything with a name, in the order it is drawn, including text
        // inside list rows and table cells: those are left out of the tree
        // `names` walks, because a screen reader reads them through their row.
        Dump(AutomationElement.FromHandle(hwnd));
        break;

    case "click":
        // A list row and the button inside it can carry the same name: take
        // the first match that can itself be pressed, and only then the first
        // match's nearest pressable ancestor.
        AutomationElement? el = null;
        var named = new PropertyCondition(AutomationElement.NameProperty, args[2]);
        foreach (AutomationElement match in AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, named))
        {
            if (!Pressable(match)) continue;
            el = match;
            break;
        }
        if (el is null)
        {
            el = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, named);
            while (el is not null && !Pressable(el)) el = TreeWalker.ControlViewWalker.GetParent(el);
        }
        if (el is null)
        {
            Console.Error.WriteLine("not found: " + args[2]);
            return 1;
        }
        if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sel)) ((SelectionItemPattern)sel).Select();
        else if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var tog)) ((TogglePattern)tog).Toggle();
        else if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var inv)) ((InvokePattern)inv).Invoke();
        else
        {
            var fold = (ExpandCollapsePattern)el.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            if (fold.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) fold.Expand();
            else fold.Collapse();
        }
        Console.WriteLine("pressed");
        break;

    case "where":
    {
        var found = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, args[2]));
        if (found is null)
        {
            Console.Error.WriteLine("not found: " + args[2]);
            return 1;
        }
        var spot = found.Current.BoundingRectangle;
        var f = Frame(hwnd);
        Console.WriteLine($"{(int)(spot.X + spot.Width / 2) - f.L} {(int)(spot.Y + spot.Height / 2) - f.T}");
        break;
    }

    case "set":
        // A slider's label often carries the same name; take the one with a range.
        var ranged = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, args[2]),
            new PropertyCondition(AutomationElement.IsRangeValuePatternAvailableProperty, true)));
        if (ranged is null || !ranged.TryGetCurrentPattern(RangeValuePattern.Pattern, out var range))
        {
            Console.Error.WriteLine("no range named " + args[2]);
            return 1;
        }
        ((RangeValuePattern)range).SetValue(double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine("set");
        break;

    case "type":
        // A label often carries the same name as its box, and so does the
        // list a box drops down (a Save dialog's is read-only): take the
        // first one that holds a value and can be written to.
        ValuePattern? box = null;
        foreach (AutomationElement field in AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, new AndCondition(
                     new PropertyCondition(AutomationElement.NameProperty, args[2]),
                     new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true))))
        {
            var value = (ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern);
            if (value.Current.IsReadOnly) continue;
            box = value;
            break;
        }
        if (box is null)
        {
            Console.Error.WriteLine("no text field named " + args[2]);
            return 1;
        }
        box.SetValue(args[3]);
        Console.WriteLine("typed");
        break;

    case "enter":
        if (GetForegroundWindow() != hwnd)
        {
            keybd_event(0x12, 0, 0, 0);
            keybd_event(0x12, 0, 2, 0);
            SetForegroundWindow(hwnd);
            Thread.Sleep(200);
        }
        keybd_event(0x0D, 0, 0, 0);
        keybd_event(0x0D, 0, 2, 0);
        Console.WriteLine("entered");
        break;

    case "keys":
    {
        if (GetForegroundWindow() != hwnd)
        {
            keybd_event(0x12, 0, 0, 0);
            keybd_event(0x12, 0, 2, 0);
            SetForegroundWindow(hwnd);
            Thread.Sleep(300);
        }
        var strokes = new List<Input>();
        foreach (char ch in args[2])
        {
            strokes.Add(new Input { Type = 1, Scan = ch, Flags = 4 /* KEYEVENTF_UNICODE */ });
            strokes.Add(new Input { Type = 1, Scan = ch, Flags = 4 | 2 /* KEYUP */ });
        }
        Console.WriteLine(SendInput((uint)strokes.Count, strokes.ToArray(), Marshal.SizeOf<Input>()) == strokes.Count ? "keyed" : "blocked");
        break;
    }

    case "scroll":
        // The first area that can actually move up and down: a list that
        // fits, or one that only scrolls sideways, is not what was meant.
        ScrollPattern? scroller = null;
        foreach (AutomationElement area in AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true)))
        {
            var pattern = (ScrollPattern)area.GetCurrentPattern(ScrollPattern.Pattern);
            if (!pattern.Current.VerticallyScrollable) continue;
            scroller = pattern;
            break;
        }
        if (scroller is null)
        {
            Console.Error.WriteLine("nothing scrolls");
            return 1;
        }
        scroller.SetScrollPercent(ScrollPattern.NoScroll, double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine("scrolled");
        break;

    case "hover":
        var at = Frame(hwnd);
        Console.WriteLine(SetCursorPos(at.L + int.Parse(args[2]), at.T + int.Parse(args[3])));
        break;

    case "press":
    {
        var f = Frame(hwnd);
        SetCursorPos(f.L + int.Parse(args[2]), f.T + int.Parse(args[3]));
        Thread.Sleep(120);
        mouse_event(2 /* LEFTDOWN */, 0, 0, 0, 0);
        Thread.Sleep(60);
        mouse_event(4 /* LEFTUP */, 0, 0, 0, 0);
        Console.WriteLine("pressed");
        break;
    }

    case "drag":
    {
        // In steps, as a hand moves: a window being dragged is told about
        // every one, and a single jump would not show whether it follows.
        var f = Frame(hwnd);
        int x1 = f.L + int.Parse(args[2]), y1 = f.T + int.Parse(args[3]);
        int x2 = f.L + int.Parse(args[4]), y2 = f.T + int.Parse(args[5]);
        SetCursorPos(x1, y1);
        Thread.Sleep(150);
        mouse_event(2 /* LEFTDOWN */, 0, 0, 0, 0);
        Thread.Sleep(120);
        const int steps = 16;
        for (int i = 1; i <= steps; i++)
        {
            SetCursorPos(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
            // A relative nudge as well: a move loop listens for mouse input,
            // and SetCursorPos alone does not always count as any.
            mouse_event(1 /* MOVE */, 0, 0, 0, 0);
            Thread.Sleep(25);
        }
        // Where the window is while the button is still down.
        var mid = Frame(hwnd);
        Console.WriteLine($"held: {mid.L},{mid.T} {mid.R - mid.L}x{mid.B - mid.T}");
        Thread.Sleep(120);
        mouse_event(4 /* LEFTUP */, 0, 0, 0, 0);
        Thread.Sleep(150);
        var end = Frame(hwnd);
        Console.WriteLine($"released: {end.L},{end.T} {end.R - end.L}x{end.B - end.T}");
        break;
    }

    case "tray":
    {
        // Zerg's icon is number 1 of its window, reporting with WM_APP + 1.
        const uint id = 1, callback = 0x8001;
        if (args[2] == "rect")
        {
            var which = new IconId { Size = (uint)Marshal.SizeOf<IconId>(), Window = hwnd, Id = id };
            int hr = Shell_NotifyIconGetRect(ref which, out Rect spot);
            if (hr != 0)
            {
                Console.Error.WriteLine($"no icon (0x{hr:X8})");
                return 1;
            }
            Console.WriteLine($"{spot.L},{spot.T} {spot.R - spot.L}x{spot.B - spot.T}");
            break;
        }
        // What happened in the low word, which icon in the high one.
        nint what = args[2] == "menu" ? 0x7B /* WM_CONTEXTMENU */ : 0x400 /* NIN_SELECT */;
        Console.WriteLine(PostMessage(hwnd, callback, 0, what | (nint)(id << 16)) ? "told" : "refused");
        break;
    }

    default:
        Console.Error.WriteLine("unknown command " + args[0]);
        return 2;
}
return 0;

static Rect Frame(nint hwnd)
{
    DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out Rect r, 16);
    return r;
}

static string TitleOf(nint hwnd)
{
    var text = new char[256];
    return new string(text, 0, GetWindowText(hwnd, text, text.Length));
}

static bool Pressable(AutomationElement e) =>
    e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _) || e.TryGetCurrentPattern(TogglePattern.Pattern, out _)
    || e.TryGetCurrentPattern(InvokePattern.Pattern, out _) || e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);

static void Dump(AutomationElement e)
{
    try
    {
        if (e.Current.Name is { Length: > 0 } name) Console.WriteLine(name);
    }
    catch (ElementNotAvailableException) { return; }
    for (var c = TreeWalker.RawViewWalker.GetFirstChild(e); c is not null; c = TreeWalker.RawViewWalker.GetNextSibling(c))
        Dump(c);
}

static void Snap(nint hwnd, string path)
{
    var r = Frame(hwnd);
    int w = r.R - r.L, h = r.B - r.T;
    nint screen = GetDC(0), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
    nint old = SelectObject(mem, bmp);
    // CAPTUREBLT, so a layered window (a floating panel) is in the copy.
    BitBlt(mem, 0, 0, w, h, screen, r.L, r.T, 0x00CC0020 /* SRCCOPY */ | 0x40000000 /* CAPTUREBLT */);
    SelectObject(mem, old);
    try
    {
        var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var f = File.Create(path);
        enc.Save(f);
    }
    finally
    {
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(0, screen);
    }
    Console.WriteLine($"{path} {w}x{h} at {r.L},{r.T}");
}

[DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(nint value);
[DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
[DllImport("user32.dll")] static extern nint GetForegroundWindow();
[DllImport("user32.dll")] static extern bool ShowWindow(nint hwnd, int cmd);
[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, nint extra);
[DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
[DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nint extra);
[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, nint lParam);
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
[DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hwnd, [Out] char[] text, int max);
[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
[DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostMessage(nint hwnd, uint msg, nint wp, nint lp);
[DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref IconId id, out Rect rect);
[DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd, int attr, out Rect rect, int size);
[DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
[DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint dc);
[DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
[DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
[DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
[DllImport("gdi32.dll")] static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
[DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
[DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);

struct Rect { public int L, T, R, B; }

/// NOTIFYICONIDENTIFIER: an icon by the window that owns it and its number.
[StructLayout(LayoutKind.Sequential)]
struct IconId
{
    public uint Size;
    public nint Window;
    public uint Id;
    public Guid Guid;
}

/// One keyboard event for SendInput: the 64-bit INPUT with its KEYBDINPUT.
[StructLayout(LayoutKind.Explicit, Size = 40)]
struct Input
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public ushort Vk;
    [FieldOffset(10)] public ushort Scan;
    [FieldOffset(12)] public uint Flags;
    [FieldOffset(16)] public uint Time;
    [FieldOffset(24)] public nint Extra;
}
delegate bool EnumProc(nint hwnd, nint lParam);
