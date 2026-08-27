using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.Settings;
using AngesnHardwareWidget.ViewModels;

namespace AngesnHardwareWidget.Views;

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

        // Now that the monitor and its DPI are known, rescue the widget only if the saved position
        // is genuinely off screen -- for example the display it was on has been disconnected.
        if (EnsureOnScreen())
        {
            SavePlacement();
        }
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

    /// <summary>
    /// Ticks the entries that match the persisted state each time the menu opens.
    ///
    /// Each submenu is addressed by name and told which value it reflects. The previous version
    /// walked every submenu generically and inferred the value from the parent's header text, which
    /// silently mis-handled any submenu it did not recognise: the Polling interval items were
    /// compared against the text scale, so every one of them came out unticked.
    /// </summary>
    private void OnContextMenuOpened(object sender, RoutedEventArgs eventArgs)
    {
        var settings = _settings.Current;

        AlwaysOnTopMenuItem.IsChecked = settings.WidgetAlwaysOnTop;
        LockWidgetMenuItem.IsChecked = settings.WidgetLocked;

        TickMatching(TextSizeMenuItem, settings.WidgetTextScale);
        TickMatching(OpacityMenuItem, settings.WidgetOpacity);
        BuildPollingIntervalMenu(settings);
    }

    /// <summary>Ticks whichever child's Tag matches <paramref name="current"/>, and clears the rest.</summary>
    private static void TickMatching(MenuItem submenu, double current)
    {
        foreach (var child in submenu.Items.OfType<MenuItem>())
        {
            if (TryGetTag(child, out var value))
            {
                child.IsChecked = Math.Abs(value - current) < 0.001;
            }
        }
    }

    /// <summary>
    /// Fills the polling-interval submenu from the shared list of offered intervals.
    ///
    /// Hidden entirely in individual-interval mode: there is no single interval to pick then, and
    /// offering one here would silently overwrite eight per-metric settings. Those stay in the
    /// settings dialog, where all eight are visible at once.
    /// </summary>
    private void BuildPollingIntervalMenu(AppSettings settings)
    {
        if (!settings.UseUnifiedPollingInterval)
        {
            PollingIntervalMenuItem.Visibility = Visibility.Collapsed;
            return;
        }

        PollingIntervalMenuItem.Visibility = Visibility.Visible;

        // Surfaced on the header as well, so the current cadence is visible without opening the
        // submenu at all.
        var active = new PollingIntervalOption(settings.UnifiedPollingSeconds).Label;
        PollingIntervalMenuItem.Header = $"Polling interval  ({active})";
        PollingIntervalMenuItem.Items.Clear();

        foreach (var seconds in AppSettings.OfferedIntervalSeconds)
        {
            var item = new MenuItem
            {
                Header = new PollingIntervalOption(seconds).Label,
                IsCheckable = true,
                IsChecked = seconds == settings.UnifiedPollingSeconds,
                Tag = seconds.ToString(CultureInfo.InvariantCulture),
            };

            item.Click += OnPollingIntervalClick;
            PollingIntervalMenuItem.Items.Add(item);
        }
    }

    /// <summary>
    /// Unlike the cosmetic items on this menu, this rebuilds the polling schedule -- which is why
    /// the settings dialog keeps it behind a Save button. Here the choice is a single deliberate
    /// click, so applying it straight away is the expected behaviour.
    /// </summary>
    private void OnPollingIntervalClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not MenuItem item
            || !TryGetTag(item, out var seconds)
            || !AppSettings.IsValidInterval((int)seconds))
        {
            return;
        }

        Mutate(settings => settings.UnifiedPollingSeconds = (int)seconds);
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

    /// <summary>
    /// Restores the saved position verbatim. It is deliberately not clamped here: the only bounds
    /// available this early are SystemParameters.WorkArea, which describes the *primary* monitor
    /// only, so clamping to it dragged a widget parked on any other monitor back onto the primary
    /// one at every restart -- which is what made the widget appear to move on its own. Bounds
    /// checking happens in <see cref="EnsureOnScreen"/> once there is a handle to ask which monitor
    /// the window is actually on.
    /// </summary>
    private void RestorePlacement(AppSettings settings)
    {
        _restoringPlacement = true;
        try
        {
            Width = settings.WidgetWidth;
            Height = settings.WidgetHeight;

            if (settings.WidgetLeft is { } savedLeft && double.IsFinite(savedLeft)
                && settings.WidgetTop is { } savedTop && double.IsFinite(savedTop))
            {
                Left = savedLeft;
                Top = savedTop;
                return;
            }

            // Never positioned: park it near the top-right of the primary monitor on first run.
            // The primary monitor is the right assumption here precisely because there is no saved
            // position to respect.
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 24;
            Top = workArea.Top + 24;
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    /// <summary>
    /// Pulls the widget back on screen only if it is genuinely off it, measuring against the bounds
    /// of the monitor it is on rather than the primary monitor's work area.
    ///
    /// Screen *bounds*, not the work area, so parking the widget against or partly beneath the
    /// taskbar stays possible -- the same choice the AI Usage Monitor makes. Returns true if it
    /// moved anything, so the caller can persist the correction; an unsaved correction is what makes
    /// a widget reappear somewhere other than where it was left.
    /// </summary>
    private bool EnsureOnScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !double.IsFinite(Left) || !double.IsFinite(Top))
        {
            return false;
        }

        var bounds = Forms.Screen.FromHandle(handle).Bounds;

        // Screen bounds are physical pixels; Left/Top are DIPs. Without this transform the
        // comparison is wrong on any display that is not at 100% scaling.
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(bounds.Left, bounds.Top));
        var bottomRight = transform.Transform(new Point(bounds.Right, bounds.Bottom));

        var width = double.IsFinite(Width) ? Width : ActualWidth;
        var height = double.IsFinite(Height) ? Height : ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var changed = false;

        // A widget bigger than the screen can never sit fully on it; trim it first.
        if (height > bottomRight.Y - topLeft.Y)
        {
            height = Math.Max(MinHeight, bottomRight.Y - topLeft.Y);
            Height = height;
            changed = true;
        }

        if (width > bottomRight.X - topLeft.X)
        {
            width = Math.Max(MinWidth, bottomRight.X - topLeft.X);
            Width = width;
            changed = true;
        }

        var left = Math.Clamp(Left, topLeft.X, Math.Max(topLeft.X, bottomRight.X - width));
        var top = Math.Clamp(Top, topLeft.Y, Math.Max(topLeft.Y, bottomRight.Y - height));

        if (Math.Abs(left - Left) > 0.5)
        {
            Left = left;
            changed = true;
        }

        if (Math.Abs(top - Top) > 0.5)
        {
            Top = top;
            changed = true;
        }

        if (changed)
        {
            AppLog.Info($"Widget was off screen; moved to {Left:0},{Top:0} ({Width:0}x{Height:0}).");
        }

        return changed;
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
        if (settings.WidgetLeft == Left
            && settings.WidgetTop == Top
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
