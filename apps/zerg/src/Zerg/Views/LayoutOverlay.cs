using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Zerg.Core.Layout;

namespace Zerg.Views;

/// <summary>A pane that is being carried by its heading, as the overlay draws it.</summary>
/// <param name="Title">What the pane is called.</param>
/// <param name="Origin">Its place, which it keeps until it is put down.</param>
/// <param name="Sketch">A picture of the top of the pane as it was when
/// it was picked up, for the card that is carried.</param>
internal sealed record Carried(string Title, Rect Origin, ImageSource? Sketch);

/// <summary>The pane under the pointer while another is carried over it.</summary>
/// <param name="Title">What it is called.</param>
/// <param name="Box">Its place.</param>
/// <param name="Zones">The places on it (<see cref="PaneDrops.Zones"/>).</param>
internal sealed record Under(string Title, Rect Box, IReadOnlyList<DropZone> Zones);

/// <summary>
/// What is drawn over a section's panes while their arrangement is in
/// someone's hand, as the design's sheet 10 draws it. It lies over the
/// whole of a <see cref="SplitPanel"/>, takes no input, and at rest draws
/// nothing at all.
///
/// <para><b>A pane being carried</b> (sheet 10, A): its old place dimmed
/// and outlined, saying it is being moved and that Esc puts it back; the
/// pane under the pointer dimmed, with its places outlined and named, and
/// the one the pointer is in lit in the accent and saying in words what
/// letting go would do; a band along each edge of the section, which is a
/// place too; and a small card with the pane's heading and a sketch of it,
/// which follows the pointer.</para>
///
/// <para><b>A rule in the hand</b> (sheet 10, B): both sizes across it in
/// a small readout beside the pointer. The rule's own amber is the thumb's
/// (<see cref="DividerThumb"/>).</para>
///
/// <para>Two layers. What stands still between two places (the dims, the
/// outlines, the words) is drawn when the place under the pointer changes.
/// What follows the pointer (the card, the readout) is drawn once and then
/// only moved. Nothing here is drawn on a clock, and the sketch is a
/// picture taken once: a chart that goes on drawing itself under a carried
/// pane costs what it always costs and no more.</para>
/// </summary>
public sealed class LayoutOverlay : FrameworkElement
{
    readonly DrawingVisual places = new(), follower = new();

    Carried? carried;
    Under? under;
    PaneDrop drop = PaneDrop.Nowhere;
    string readout = "";
    Size followerSize;

    public LayoutOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        // The sketch on the card is a pane made a quarter of its size.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        AddVisualChild(places);
        AddVisualChild(follower);
    }

    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index == 0 ? places : follower;
    protected override Size MeasureOverride(Size available) => default;

    // ----------------------------------------------------------- what to draw

    /// <summary>Nothing is in anyone's hand any more.</summary>
    public void Clear()
    {
        if (carried is null && readout.Length == 0) return;
        carried = null;
        under = null;
        drop = PaneDrop.Nowhere;
        readout = "";
        using (places.RenderOpen()) { }
        using (follower.RenderOpen()) { }
    }

    /// <summary>A pane has been picked up.</summary>
    internal void Carry(Carried pane, Point pointer)
    {
        carried = pane;
        under = null;
        drop = PaneDrop.Nowhere;
        readout = "";
        DrawPlaces();
        DrawCard();
        Follow(pointer);
    }

    /// <summary>The pointer is somewhere else with the pane. The places are
    /// drawn again only if it is over another one of them.</summary>
    internal void Over(Under? target, PaneDrop place, Point pointer)
    {
        if (carried is null) return;
        // Not the records themselves: each call brings a new list of zones.
        if (target?.Box != under?.Box || target?.Title != under?.Title || place != drop)
        {
            under = target;
            drop = place;
            DrawPlaces();
        }
        Follow(pointer);
    }

    /// <summary>A rule is in the hand, its leading edge at <paramref name="edge"/>.</summary>
    internal void Hold(bool upright, double edge, Point pointer, string text)
    {
        if (text != readout)
        {
            readout = text;
            DrawReadout();
        }
        // Left of an upright rule and above a lying one, a little away
        // from the pointer; on the other side where there is no room.
        const double away = 14, rise = 34;
        double x, y;
        if (upright)
        {
            x = edge - away - followerSize.Width;
            if (x < 4) x = edge + away;
            y = pointer.Y - rise - followerSize.Height / 2;
        }
        else
        {
            y = edge - away - followerSize.Height;
            if (y < 4) y = edge + away;
            x = pointer.X - rise - followerSize.Width;
        }
        Put(x, y);
    }

    /// <summary>The card, a little down and right of the pointer.</summary>
    void Follow(Point pointer) => Put(pointer.X + 14, pointer.Y + 12);

    /// <summary>What follows the pointer, kept inside the panel: nothing
    /// is carried out of the section.</summary>
    void Put(double x, double y)
    {
        x = Math.Clamp(x, 0, Math.Max(0, ActualWidth - followerSize.Width));
        y = Math.Clamp(y, 0, Math.Max(0, ActualHeight - followerSize.Height));
        // On whole pixels, or its text is drawn soft.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        follower.Offset = new Vector(Math.Round(x * scale) / scale, Math.Round(y * scale) / scale);
    }

    // ---------------------------------------------------------------- drawing

    Brush Ink(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;

    FormattedText Words(string text, double size, Brush ink, bool strong = false, string font = "TextFont") =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TryFindResource(font) as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal,
                         strong ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    static Pen Dashes(Brush ink, double on, double off)
    {
        var pen = new Pen(ink, 1) { DashStyle = new DashStyle([on, off], 0) };
        pen.Freeze();
        return pen;
    }

    static Rect Of(Box box) => new(box.X, box.Y, Math.Max(0, box.Width), Math.Max(0, box.Height));

    /// <summary>Half a unit in, so a one-unit outline lies on whole pixels at 100%.</summary>
    static Rect Line(Rect box) => new(box.X + 0.5, box.Y + 0.5, Math.Max(0, box.Width - 1), Math.Max(0, box.Height - 1));

    static void Centre(DrawingContext dc, FormattedText text, Rect box, double y) =>
        dc.DrawText(text, new Point(Math.Round(box.X + (box.Width - text.Width) / 2), Math.Round(y)));

    void DrawPlaces()
    {
        using var dc = places.RenderOpen();
        if (carried is null) return;
        Brush dim = Ink("Bg0Brush"), line = Ink("Line3Brush"), accent = Ink("AccentBrush");
        Brush quiet = Ink("Text2Brush"), quieter = Ink("Text3Brush");
        double heading = TryFindResource("PaneHeadingHeight") is double h ? h : 30;

        // Where it came from: dimmed, outlined, and saying so.
        var origin = carried.Origin;
        bool tall = origin.Height > heading + 12;
        dc.PushOpacity(0.55);
        dc.DrawRectangle(dim, null, tall ? new Rect(origin.X, origin.Y, origin.Width, heading) : origin);
        dc.Pop();
        if (tall)
        {
            dc.PushOpacity(0.78);
            dc.DrawRectangle(dim, null, new Rect(origin.X, origin.Y + heading, origin.Width, origin.Height - heading));
            dc.Pop();
        }
        if (origin.Width > 12 && origin.Height > 12)
            dc.DrawRoundedRectangle(null, Dashes(line, 5, 4), Line(new Rect(origin.X + 4, origin.Y + 4, origin.Width - 8, origin.Height - 8)), 4, 4);
        var moving = Words(carried.Title + " is being moved", 12, quiet, strong: true);
        var back = Words("Esc puts it back", 11, quieter);
        moving.MaxTextWidth = back.MaxTextWidth = Math.Max(1, origin.Width - 24);
        moving.Trimming = back.Trimming = TextTrimming.CharacterEllipsis;
        moving.MaxLineCount = back.MaxLineCount = 1;
        if (origin.Width > 120 && origin.Height >= 70)
        {
            double top = origin.Y + (origin.Height - 34) / 2;
            Centre(dc, moving, origin, top);
            Centre(dc, back, origin, top + 19);
        }

        // The pane under the pointer, and its places.
        if (under is { } pane)
        {
            bool whole = pane.Zones.Count < 5;
            var body = whole ? pane.Box : new Rect(pane.Box.X, pane.Box.Y + heading, pane.Box.Width, Math.Max(0, pane.Box.Height - heading));
            dc.PushOpacity(0.84);
            dc.DrawRectangle(dim, null, body);
            dc.Pop();
            var dashes = Dashes(line, 3, 3);
            foreach (var zone in pane.Zones)
            {
                var box = Of(zone.Box);
                bool hot = drop.Target != null && drop.Kind == zone.Kind && (zone.Kind == DropKind.Swap || drop.Side == zone.Side);
                if (hot)
                {
                    Light(dc, box, zone.Kind == DropKind.Swap ? "Swap with " + pane.Title : Beside(zone.Side) + " " + pane.Title,
                          zone.Kind == DropKind.Swap ? "the two panes change places" : "the two panes share this room");
                    continue;
                }
                dc.DrawRoundedRectangle(null, dashes, Line(box), 3, 3);
                var label = Words(zone.Kind == DropKind.Swap ? "swap" : Beside(zone.Side).ToLowerInvariant(), 11, quiet);
                if (label.Width + 18 > box.Width || box.Height < 16) continue;
                // On a patch of the dim surface: the outline does not run through the word.
                double tag = Math.Min(20, box.Height);
                var patch = new Rect(Math.Round(box.X + (box.Width - label.Width - 18) / 2), Math.Round(box.Y + (box.Height - tag) / 2), label.Width + 18, tag);
                dc.DrawRoundedRectangle(dim, null, patch, 3, 3);
                Centre(dc, label, patch, patch.Y + (patch.Height - label.Height) / 2);
            }
        }

        // The section's four edges, each a place.
        var band = Ink("EdgeZoneBrush");
        double w = ActualWidth, t = PaneDrops.EdgeBand;
        double height = ActualHeight;
        dc.DrawRectangle(band, null, new Rect(0, 0, w, t));
        dc.DrawRectangle(band, null, new Rect(0, height - t, w, t));
        dc.DrawRectangle(band, null, new Rect(0, 0, t, height));
        dc.DrawRectangle(band, null, new Rect(w - t, 0, t, height));
        if (drop.Kind == DropKind.Edge)
        {
            bool across = drop.Side is PaneSide.Above or PaneSide.Below;
            Light(dc, Of(drop.Box), drop.Side switch
            {
                PaneSide.Above => "Along the top",
                PaneSide.Below => "Along the bottom",
                PaneSide.Left => "Down the left",
                _ => "Down the right",
            }, across ? "the full width of the section" : "the full height of the section");
        }

        void Light(DrawingContext to, Rect box, string title, string what)
        {
            to.DrawRoundedRectangle(dim, null, box, 4, 4);
            to.DrawRoundedRectangle(Ink("DropZoneFillBrush"), null, box, 4, 4);
            var edge = new Pen(accent, 1.5);
            edge.Freeze();
            to.DrawRoundedRectangle(null, edge, new Rect(box.X + 0.75, box.Y + 0.75, Math.Max(0, box.Width - 1.5), Math.Max(0, box.Height - 1.5)), 4, 4);
            // Two lines where there is room, the first alone where there
            // is not, and nothing in a place too small to read in: the
            // status line and the outline say it then.
            bool two = box.Height >= 48;
            var first = Words(title, two ? 13 : 11.5, accent, strong: true);
            first.MaxTextWidth = Math.Max(1, box.Width - 12);
            first.Trimming = TextTrimming.CharacterEllipsis;
            first.MaxLineCount = 1;
            if (box.Width < 60 || box.Height < 16) return;
            if (!two)
            {
                Centre(to, first, box, box.Y + (box.Height - first.Height) / 2);
                return;
            }
            var second = Words(what, 11, quiet);
            second.MaxTextWidth = first.MaxTextWidth;
            second.Trimming = TextTrimming.CharacterEllipsis;
            second.MaxLineCount = 1;
            double top = box.Y + (box.Height - 36) / 2;
            Centre(to, first, box, top);
            Centre(to, second, box, top + 20);
        }
    }

    static string Beside(PaneSide side) => side switch
    {
        PaneSide.Above => "Above",
        PaneSide.Below => "Below",
        PaneSide.Left => "Left of",
        _ => "Right of",
    };

    /// <summary>The card that is carried: 250 by 92, the pane's heading
    /// and a sketch of it, on the raised surface with an accent edge.</summary>
    void DrawCard()
    {
        const double width = 250, height = 92, head = 28;
        followerSize = new Size(width, height + 8);
        using var dc = follower.RenderOpen();
        if (carried is null) return;
        Shadow(dc, new Rect(0, 0, width, height), 5, 8);
        var box = new Rect(0, 0, width, height);
        dc.DrawRoundedRectangle(Ink("Bg2Brush"), null, box, 5, 5);
        // The heading, with its corners inside the card's.
        dc.PushClip(new RectangleGeometry(new Rect(1, 1, width - 2, height - 2), 4, 4));
        dc.DrawRectangle(Ink("Bg3Brush"), null, new Rect(1, 1, width - 2, head));
        // The picture at the card's width, as tall as that makes it: it was
        // cut to fit, and a short pane's (a stand-in, a heading) is short.
        if (carried.Sketch is { Width: > 0 } sketch)
            dc.DrawImage(sketch, new Rect(1, head + 1, width - 2, Math.Min(height - head - 2, (width - 2) * sketch.Height / sketch.Width)));
        dc.Pop();
        var ink = Ink("Text1Brush");
        for (int column = 0; column < 2; column++)
            for (int row = 0; row < 3; row++)
                dc.DrawEllipse(ink, null, new Point(10 + column * 4, 11 + row * 4), 1, 1);
        var title = Words(carried.Title, 12.5, ink, strong: true);
        title.MaxTextWidth = width - 34;
        title.Trimming = TextTrimming.CharacterEllipsis;
        title.MaxLineCount = 1;
        dc.DrawText(title, new Point(23, Math.Round((head + 2 - title.Height) / 2)));
        var edge = new Pen(Ink("AccentBrush"), 1);
        edge.Freeze();
        dc.DrawRoundedRectangle(null, edge, Line(box), 4.5, 4.5);
    }

    /// <summary>The readout: both sizes, in the face figures are set in.</summary>
    void DrawReadout()
    {
        using var dc = follower.RenderOpen();
        if (readout.Length == 0)
        {
            followerSize = default;
            return;
        }
        var text = Words(readout, 11.5, Ink("Text1Brush"), strong: true, font: "MonoFont");
        double width = Math.Ceiling(text.Width) + 56, height = 26;
        followerSize = new Size(width, height + 4);
        var box = new Rect(0, 0, width, height);
        Shadow(dc, box, 4, 4);
        dc.DrawRoundedRectangle(Ink("Bg4Brush"), null, box, 4, 4);
        var edge = new Pen(Ink("Line3Brush"), 1);
        edge.Freeze();
        dc.DrawRoundedRectangle(null, edge, Line(box), 3.5, 3.5);
        Centre(dc, text, box, (height - text.Height) / 2);
    }

    /// <summary>
    /// A shadow under something that follows the pointer: rings of plain
    /// black, each a little larger and fainter than the last, a few units
    /// down. Not an effect: an effect is worked out again whenever what it
    /// is on is drawn, and would draw the card's text through a bitmap.
    /// </summary>
    static void Shadow(DrawingContext dc, Rect box, double radius, double down)
    {
        for (int ring = 4; ring >= 1; ring--)
        {
            var shade = new SolidColorBrush(Color.FromArgb((byte)(40 - ring * 7), 0, 0, 0));
            shade.Freeze();
            var at = new Rect(box.X - ring, box.Y - ring + down, box.Width + 2 * ring, box.Height + 2 * ring - down / 2);
            dc.DrawRoundedRectangle(shade, null, at, radius + ring, radius + ring);
        }
    }
}
