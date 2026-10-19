using System.Windows;
using System.Windows.Controls;

namespace Sora.App;

// Descriptions take the available width; trailing controls share the card's right edge.
public sealed class SettingsGrid : Grid
{
    public SettingsGrid()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    }
}
