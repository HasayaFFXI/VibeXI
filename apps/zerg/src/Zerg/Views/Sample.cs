using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>
/// A small picture, on the Settings page, of what a setting does: a strip
/// at the opacity set, four rows shaded as set, six rates marked as they
/// would be, a Compare row in the two runs' colours. It is made of the
/// tables' own parts (the row styles, <see cref="LowMark"/>,
/// <see cref="SmallBar"/>, <see cref="RunPair"/>), so it cannot draw
/// anything the tables would not.
///
/// <para>The hot key's keycaps are one too, named by the chord as it is
/// written: three keys and two plus signs are one thing to say.</para>
///
/// <para>To UI Automation it is one picture with a name
/// (<c>AutomationProperties.Name</c>), and nothing inside it is anything:
/// the names and figures in it are made up, and a script that reads the
/// window's text for a character's total (or a screen reader going through
/// the page) must not find them. Hidden text is read out as well as what is
/// shown, so they would turn up in every section's text.</para>
/// </summary>
public sealed class Sample : Decorator
{
    public Sample()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer(Sample owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(Sample);

        /// <summary>Nothing under it: the picture is the whole of it.</summary>
        protected override List<AutomationPeer>? GetChildrenCore() => null;

        // Whether it is a control and content is left to WPF, which says
        // yes only while it can be seen.
    }
}
