using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace Zerg.Charts;

/// <summary>
/// One line of text, measured and ready to draw, with tabular figures: every
/// digit the same width, so a number that changes many times a second doesn't
/// shuffle sideways. WPF's FormattedText can't ask a font for those, so this
/// goes to the text formatter underneath it.
/// </summary>
sealed class DrawnText : IDisposable
{
    static readonly TextFormatter Formatter = TextFormatter.Create(TextFormattingMode.Ideal);

    readonly TextLine line;

    public DrawnText(string text, Typeface face, double size, Brush brush, double pixelsPerDip)
    {
        var run = new Run(face, size, brush, pixelsPerDip);
        // A paragraph width of 0 is "no limit": the line is never wrapped.
        line = Formatter.FormatLine(new Source(text, run), 0, 0, new Paragraph(run), null);
    }

    public double Width => line.WidthIncludingTrailingWhitespace;
    public double Height => line.Height;

    /// <summary>Draws the line with its top-left corner at (x, y).</summary>
    public void Draw(DrawingContext dc, double x, double y) => line.Draw(dc, new Point(x, y), InvertAxes.None);

    public void Dispose() => line.Dispose();

    sealed class Source(string text, TextRunProperties run) : TextSource
    {
        public override TextRun GetTextRun(int index) =>
            index < text.Length ? new TextCharacters(text, index, text.Length - index, run) : new TextEndOfParagraph(1);

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int index) =>
            new(0, new CultureSpecificCharacterBufferRange(CultureInfo.InvariantCulture, CharacterBufferRange.Empty));

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }

    sealed class Run : TextRunProperties
    {
        readonly Typeface face;
        readonly double size;
        readonly Brush brush;

        public Run(Typeface face, double size, Brush brush, double pixelsPerDip)
        {
            this.face = face;
            this.size = size;
            this.brush = brush;
            PixelsPerDip = pixelsPerDip;
        }

        public override Typeface Typeface => face;
        public override double FontRenderingEmSize => size;
        public override double FontHintingEmSize => size;
        public override TextDecorationCollection? TextDecorations => null;
        public override Brush ForegroundBrush => brush;
        public override Brush? BackgroundBrush => null;
        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;
        public override TextEffectCollection? TextEffects => null;
        public override TextRunTypographyProperties TypographyProperties => Tabular.Instance;
    }

    sealed class Paragraph(TextRunProperties run) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => run;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties? TextMarkerProperties => null;
        public override double Indent => 0;
    }

    /// <summary>The font's defaults, except that numerals are tabular.</summary>
    sealed class Tabular : TextRunTypographyProperties
    {
        public static readonly Tabular Instance = new();

        public override FontNumeralAlignment NumeralAlignment => FontNumeralAlignment.Tabular;

        public override bool StandardLigatures => true;
        public override bool ContextualLigatures => true;
        public override bool DiscretionaryLigatures => false;
        public override bool HistoricalLigatures => false;
        public override bool ContextualAlternates => true;
        public override bool HistoricalForms => false;
        public override bool Kerning => true;
        public override bool CapitalSpacing => false;
        public override bool CaseSensitiveForms => false;
        public override bool StylisticSet1 => false;
        public override bool StylisticSet2 => false;
        public override bool StylisticSet3 => false;
        public override bool StylisticSet4 => false;
        public override bool StylisticSet5 => false;
        public override bool StylisticSet6 => false;
        public override bool StylisticSet7 => false;
        public override bool StylisticSet8 => false;
        public override bool StylisticSet9 => false;
        public override bool StylisticSet10 => false;
        public override bool StylisticSet11 => false;
        public override bool StylisticSet12 => false;
        public override bool StylisticSet13 => false;
        public override bool StylisticSet14 => false;
        public override bool StylisticSet15 => false;
        public override bool StylisticSet16 => false;
        public override bool StylisticSet17 => false;
        public override bool StylisticSet18 => false;
        public override bool StylisticSet19 => false;
        public override bool StylisticSet20 => false;
        public override bool SlashedZero => false;
        public override bool MathematicalGreek => false;
        public override bool EastAsianExpertForms => false;
        public override FontVariants Variants => FontVariants.Normal;
        public override FontCapitals Capitals => FontCapitals.Normal;
        public override FontFraction Fraction => FontFraction.Normal;
        public override FontNumeralStyle NumeralStyle => FontNumeralStyle.Normal;
        public override FontEastAsianWidths EastAsianWidths => FontEastAsianWidths.Normal;
        public override FontEastAsianLanguage EastAsianLanguage => FontEastAsianLanguage.Normal;
        public override int StandardSwashes => 0;
        public override int ContextualSwashes => 0;
        public override int StylisticAlternates => 0;
        public override int AnnotationAlternates => 0;
    }
}
