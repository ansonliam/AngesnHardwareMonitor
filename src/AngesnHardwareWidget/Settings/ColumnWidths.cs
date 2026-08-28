using System.Globalization;
using System.Windows;

namespace AngesnHardwareWidget.Settings;

/// <summary>
/// Turns one of the column-width settings ("*" or a pixel size) into the GridLength WPF actually
/// needs. Shared by the widget (label/graph columns, uniform across every row) and the view model
/// (the value column, which the RAM override makes different per row).
/// </summary>
public static class ColumnWidths
{
    /// <summary>Settings has already validated this, so a bad value here would mean a corrupt
    /// settings.json; falling back to a star column is the least disruptive thing to do with it.</summary>
    public static GridLength Parse(string width, double scale)
    {
        var trimmed = width.Trim();
        if (string.Equals(trimmed, AppSettings.StarColumnWidth, StringComparison.OrdinalIgnoreCase))
        {
            return new GridLength(1, GridUnitType.Star);
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
            ? new GridLength(pixels * scale)
            : new GridLength(1, GridUnitType.Star);
    }
}
