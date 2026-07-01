using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Services;
using Microsoft.Win32;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using MediaColor = System.Windows.Media.Color;

namespace FocusTool.Win.Overlay;

// A single floating timer. Renders the model's snapshot and owns its keyboard/mouse
// input; keys only act while this window holds OS focus ("TimerFocus"), so they never
// leak to the app underneath when the timer is not focused.
internal sealed class TimerWindow : Window
{
    private const double BlockWidth = 238;
    private const double TimeFontSize = 46;
    private const double CompactTimeFontSize = 34;
    private static readonly MediaColor OvertimeColor = MediaColor.FromRgb(232, 118, 90);

    private readonly TimerModel _model;
    private readonly Func<double> _clock;

    private readonly Border _block;
    private readonly TextBlock _labelText;
    private readonly WpfTextBox _labelEdit;
    private readonly TextBlock _timeText;
    private readonly WpfTextBox _timeEdit;
    private readonly Border _progressTrack;
    private readonly Border _progressFill;
    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly StackPanel _styleControls;

    private double _scale;
    private double _opacity;
    private readonly TimerTheme _theme;
    private bool _progressVisible;
    private bool _blinkOnFinish;

    private readonly TimerLabelEditSession _labelSession;
    private readonly TimerTimeEditSession _timeSession;

    private IntPtr _returnForeground;
    private bool _focused;

    public event EventHandler? DefaultsChanged;
    public event EventHandler<string>? LabelCommitted;

    public TimerWindow(TimerModel model, TimerSettings style, Func<double> clock)
    {
        _model = model;
        _clock = clock;
        _scale = style.Scale;
        _opacity = style.Opacity;
        _theme = new TimerTheme(style.Theme);
        _progressVisible = style.ProgressVisible;
        _blinkOnFinish = style.BlinkOnFinish;

        Title = "FocusTool Timer";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        Focusable = true;
        Left = -10000;
        Top = -10000;

        _labelText = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _labelText.MouseLeftButtonDown += OnLabelMouseDown;
        _labelEdit = new WpfTextBox
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(2, 0, 2, 0),
            Visibility = Visibility.Collapsed,
            MinWidth = 80
        };
        _labelSession = new TimerLabelEditSession(
            _labelEdit,
            _labelText,
            _model,
            onChanged: () => { DefaultsChanged?.Invoke(this, EventArgs.Empty); Refresh(); },
            onCommitted: label => LabelCommitted?.Invoke(this, label),
            onCancelled: Refresh,
            focusBack: () => Focus());

        _timeText = new TextBlock
        {
            FontSize = TimeFontSize,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 48,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };

        _progressFill = new Border { Height = 5, HorizontalAlignment = WpfHorizontalAlignment.Left, CornerRadius = new CornerRadius(2.5) };
        _progressTrack = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            Margin = new Thickness(0, 10, 0, 0),
            Child = _progressFill
        };

        _timeEdit = new WpfTextBox
        {
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(2, 0, 2, 0),
            Visibility = Visibility.Collapsed
        };
        _timeSession = new TimerTimeEditSession(
            _timeEdit,
            _timeText,
            _model,
            onChanged: () => DefaultsChanged?.Invoke(this, EventArgs.Empty),
            refresh: Refresh,
            focusBack: () => Focus());
        _timeText.MouseLeftButtonDown += OnTimeMouseDown;

        var stack = new StackPanel { Orientation = WpfOrientation.Vertical };
        stack.Children.Add(_labelText);
        stack.Children.Add(_labelEdit);
        stack.Children.Add(_timeText);
        stack.Children.Add(_timeEdit);
        stack.Children.Add(_progressTrack);

        _styleControls = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            Visibility = Visibility.Hidden
        };
        _styleControls.Children.Add(MakeStyleLabel("Size"));
        _styleControls.Children.Add(CreateStepButton("-", () => StepScale(-0.1)));
        _styleControls.Children.Add(CreateStepButton("+", () => StepScale(0.1)));
        _styleControls.Children.Add(MakeStyleLabel("Opacity"));
        _styleControls.Children.Add(CreateStepButton("-", () => StepOpacity(-0.1)));
        _styleControls.Children.Add(CreateStepButton("+", () => StepOpacity(0.1)));
        stack.Children.Add(_styleControls);

        _block = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            BorderThickness = new Thickness(1),
            Width = BlockWidth,
            SnapsToDevicePixels = true,
            LayoutTransform = _scaleTransform,
            Child = stack
        };
        _block.MouseLeftButtonDown += OnBlockMouseLeftButtonDown;
        _block.MouseWheel += OnBlockMouseWheel;
        _block.ContextMenu = BuildContextMenu();
        _block.ContextMenuOpening += (_, _) => RefreshContextMenu();

        Content = _block;

        PreviewKeyDown += OnPreviewKeyDown;
        Activated += (_, _) => { _focused = true; ApplyFocusVisual(); UpdateStyleControlsVisibility(); };
        Deactivated += OnDeactivated;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ApplyStyle();
        Refresh();
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void FocusTimer()
    {
        Activate();
    }

    // Snapshot the timer as a premultiplied-BGRA sprite plus its screen rect, so a
    // Capture Stage can composite it over the source frame by screen overlap. The
    // content (_block) is drawn via a VisualBrush stretched to the window's client
    // size, which reproduces the on-screen scale transform.
    public OverlaySprite? CaptureSprite()
    {
        if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var widthPx = rect.Right - rect.Left;
        var heightPx = rect.Bottom - rect.Top;
        if (widthPx <= 0 || heightPx <= 0)
        {
            return null;
        }

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            widthPx, heightPx, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new System.Windows.Media.VisualBrush(_block) { Stretch = System.Windows.Media.Stretch.Fill };
            dc.DrawRectangle(brush, null, new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
        }

        bitmap.Render(visual);
        var stride = widthPx * 4;
        var pixels = new byte[stride * heightPx];
        bitmap.CopyPixels(pixels, stride, 0);
        return new OverlaySprite(pixels, widthPx, heightPx, stride, rect.Left, rect.Top, widthPx, heightPx);
    }

    public void MoveToPhysical(int x, int y)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            x,
            y,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    // Pull the timer back onto the nearest surviving monitor's working area after a
    // display change so it is never stranded off-screen on a removed monitor.
    public void ReconcileToWorkingArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var rect))
        {
            return;
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var area = System.Windows.Forms.Screen
            .FromRectangle(System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom))
            .WorkingArea;
        const int margin = 1;
        var maxLeft = Math.Max(area.Left + margin, area.Right - width - margin);
        var maxTop = Math.Max(area.Top + margin, area.Bottom - height - margin);
        var x = Math.Clamp(rect.Left, area.Left + margin, maxLeft);
        var y = Math.Clamp(rect.Top, area.Top + margin, maxTop);
        if (x != rect.Left || y != rect.Top)
        {
            MoveToPhysical(x, y);
        }
    }

    public void ReassertTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
        WpfTopmostContextMenuHelper.ReassertIfOpen(_block.ContextMenu);
    }

    public void Refresh()
    {
        var nowMs = _clock();
        var snapshot = _model.GetSnapshot(nowMs);

        _timeText.Text = snapshot.TimeText;
        ApplyTimeTextFit(snapshot.TimeText);
        var overtime = snapshot.State == TimerState.Overtime;
        var blink = overtime && _blinkOnFinish ? TimerTheme.BlinkAmount(nowMs) : 0;
        var textColor = overtime ? OvertimeColor : _theme.TextColor;
        _timeText.Foreground = new SolidColorBrush(textColor);
        _timeText.Opacity = overtime && _blinkOnFinish
            ? 0.62 + (0.38 * blink)
            : snapshot.State == TimerState.Paused ? 0.65 : 1.0;
        _block.Opacity = overtime && _blinkOnFinish ? 0.78 + (0.22 * blink) : 1.0;

        if (!_labelSession.IsEditing)
        {
            _labelText.Text = snapshot.Label;
            var showLabel = snapshot.LabelVisible && snapshot.Label.Length > 0;
            _labelText.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
        }

        var showProgress = _progressVisible && snapshot.HasProgress;
        _progressTrack.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (showProgress)
        {
            _progressFill.Background = new SolidColorBrush(overtime ? TimerTheme.Blend(OvertimeColor, _theme.AccentColor, blink * 0.35) : _theme.AccentColor);
            UpdateProgressWidth(snapshot.Progress);
        }

        if (overtime && _blinkOnFinish)
        {
            _block.BorderBrush = new SolidColorBrush(TimerTheme.Blend(OvertimeColor, _theme.AccentColor, blink * 0.45));
        }
        else
        {
            ApplyFocusVisual();
        }
    }

    private void UpdateProgressWidth(double progress)
    {
        var trackWidth = _progressTrack.ActualWidth;
        if (trackWidth <= 0)
        {
            _progressTrack.UpdateLayout();
            trackWidth = _progressTrack.ActualWidth;
        }

        _progressFill.Width = Math.Max(0, trackWidth * Math.Clamp(progress, 0, 1));
    }

    private void ApplyTimeTextFit(string text)
    {
        // Compact font for the AM/PM clock and for longer strings (e.g. the "+hh:mm:ss"
        // overtime form) so they never overflow the fixed-width block.
        var compact = text.Contains("AM", StringComparison.Ordinal)
            || text.Contains("PM", StringComparison.Ordinal)
            || text.Length > 8;
        _timeText.FontSize = compact ? CompactTimeFontSize : TimeFontSize;
    }

    private void ApplyStyle()
    {
        _scaleTransform.ScaleX = _scale;
        _scaleTransform.ScaleY = _scale;

        var baseColor = _theme.BaseColor;
        var alpha = (int)Math.Round(225 * Math.Clamp(_opacity, 0.2, 1.0));
        _block.Background = new SolidColorBrush(MediaColor.FromArgb((byte)Math.Clamp(alpha, 0, 255), baseColor.R, baseColor.G, baseColor.B));

        _labelText.Foreground = new SolidColorBrush(_theme.LabelColor);
        _labelEdit.Foreground = new SolidColorBrush(_theme.TextColor);
        _labelEdit.Background = new SolidColorBrush(_theme.IsLight ? Colors.White : MediaColor.FromRgb(45, 45, 45));
        _labelEdit.CaretBrush = new SolidColorBrush(_theme.TextColor);
        _labelEdit.BorderBrush = new SolidColorBrush(_theme.AccentColor);
        _timeEdit.Foreground = new SolidColorBrush(_theme.TextColor);
        _timeEdit.Background = new SolidColorBrush(_theme.IsLight ? Colors.White : MediaColor.FromRgb(45, 45, 45));
        _timeEdit.CaretBrush = new SolidColorBrush(_theme.TextColor);
        _timeEdit.BorderBrush = new SolidColorBrush(_theme.AccentColor);
        _progressTrack.Background = new SolidColorBrush(_theme.ProgressTrackColor);

        var text = _theme.TextColor;
        var stepForeground = new SolidColorBrush(text);
        var stepBackground = new SolidColorBrush(MediaColor.FromArgb(36, text.R, text.G, text.B));
        foreach (var child in _styleControls.Children)
        {
            if (child is WpfButton stepButton)
            {
                stepButton.Foreground = stepForeground;
                stepButton.Background = stepBackground;
            }
            else if (child is TextBlock stepLabel)
            {
                stepLabel.Foreground = new SolidColorBrush(_theme.LabelColor);
            }
        }

        ApplyFocusVisual();
        Refresh();
    }

    private void ApplyFocusVisual()
    {
        if (_focused)
        {
            _block.BorderBrush = new SolidColorBrush(_theme.AccentColor);
            _block.BorderThickness = new Thickness(2);
        }
        else
        {
            _block.BorderBrush = new SolidColorBrush(_theme.InactiveBorderColor);
            _block.BorderThickness = new Thickness(1);
        }
    }

    private void UpdateStyleControlsVisibility()
    {
        _styleControls.Visibility = _focused ? Visibility.Visible : Visibility.Hidden;
    }

    private void StepScale(double delta)
    {
        _scale = Math.Clamp(_scale + delta, 0.6, 2.5);
        ApplyStyle();
        DefaultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StepOpacity(double delta)
    {
        _opacity = Math.Clamp(_opacity + delta, 0.2, 1.0);
        ApplyStyle();
        DefaultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private WpfButton CreateStepButton(string text, Action onClick)
    {
        var button = new WpfButton
        {
            Content = text,
            Width = 22,
            Height = 20,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Focusable = false,
            Cursor = WpfCursors.Hand
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static TextBlock MakeStyleLabel(string text) => new()
    {
        Text = text,
        FontSize = 10,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 4, 0)
    };

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnClosed(e);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (!_theme.IsAuto)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnUserPreferenceChanged(sender, e)));
            return;
        }

        ApplyStyle();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmActivate && (wParam.ToInt32() & 0xFFFF) != 0)
        {
            // lParam = the window being deactivated, i.e. the app we should hand focus
            // back to when the user presses Esc.
            if (lParam != IntPtr.Zero && lParam != hwnd)
            {
                _returnForeground = lParam;
            }
        }

        return IntPtr.Zero;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_labelSession.IsEditing)
        {
            _labelSession.Commit();
        }

        if (_timeSession.IsEditing)
        {
            _timeSession.Commit();
        }

        _focused = false;
        ApplyFocusVisual();
        UpdateStyleControlsVisibility();
    }

    private void ExitFocus()
    {
        if (_returnForeground != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_returnForeground);
        }
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (_labelSession.IsEditing || _timeSession.IsEditing)
        {
            return;
        }

        var nowMs = _clock();
        switch (e.Key)
        {
            case Key.Space:
                _model.ToggleStartPause(nowMs);
                break;
            case Key.R:
                _model.Reset(nowMs);
                break;
            case Key.Up:
                _model.Adjust(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 300 : 60, nowMs);
                break;
            case Key.Down:
                _model.Adjust(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -300 : -60, nowMs);
                break;
            case Key.Right:
                _model.Adjust(1, nowMs);
                break;
            case Key.Left:
                _model.Adjust(-1, nowMs);
                break;
            case Key.Tab:
                _model.CycleMode(nowMs);
                break;
            case Key.Enter:
            case Key.F2:
                _labelSession.Begin();
                e.Handled = true;
                return;
            case Key.Back:
            case Key.Delete:
                Close();
                e.Handled = true;
                return;
            case Key.Escape:
                ExitFocus();
                e.Handled = true;
                return;
            default:
                return;
        }

        e.Handled = true;
        if (e.Key != Key.R)
        {
            DefaultsChanged?.Invoke(this, EventArgs.Empty);
        }

        Refresh();
    }

    private void OnBlockMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_labelSession.IsEditing || _timeSession.IsEditing || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }

    private void OnLabelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_labelSession.IsEditing || _timeSession.IsEditing || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            _labelSession.Begin();
        }
    }

    private void OnBlockMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 300 : 60;
        _model.Adjust(e.Delta > 0 ? step : -step, _clock());
        DefaultsChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
        e.Handled = true;
    }

    private void OnTimeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_labelSession.IsEditing || _timeSession.IsEditing || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        if (!_model.CanEditTime)
        {
            return; // not an editable time; let the press bubble to the block for dragging
        }

        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            _timeSession.Begin();
        }
    }

    public TimerSettings CaptureDefaults() => new()
    {
        Mode = _model.Mode.ToString(),
        DurationSeconds = _model.CountdownSeconds,
        Label = _model.Label,
        Scale = _scale,
        Opacity = _opacity,
        Theme = _theme.Name,
        TimeFormat = _model.TimeFormat,
        ProgressVisible = _progressVisible,
        LabelVisible = _model.LabelVisible,
        BlinkOnFinish = _blinkOnFinish
    };

    private WpfContextMenu BuildContextMenu()
    {
        var menu = new WpfContextMenu();
        menu.Items.Add(NewItem("Start / Pause", (_, _) => { _model.ToggleStartPause(_clock()); DefaultsChanged?.Invoke(this, EventArgs.Empty); Refresh(); }));
        menu.Items.Add(NewItem("Reset", (_, _) => { _model.Reset(_clock()); Refresh(); }));
        menu.Items.Add(new Separator());

        var mode = new WpfMenuItem { Header = "Mode" };
        foreach (var value in Enum.GetValues<TimerMode>())
        {
            var captured = value;
            var modeItem = NewItem(ModeName(value), (_, _) => { _model.SetMode(captured, _clock()); DefaultsChanged?.Invoke(this, EventArgs.Empty); Refresh(); });
            modeItem.IsCheckable = true;
            modeItem.IsChecked = value == _model.Mode;
            mode.Items.Add(modeItem);
        }

        menu.Items.Add(mode);
        menu.Items.Add(NewItem("Edit label", (_, _) => _labelSession.Begin()));
        menu.Items.Add(NewItem("Edit time", (_, _) => _timeSession.Begin()));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildStyleMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(NewItem("Close", (_, _) => Close()));
        WpfTopmostContextMenuHelper.Attach(menu);
        return menu;
    }

    private WpfMenuItem BuildStyleMenu()
    {
        var style = new WpfMenuItem { Header = "Style" };

        var theme = new WpfMenuItem { Header = "Theme" };
        foreach (var option in new[] { "Light", "Dark", "Auto" })
        {
            var captured = option;
            var themeItem = NewItem(option, (_, _) => { _theme.Name = captured; ApplyStyle(); DefaultsChanged?.Invoke(this, EventArgs.Empty); });
            themeItem.IsCheckable = true;
            themeItem.IsChecked = string.Equals(_theme.Name, option, StringComparison.OrdinalIgnoreCase);
            theme.Items.Add(themeItem);
        }

        style.Items.Add(theme);

        var timeFormat = new WpfMenuItem { Header = "Time format" };
        var twentyFourHourItem = NewItem("24-hour", (_, _) => SetTimeFormat("24"));
        twentyFourHourItem.IsCheckable = true;
        twentyFourHourItem.IsChecked = _model.Use24HourTime;
        timeFormat.Items.Add(twentyFourHourItem);

        var twelveHourItem = NewItem("12-hour AM/PM", (_, _) => SetTimeFormat("12"));
        twelveHourItem.IsCheckable = true;
        twelveHourItem.IsChecked = !_model.Use24HourTime;
        timeFormat.Items.Add(twelveHourItem);

        style.Items.Add(timeFormat);
        var progressItem = NewItem("Show progress", (_, _) => { _progressVisible = !_progressVisible; ApplyStyle(); DefaultsChanged?.Invoke(this, EventArgs.Empty); });
        progressItem.IsCheckable = true;
        progressItem.IsChecked = _progressVisible;
        style.Items.Add(progressItem);

        var labelItem = NewItem("Show label", (_, _) => { _model.LabelVisible = !_model.LabelVisible; DefaultsChanged?.Invoke(this, EventArgs.Empty); Refresh(); });
        labelItem.IsCheckable = true;
        labelItem.IsChecked = _model.LabelVisible;
        style.Items.Add(labelItem);

        var blinkItem = NewItem("Blink on finish", (_, _) => { _blinkOnFinish = !_blinkOnFinish; DefaultsChanged?.Invoke(this, EventArgs.Empty); Refresh(); });
        blinkItem.IsCheckable = true;
        blinkItem.IsChecked = _blinkOnFinish;
        style.Items.Add(blinkItem);
        return style;
    }

    private void RefreshContextMenu()
    {
        if (_block.ContextMenu is null)
        {
            return;
        }

        foreach (var item in _block.ContextMenu.Items)
        {
            if (item is WpfMenuItem { Header: "Mode" } modeItem)
            {
                for (var i = 0; i < modeItem.Items.Count && i < Enum.GetValues<TimerMode>().Length; i++)
                {
                    if (modeItem.Items[i] is WpfMenuItem mi)
                    {
                        mi.IsChecked = (TimerMode)i == _model.Mode;
                    }
                }
            }
            else if (item is WpfMenuItem { Header: "Reset" } resetItem)
            {
                resetItem.IsEnabled = _model.CanReset;
            }
            else if (item is WpfMenuItem { Header: "Edit time" } editTimeItem)
            {
                editTimeItem.IsEnabled = _model.CanEditTime;
            }
            else if (item is WpfMenuItem { Header: "Style" } styleItem)
            {
                RefreshStyleMenu(styleItem);
            }
        }
    }

    private void SetTimeFormat(string timeFormat)
    {
        _model.SetTimeFormat(timeFormat);
        DefaultsChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void RefreshStyleMenu(WpfMenuItem styleItem)
    {
        foreach (var item in styleItem.Items)
        {
            if (item is WpfMenuItem { Header: "Time format" } formatItem)
            {
                foreach (var formatOption in formatItem.Items.OfType<WpfMenuItem>())
                {
                    if (formatOption.Header?.ToString() == "24-hour")
                    {
                        formatOption.IsChecked = _model.Use24HourTime;
                    }
                    else if (formatOption.Header?.ToString() == "12-hour AM/PM")
                    {
                        formatOption.IsChecked = !_model.Use24HourTime;
                    }
                }
            }
            else if (item is WpfMenuItem { Header: "Theme" } themeItem)
            {
                foreach (var themeOption in themeItem.Items.OfType<WpfMenuItem>())
                {
                    themeOption.IsChecked = string.Equals(themeOption.Header?.ToString(), _theme.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
            else if (item is WpfMenuItem { Header: "Show progress" } progressItem)
            {
                progressItem.IsChecked = _progressVisible;
            }
            else if (item is WpfMenuItem { Header: "Show label" } labelItem)
            {
                labelItem.IsChecked = _model.LabelVisible;
            }
            else if (item is WpfMenuItem { Header: "Blink on finish" } blinkItem)
            {
                blinkItem.IsChecked = _blinkOnFinish;
            }
        }
    }

    private static WpfMenuItem NewItem(string header, RoutedEventHandler onClick)
    {
        var item = new WpfMenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    private static string ModeName(TimerMode mode) => mode switch
    {
        TimerMode.Countdown => "Countdown",
        TimerMode.Stopwatch => "Stopwatch",
        TimerMode.Clock => "Clock",
        TimerMode.UntilTime => "Until time",
        _ => mode.ToString()
    };
}
