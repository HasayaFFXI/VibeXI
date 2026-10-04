using System.Windows;
using System.Windows.Controls;

namespace Zerg.Views;

/// <summary>The actions table and the heals table have two kinds of line: a
/// character's heading, and an action under it.</summary>
public sealed class ActionRowTemplates : DataTemplateSelector
{
    public DataTemplate? Heading { get; set; }
    public DataTemplate? Action { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is IGroupedRow { IsHeading: true } ? Heading : Action;
}
