using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>
/// The room a line of text takes, without the text: never drawn, never
/// pressed, never read out.
///
/// <para>A section's tab is SemiBold while it is the one picked, and a
/// SemiBold word is a unit or two wider than the same word in Regular: left
/// alone, every tab after the picked one would shift sideways each time
/// another is picked. Laid under the label in the picked weight, this holds
/// the label's place at its widest, and the tabs stand still.</para>
///
/// <para>It is a <c>TextBlock</c> so that it measures exactly as the label
/// over it does. It tells UI Automation nothing: a script that lists every
/// piece of text (<c>drive.cs text</c>) would otherwise read each tab's
/// label twice.</para>
/// </summary>
public sealed class Room : TextBlock
{
    public Room()
    {
        Visibility = Visibility.Hidden;
        IsHitTestVisible = false;
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
