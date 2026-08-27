using System.Windows;
using System.Windows.Controls;

namespace HardwareWidget.Controls;

/// <summary>
/// Lays the metric rows out in as many equal columns as the available width allows, balancing the
/// rows between them.
///
/// A WrapPanel cannot do this. With Orientation="Vertical" a WrapPanel wraps on *height*, so
/// widening the widget would leave one tall column and a band of empty space; the requirement is
/// the opposite -- when the widget gets wide, the rows should split into another column. So the
/// column count is derived from width here, and the rows are then divided as evenly as possible:
/// 8 rows become 8x1, then 4x2, then 3+3+2, and so on.
/// </summary>
public sealed class MetricColumnsPanel : Panel
{
    /// <summary>Narrowest a column may get before rows are folded back into fewer columns.</summary>
    public static readonly DependencyProperty MinimumColumnWidthProperty = DependencyProperty.Register(
        nameof(MinimumColumnWidth),
        typeof(double),
        typeof(MetricColumnsPanel),
        new FrameworkPropertyMetadata(150d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Horizontal gap between columns.</summary>
    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing),
        typeof(double),
        typeof(MetricColumnsPanel),
        new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinimumColumnWidth
    {
        get => (double)GetValue(MinimumColumnWidthProperty);
        set => SetValue(MinimumColumnWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = VisibleChildren();
        if (visible.Count == 0)
        {
            return new Size(0, 0);
        }

        var layout = Plan(visible.Count, availableSize.Width);
        var childConstraint = new Size(layout.ColumnWidth, double.PositiveInfinity);

        var rowHeight = 0d;
        foreach (var child in visible)
        {
            child.Measure(childConstraint);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        var width = (layout.ColumnWidth * layout.Columns) + (ColumnSpacing * (layout.Columns - 1));

        // Height is driven by the tallest column, which is the first one whenever the rows do not
        // divide evenly.
        return new Size(
            double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            rowHeight * layout.RowsPerColumn);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = VisibleChildren();
        if (visible.Count == 0)
        {
            return finalSize;
        }

        var layout = Plan(visible.Count, finalSize.Width);
        var rowHeight = layout.RowsPerColumn > 0 ? finalSize.Height / layout.RowsPerColumn : finalSize.Height;

        var index = 0;
        for (var column = 0; column < layout.Columns && index < visible.Count; column++)
        {
            // The short columns go on the right, so 8 rows over 3 columns reads 3+3+2 rather than
            // 2+3+3 -- filling left to right keeps the ragged edge at the end.
            var rowsInColumn = layout.RowsPerColumn
                - (column >= layout.Columns - layout.ShortColumns ? 1 : 0);
            var x = column * (layout.ColumnWidth + ColumnSpacing);

            for (var row = 0; row < rowsInColumn && index < visible.Count; row++)
            {
                visible[index++].Arrange(new Rect(x, row * rowHeight, layout.ColumnWidth, rowHeight));
            }
        }

        return finalSize;
    }

    private List<UIElement> VisibleChildren()
    {
        var visible = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                visible.Add(child);
            }
        }

        return visible;
    }

    private Layout Plan(int count, double availableWidth)
    {
        var minimumWidth = Math.Max(1d, MinimumColumnWidth);
        var spacing = Math.Max(0d, ColumnSpacing);

        var columns = 1;
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            // How many columns of at least minimumWidth, plus the gaps between them, fit?
            columns = (int)Math.Floor((availableWidth + spacing) / (minimumWidth + spacing));
        }

        columns = Math.Clamp(columns, 1, count);

        var rowsPerColumn = (int)Math.Ceiling(count / (double)columns);

        // Columns that carry one row fewer than the tallest, e.g. 8 rows over 3 columns is 3+3+2,
        // so exactly one column is short.
        var shortColumns = (columns * rowsPerColumn) - count;

        var columnWidth = double.IsInfinity(availableWidth) || availableWidth <= 0
            ? minimumWidth
            : Math.Max(1d, (availableWidth - (spacing * (columns - 1))) / columns);

        return new Layout(columns, rowsPerColumn, shortColumns, columnWidth);
    }

    private readonly record struct Layout(int Columns, int RowsPerColumn, int ShortColumns, double ColumnWidth);
}
