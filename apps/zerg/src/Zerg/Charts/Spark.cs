using System.Windows;
using System.Windows.Media;
using Zerg.Core.Charts;

namespace Zerg.Charts;

/// <summary>How a <see cref="Spark"/> draws its numbers.</summary>
public enum SparkKind
{
    /// <summary>A thin bar per number, from the left, the tallest as tall as the mark.</summary>
    Bars,
    /// <summary>One line through the numbers, the whole mark wide, with a faint area under it.</summary>
    Line,
}

/// <summary>
/// A small mark beside a figure in the main window's band: a row of numbers
/// as thin bars, or as a line. Not a <see cref="Chart"/>: no axis, no label,
/// no hover card, nothing to read off it but its shape.
///
/// <para><b>It is redrawn when its numbers change and at no other time.</b>
/// The figure beside it is rewritten at the draw frequency; this is given
/// new numbers by a count, when an event arrives. A change while it cannot
/// be seen (its section is not on screen, the window is in the tray) is not
/// drawn until it can: WPF's render thread pays for a redraw nobody sees as
/// it does for one somebody does.</para>
/// </summary>
public sealed class Spark : FrameworkElement
{
    /// <summary>A bar's width, in units. From one bar to the next is the
    /// mark's width over the most bars it holds: 2.2 in a mark 96 wide.</summary>
    const double BarWidth = 1.2;

    /// <summary>The line's weight, and how strong the area under it is.</summary>
    const double LineWeight = 1.3, AreaStrength = 0.16;

    /// <summary>How strong the bars are.</summary>
    const double BarStrength = 0.75;

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values),
        typeof(IReadOnlyList<double>), typeof(Spark), new PropertyMetadata(null, (d, _) => ((Spark)d).Redraw()));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(SparkKind),
        typeof(Spark), new PropertyMetadata(SparkKind.Bars, (d, _) => ((Spark)d).Restyle()));

    /// <summary>The colour the mark is drawn in, before its strength.</summary>
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush),
        typeof(Spark), new PropertyMetadata(null, (d, _) => ((Spark)d).Restyle()));

    Brush? faint;
    Pen? pen;

    public Spark()
    {
        UseLayoutRounding = true;
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) InvalidateVisual();
        };
    }

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public SparkKind Kind { get => (SparkKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush? Ink { get => (Brush?)GetValue(InkProperty); set => SetValue(InkProperty, value); }

    void Redraw()
    {
        if (IsVisible) InvalidateVisual();
    }

    void Restyle()
    {
        faint = null;
        pen = null;
        Redraw();
    }

    /// <summary>The ink at a strength, frozen: made once per ink.</summary>
    Brush Faint(double strength)
    {
        if (faint != null) return faint;
        var b = (Ink ?? Brushes.Gray).Clone();
        b.Opacity *= strength;
        b.Freeze();
        return faint = b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = RenderSize.Width, h = RenderSize.Height;
        if (w < 1 || h < 1 || Values is not { Count: > 0 } values) return;

        double most = 0;
        foreach (var v in values)
            if (v > most && !double.IsInfinity(v)) most = v;
        if (!(most > 0)) return;

        if (Kind == SparkKind.Bars) Bars(dc, values, most, w, h);
        else Line(dc, values, most, w, h);
    }

    /// <summary>
    /// A bar per number from the left, at the pitch that fits as many bars
    /// as the mark ever holds into its width. A short session is a few bars
    /// at the left and fills the mark as it goes on. Each bar is on whole
    /// pixels, so it is sharp; the pitch is not a whole number of them, so
    /// the gaps differ by a pixel.
    /// </summary>
    void Bars(DrawingContext dc, IReadOnlyList<double> values, double most, double w, double h)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double pitch = w / Math.Max(BandMarks.MostBars, values.Count);
        double wide = Math.Max(1, Math.Round(BarWidth * scale)) / scale;
        var ink = Faint(BarStrength);

        for (int i = 0; i < values.Count; i++)
        {
            double v = values[i];
            if (!(v > 0)) continue;
            double x = Math.Round(i * pitch * scale) / scale;
            // Anything dealt at all is at least a pixel tall.
            double tall = Math.Max(1, Math.Round(Math.Min(v, most) / most * h * scale)) / scale;
            dc.DrawRectangle(ink, null, new Rect(x, h - tall, wide, tall));
        }
    }

    /// <summary>One line through the numbers, first at the left edge and
    /// last at the right, over zero at the bottom. A single number is a
    /// level line.</summary>
    void Line(DrawingContext dc, IReadOnlyList<double> values, double most, double w, double h)
    {
        int n = values.Count;
        double top = LineWeight / 2, tall = h - top;
        Point At(int i)
        {
            double v = values[i];
            if (!(v > 0) || double.IsInfinity(v)) v = 0;
            return new Point(n == 1 ? 0 : w * i / (n - 1), top + tall * (1 - Math.Min(v, most) / most));
        }

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var l = line.Open())
        using (var a = area.Open())
        {
            var first = At(0);
            l.BeginFigure(first, false, false);
            a.BeginFigure(new Point(0, h), true, true);
            a.LineTo(first, false, false);
            for (int i = 1; i < n; i++)
            {
                var p = At(i);
                l.LineTo(p, true, true);
                a.LineTo(p, false, false);
            }
            if (n == 1)
            {
                l.LineTo(new Point(w, first.Y), true, true);
                a.LineTo(new Point(w, first.Y), false, false);
            }
            a.LineTo(new Point(w, h), false, false);
        }
        line.Freeze();
        area.Freeze();

        dc.DrawGeometry(Faint(AreaStrength), null, area);
        if (pen == null)
        {
            pen = new Pen(Ink ?? Brushes.Gray, LineWeight) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
        }
        dc.DrawGeometry(null, pen, line);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();
}

/// <summary>
/// One short bar cut into a segment per character, each as long as their
/// share and in their colour, largest first as given: the party, end to
/// end. Where each segment goes is <see cref="BandMarks.Shares"/>; this
/// paints them, and like <see cref="Spark"/> only when what it is given
/// changes and it can be seen.
/// </summary>
public sealed class ShareBar : FrameworkElement
{
    /// <summary>The room between two segments, the shortest a segment is
    /// drawn, and the rounding of its corners, in units.</summary>
    const double Gap = 1, Least = 1, Corner = 1;

    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(nameof(Slices),
        typeof(IReadOnlyList<ShareSlice>), typeof(ShareBar), new PropertyMetadata(null, (d, _) => ((ShareBar)d).Redraw()));

    public ShareBar()
    {
        UseLayoutRounding = true;
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) InvalidateVisual();
        };
    }

    public IReadOnlyList<ShareSlice>? Slices
    {
        get => (IReadOnlyList<ShareSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    void Redraw()
    {
        if (IsVisible) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = RenderSize.Width, h = RenderSize.Height;
        if (w < 1 || h < 1 || Slices is not { Count: > 0 } slices) return;

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double Snap(double x) => Math.Round(x * scale) / scale;

        var places = BandMarks.Shares(slices.Select(s => s.Amount).ToList(), w, Gap, Least);
        for (int i = 0; i < slices.Count; i++)
        {
            var (x, wide) = places[i];
            if (!(wide > 0)) continue;
            // Both ends on whole pixels, so the gaps stay open and even.
            double left = Snap(x), right = Math.Max(left + 1 / scale, Snap(x + wide));
            double corner = Math.Min(Corner, (right - left) / 2);
            dc.DrawRoundedRectangle(slices[i].Fill, null, new Rect(left, 0, right - left, h), corner, corner);
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();
}
