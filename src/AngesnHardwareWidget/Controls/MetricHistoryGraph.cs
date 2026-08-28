using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using AngesnHardwareWidget.ViewModels;

namespace AngesnHardwareWidget.Controls;

public sealed class MetricHistoryGraph : FrameworkElement
{
    private static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(15);
    private INotifyCollectionChanged? _observedCollection;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(MetricHistoryGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(MetricHistoryGraph),
        new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(MetricHistoryGraph),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(37, 37, 37)),
            new Pen(new SolidColorBrush(Color.FromRgb(103, 103, 103)), 1),
            new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));

        var history = ItemsSource?.Cast<HardwareHistoryPoint>().OrderBy(point => point.RecordedAt).ToList() ?? [];
        if (history.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var start = now - WindowLength;
        var visible = history.Where(point => point.RecordedAt >= start).ToList();
        var predecessor = history.LastOrDefault(point => point.RecordedAt < start);
        if (predecessor is not null)
        {
            visible.Insert(0, predecessor with { RecordedAt = start });
        }
        else if (visible.Count > 0)
        {
            // With disk history deliberately disabled, the first app-session sample would
            // otherwise be a single invisible point at the right border. Extend that first known
            // value across the empty part of the window so every graph is visible immediately.
            visible.Insert(0, visible[0] with { RecordedAt = start });
        }

        if (visible.Count == 0)
        {
            return;
        }

        var plot = new Rect(2, 2, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 4));
        var maximum = double.IsFinite(Maximum) && Maximum > 0 ? Maximum : 100;
        var points = visible.Select(point => ToPoint(point, start, plot, maximum)).ToList();
        points.Add(new Point(plot.Right, points[^1].Y));

        var lineBrush = LineBrush ?? Brushes.LimeGreen;
        var lineColor = lineBrush is SolidColorBrush solid ? solid.Color : Colors.LimeGreen;
        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Point(points[0].X, plot.Bottom), true, true);
            context.LineTo(points[0], true, false);
            context.PolyLineTo(points.Skip(1).ToList(), true, false);
            context.LineTo(new Point(plot.Right, plot.Bottom), true, false);
        }

        drawingContext.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(38, lineColor.R, lineColor.G, lineColor.B)),
            null,
            area);

        var line = new StreamGeometry();
        using (var context = line.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToList(), true, false);
        }

        drawingContext.DrawGeometry(null, new Pen(lineBrush, 1.25), line);
    }

    private static Point ToPoint(HardwareHistoryPoint point, DateTimeOffset start, Rect plot, double maximum)
    {
        var elapsed = Math.Clamp((point.RecordedAt - start).TotalMilliseconds, 0, WindowLength.TotalMilliseconds);
        var x = plot.X + (elapsed / WindowLength.TotalMilliseconds * plot.Width);
        var y = plot.Y + ((1 - Math.Clamp(point.Value / maximum, 0, 1)) * plot.Height);
        return new Point(x, y);
    }

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var graph = (MetricHistoryGraph)dependencyObject;
        if (graph._observedCollection is not null)
        {
            graph._observedCollection.CollectionChanged -= graph.OnCollectionChanged;
        }

        graph._observedCollection = args.NewValue as INotifyCollectionChanged;
        if (graph._observedCollection is not null)
        {
            graph._observedCollection.CollectionChanged += graph.OnCollectionChanged;
        }

        graph.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => InvalidateVisual();
}
