using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareWidget.Services;
using HardwareWidget.Settings;

namespace HardwareWidget.Views;

/// <summary>
/// The borderless widget shell. Owns appearance (Retro/Default, font, text size, opacity),
/// drag-to-move, edge resizing and placement persistence; what is displayed lives in the ViewModel.
/// </summary>
public partial class MainWindow : Window
{
    // WM_NCHITTEST and the hit-test results it can return, so a chromeless window can still be
    // resized from any edge or corner.
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    /// <summary>Width of the invisible band along each edge that grabs a resize.</summary>
    private const double ResizeBorder = 7d;

    // Window style bits, used to opt the widget out of Aero Snap.
    private const int GwlStyle = -16;
    private const int WsMaximizeBox = 0x00010000;

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _placementSaveTimer;

    private HwndSource? _windowSource;
    private bool _restoringPlacement;
    private bool _locked;

    public MainWindow(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();

        // Dragging and resizing fire a stream of events; save once things settle rather than
        // rewriting settings.json on every pixel.
        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _placementSaveTimer.Tick += (_, _) =>
        {
            _placementSaveTimer.Stop();
            SavePlacement();
        };

        RestorePlacement(_settings.Current);
        ApplyAppearance(_settings.Current);

        _settings.SettingsChanged += OnSettingsChanged;

        SourceInitialized += OnSourceInitialized;
        LocationChanged += (_, _) => QueuePlacementSave();
        SizeChanged += (_, _) => QueuePlacementSave();
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        Closing += (_, _) =>
        {
            _placementSaveTimer.Stop();
            SavePlacement();
        };
    }

    // ------------------------------------------------------------- edge resize

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        DisableWindowSnapping(handle);
    }

    /// <summary>
    /// Turns off Aero Snap for the widget: dragging it to a screen edge should just park it there,
    /// not maximise it or fill half the screen. Windows only offers snapping to windows that can be
    /// maximised, so clearing WS_MAXIMIZEBOX disables it. WS_THICKFRAME is deliberately left alone,
    /// so resizing (and the WM_NCHITTEST edge handling above) keeps working.
    /// </summary>
    private static void DisableWindowSnapping(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(handle, GwlStyle);
        SetWindowLong(handle, GwlStyle, style & ~WsMaximizeBox);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// Claims a border band on all four edges for resizing. WindowStyle="None" removes the frame
    /// Windows would normally hit-test, so without this the window can only be resized from the
    /// bottom-right grip; answering WM_NCHITTEST restores dragging from the left and top too.
    /// </summary>
    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest || _locked || ResizeMode == ResizeMode.NoResize)
        {
            return IntPtr.Zero;
        }

        var packed = lParam.ToInt64();
        var screenPoint = new Point(
            unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff)));
        var point = PointFromScreen(screenPoint);

        var left = point.X >= 0 && point.X < ResizeBorder;
        var right = point.X <= ActualWidth && point.X > ActualWidth - ResizeBorder;
        var top = point.Y >= 0 && point.Y < ResizeBorder;
        var bottom = point.Y <= ActualHeight && point.Y > ActualHeight - ResizeBorder;

        var hitTest = (left, right, top, bottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => 0,
        };

        if (hitTest == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hitTest);
    }

    /// <summary>Drag the card itself to move the window, since there is no title bar.</summary>
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_locked || eventArgs.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }

    // ----------------------------------------------------------- context menu

    /// <summary>Ticks the entries that match the persisted state each time the menu opens.</summary>
    private void OnContextMenuOpened(object sender, RoutedEventArgs eventArgs)
    {
        var settings = _settings.Current;

        AlwaysOnTopMenuItem.IsChecked = settings.WidgetAlwaysOnTop;
        LockWidgetMenuItem.IsChecked = settings.WidgetLocked;

        if (sender is not ContextMenu menu)
        {
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            foreach (var child in item.Items.OfType<MenuItem>())
            {
                if (!TryGetTag(child, out var tagValue))
                {
                    continue;
                }

                var target = item.Header as string == "Opacity"
                    ? settings.WidgetOpacity
                    : settings.WidgetTextScale;

                child.IsChecked = Math.Abs(tagValue - target) < 0.001;
            }
        }
    }

    private void OnTextScaleClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem item && TryGetTag(item, out var scale))
        {
            Mutate(settings => settings.WidgetTextScale = scale);
        }
    }

    private void OnOpacityClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem item && TryGetTag(item, out var opacity))
        {
            Mutate(settings => settings.WidgetOpacity = opacity);
        }
    }

    private void OnAlwaysOnTopClick(object sender, RoutedEventArgs eventArgs) =>
        Mutate(settings => settings.WidgetAlwaysOnTop = !settings.WidgetAlwaysOnTop);

    private void OnLockClick(object sender, RoutedEventArgs eventArgs) =>
        Mutate(settings => settings.WidgetLocked = !settings.WidgetLocked);

    private void OnHideClick(object sender, RoutedEventArgs eventArgs) => Hide();

    /// <summary>Menu tags are invariant-culture decimals such as "0.85".</summary>
    private static bool TryGetTag(MenuItem item, out double value) =>
        double.TryParse(
            item.Tag?.ToString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private void Mutate(Action<AppSettings> change)
    {
        var settings = _settings.Current;
        change(settings);
        _settings.Save(settings);
    }

    // ------------------------------------------------------------- appearance

    private void OnSettingsChanged(object? sender, AppSettings updated) =>
        Dispatcher.BeginInvoke(() => ApplyAppearance(updated));

    /// <summary>
    /// Retro versus Default, mirroring the AI Usage Monitor: Retro means an embedded pixel font,
    /// aliased/fixed text rendering, square corners and a visible 1px border; Default means the
    /// system UI font, ClearType and a soft rounded card.
    /// </summary>
    private void ApplyAppearance(AppSettings settings)
    {
        var retro = settings.WidgetAppearance == AppSettings.RetroAppearance;
        var font = ResolveFont(settings.WidgetFont);
        var scale = settings.WidgetTextScale;

        Resources["WidgetFontFamily"] = font;
        Resources["WidgetValueFontFamily"] = font;
        Resources["WidgetValueFontWeight"] = settings.WidgetTextWeight switch
        {
            "Bold" => FontWeights.Bold,
            "SemiBold" => FontWeights.SemiBold,
            _ => FontWeights.Normal,
        };

        // The pixel faces have a smaller apparent x-height, so Retro starts a couple of points
        // larger to stay as readable as the system font.
        Resources["WidgetLabelFontSize"] = (retro ? 12d : 11d) * scale;
        Resources["WidgetValueFontSize"] = (retro ? 16d : 14d) * scale;

        Resources["WidgetCardBackground"] = Frozen(retro
            ? Color.FromArgb(0xF2, 0x18, 0x1D, 0x24)
            : Color.FromArgb(0xE6, 0x2A, 0x2F, 0x38));
        Resources["WidgetCardBorderBrush"] = retro
            ? Frozen(Color.FromArgb(0xCC, 0x7F, 0x91, 0xA3))
            : Brushes.Transparent;
        Resources["WidgetCardBorderThickness"] = new Thickness(retro ? 1 : 0);
        Resources["WidgetCardCornerRadius"] = new CornerRadius(retro ? 0 : 10);
        Resources["WidgetLabelBrush"] = Frozen(Color.FromRgb(0xE6, 0xEB, 0xF0));

        // The expanded RAM row ("23.2/63.9 GB (36%)") needs noticeably more room before it is
        // worth splitting into another column, and larger text needs proportionally more again.
        Resources["WidgetMinimumColumnWidth"] = (settings.ShowRamUsedAndTotal ? 210d : 150d) * scale;

        TextOptions.SetTextRenderingMode(this, retro ? TextRenderingMode.Aliased : TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, retro ? TextHintingMode.Fixed : TextHintingMode.Auto);

        Opacity = settings.WidgetOpacity;
        Topmost = settings.WidgetAlwaysOnTop;

        _locked = settings.WidgetLocked;
        ResizeMode = _locked ? ResizeMode.NoResize : ResizeMode.CanResize;
    }

    /// <summary>
    /// Maps a chosen font name to a family. Every name but the system font is an embedded family
    /// under Assets/fonts, and the family names inside those TTFs match the dropdown labels, so the
    /// lookup is a straight pack-URI reference.
    /// </summary>
    private static FontFamily ResolveFont(string name) =>
        name == AppSettings.SystemFont
            ? new FontFamily(AppSettings.SystemFont)
            : new FontFamily(new Uri("pack://application:,,,/"), $"./Assets/fonts/#{name}");

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // -------------------------------------------------------------- placement

    private void RestorePlacement(AppSettings settings)
    {
        _restoringPlacement = true;
        try
        {
            Width = settings.WidgetWidth;
            Height = settings.WidgetHeight;

            var workArea = SystemParameters.WorkArea;

            // NaN means "never positioned"; park it near the top-right corner on first run.
            var left = double.IsFinite(settings.WidgetLeft)
                ? settings.WidgetLeft
                : workArea.Right - Width - 24;
            var top = double.IsFinite(settings.WidgetTop)
                ? settings.WidgetTop
                : workArea.Top + 24;

            // Guard against a saved position on a monitor that is no longer attached.
            Left = Math.Clamp(left, workArea.Left - (Width / 2), workArea.Right - 40);
            Top = Math.Clamp(top, workArea.Top, workArea.Bottom - 40);
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    private void QueuePlacementSave()
    {
        if (_restoringPlacement)
        {
            return;
        }

        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private void SavePlacement()
    {
        if (_restoringPlacement || !double.IsFinite(Left) || !double.IsFinite(Top))
        {
            return;
        }

        var settings = _settings.Current;
        if (settings.WidgetLeft.Equals(Left)
            && settings.WidgetTop.Equals(Top)
            && settings.WidgetWidth.Equals(Width)
            && settings.WidgetHeight.Equals(Height))
        {
            return;
        }

        settings.WidgetLeft = Left;
        settings.WidgetTop = Top;
        settings.WidgetWidth = Width;
        settings.WidgetHeight = Height;
        _settings.Save(settings);
    }
}
