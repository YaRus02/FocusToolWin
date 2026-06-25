using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using Microsoft.Win32;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;
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
    private string _theme;
    private bool _progressVisible;
    private bool _blinkOnFinish;

    private IntPtr _returnForeground;
    private bool _focused;
    private bool _editing;
    private bool _editingTime;

    public event EventHandler? DefaultsChanged;
    public event EventHandler<string>? LabelCommitted;

    public TimerWindow(TimerModel model, TimerSettings style, Func<double> clock)
    {
        _model = model;
        _clock = clock;
        _scale = style.Scale;
        _opacity = style.Opacity;
        _theme = style.Theme;
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
        _labelEdit.KeyDown += OnLabelEditKeyDown;
        _labelEdit.LostKeyboardFocus += (_, _) => CommitLabelEdit();

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
        _timeEdit.PreviewKeyDown += OnTimeEditPreviewKeyDown;
        _timeEdit.KeyDown += OnTimeEditKeyDown;
        _timeEdit.PreviewTextInput += OnTimeEditPreviewTextInput;
        _timeEdit.LostKeyboardFocus += (_, _) => CommitTimeEdit();
        WpfDataObject.AddPastingHandler(_timeEdit, OnTimeEditPaste);
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
        ReassertContextMenuTopmost();
    }

    public void Refresh()
    {
        var nowMs = _clock();
        var snapshot = _model.GetSnapshot(nowMs);

        _timeText.Text = snapshot.TimeText;
        ApplyTimeTextFit(snapshot.TimeText);
        var overtime = snapshot.State == TimerState.Overtime;
        var blink = overtime && _blinkOnFinish ? BlinkAmount(nowMs) : 0;
        var textColor = overtime ? OvertimeColor : ThemeTextColor();
        _timeText.Foreground = new SolidColorBrush(textColor);
        _timeText.Opacity = overtime && _blinkOnFinish
            ? 0.62 + (0.38 * blink)
            : snapshot.State == TimerState.Paused ? 0.65 : 1.0;
        _block.Opacity = overtime && _blinkOnFinish ? 0.78 + (0.22 * blink) : 1.0;

        if (!_editing)
        {
            _labelText.Text = snapshot.Label;
            var showLabel = snapshot.LabelVisible && snapshot.Label.Length > 0;
            _labelText.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
        }

        var showProgress = _progressVisible && snapshot.HasProgress;
        _progressTrack.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (showProgress)
        {
            _progressFill.Background = new SolidColorBrush(overtime ? Blend(OvertimeColor, ThemeAccentColor(), blink * 0.35) : ThemeAccentColor());
            UpdateProgressWidth(snapshot.Progress);
        }

        if (overtime && _blinkOnFinish)
        {
            _block.BorderBrush = new SolidColorBrush(Blend(OvertimeColor, ThemeAccentColor(), blink * 0.45));
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
        _timeText.FontSize = text.Contains("AM", StringComparison.Ordinal) || text.Contains("PM", StringComparison.Ordinal)
            ? CompactTimeFontSize
            : TimeFontSize;
    }

    private void ApplyStyle()
    {
        _scaleTransform.ScaleX = _scale;
        _scaleTransform.ScaleY = _scale;

        var baseColor = ThemeBaseColor();
        var alpha = (int)Math.Round(225 * Math.Clamp(_opacity, 0.2, 1.0));
        _block.Background = new SolidColorBrush(MediaColor.FromArgb((byte)Math.Clamp(alpha, 0, 255), baseColor.R, baseColor.G, baseColor.B));

        _labelText.Foreground = new SolidColorBrush(ThemeLabelColor());
        _labelEdit.Foreground = new SolidColorBrush(ThemeTextColor());
        _labelEdit.Background = new SolidColorBrush(IsLightTheme() ? Colors.White : MediaColor.FromRgb(45, 45, 45));
        _labelEdit.CaretBrush = new SolidColorBrush(ThemeTextColor());
        _labelEdit.BorderBrush = new SolidColorBrush(ThemeAccentColor());
        _timeEdit.Foreground = new SolidColorBrush(ThemeTextColor());
        _timeEdit.Background = new SolidColorBrush(IsLightTheme() ? Colors.White : MediaColor.FromRgb(45, 45, 45));
        _timeEdit.CaretBrush = new SolidColorBrush(ThemeTextColor());
        _timeEdit.BorderBrush = new SolidColorBrush(ThemeAccentColor());
        _progressTrack.Background = new SolidColorBrush(ThemeProgressTrackColor());

        var text = ThemeTextColor();
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
                stepLabel.Foreground = new SolidColorBrush(ThemeLabelColor());
            }
        }

        ApplyFocusVisual();
        Refresh();
    }

    private void ApplyFocusVisual()
    {
        if (_focused)
        {
            _block.BorderBrush = new SolidColorBrush(ThemeAccentColor());
            _block.BorderThickness = new Thickness(2);
        }
        else
        {
            _block.BorderBrush = new SolidColorBrush(ThemeInactiveBorderColor());
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

    private bool IsLightTheme()
    {
        if (_theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_theme.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return IsSystemLightTheme();
        }

        return false;
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0);
            return value is int intValue && intValue > 0;
        }
        catch
        {
            return false;
        }
    }

    private MediaColor ThemeBaseColor() => IsLightTheme() ? MediaColor.FromRgb(244, 244, 244) : MediaColor.FromRgb(28, 28, 28);

    private MediaColor ThemeTextColor() => IsLightTheme() ? MediaColor.FromRgb(26, 26, 26) : Colors.White;

    private MediaColor ThemeLabelColor() => IsLightTheme()
        ? MediaColor.FromRgb(42, 42, 42)
        : MediaColor.FromArgb(0xDD, 255, 255, 255);

    private MediaColor ThemeAccentColor() => IsLightTheme()
        ? MediaColor.FromRgb(46, 46, 46)
        : Colors.White;

    private MediaColor ThemeInactiveBorderColor() => IsLightTheme()
        ? MediaColor.FromArgb(100, 0, 0, 0)
        : MediaColor.FromArgb(100, 255, 255, 255);

    private MediaColor ThemeProgressTrackColor() => IsLightTheme()
        ? MediaColor.FromArgb(70, 0, 0, 0)
        : MediaColor.FromArgb(70, 255, 255, 255);

    private static double BlinkAmount(double nowMs) =>
        0.5 + (0.5 * Math.Sin(nowMs / 145.0));

    private static MediaColor Blend(MediaColor left, MediaColor right, double amount)
    {
        var clamped = Math.Clamp(amount, 0, 1);
        return MediaColor.FromArgb(
            (byte)Math.Round(left.A + ((right.A - left.A) * clamped)),
            (byte)Math.Round(left.R + ((right.R - left.R) * clamped)),
            (byte)Math.Round(left.G + ((right.G - left.G) * clamped)),
            (byte)Math.Round(left.B + ((right.B - left.B) * clamped)));
    }

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
        if (!_theme.Equals("Auto", StringComparison.OrdinalIgnoreCase))
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
        if (_editing)
        {
            CommitLabelEdit();
        }

        if (_editingTime)
        {
            CommitTimeEdit();
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
        if (_editing || _editingTime)
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
                BeginLabelEdit();
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
        if (_editing || _editingTime || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }

    private void OnLabelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing || _editingTime || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            BeginLabelEdit();
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

    private void BeginLabelEdit()
    {
        _editing = true;
        _labelEdit.Text = _model.Label;
        _labelText.Visibility = Visibility.Collapsed;
        _labelEdit.Visibility = Visibility.Visible;
        _labelEdit.Focus();
        _labelEdit.SelectAll();
    }

    private void OnLabelEditKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitLabelEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelLabelEdit();
            e.Handled = true;
        }
    }

    private void CommitLabelEdit()
    {
        if (!_editing)
        {
            return;
        }

        var label = _labelEdit.Text.Trim();
        _model.Label = label;
        EndLabelEdit();
        if (label.Length > 0)
        {
            LabelCommitted?.Invoke(this, label);
        }

        DefaultsChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void CancelLabelEdit()
    {
        EndLabelEdit();
        Refresh();
    }

    private void EndLabelEdit()
    {
        _editing = false;
        _labelEdit.Visibility = Visibility.Collapsed;
        Focus();
    }

    private void OnTimeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing || _editingTime || e.ButtonState != MouseButtonState.Pressed)
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
            BeginTimeEdit();
        }
    }

    private void BeginTimeEdit()
    {
        if (!_model.CanEditTime)
        {
            return;
        }

        _editingTime = true;
        _timeEdit.MaxLength = TimeEditMaxLength();
        _timeEdit.ToolTip = TimeEditToolTip();
        _timeEdit.Text = _model.Mode == TimerMode.UntilTime ? _model.TargetTimeText() : _model.DurationText();
        _timeText.Visibility = Visibility.Collapsed;
        _timeEdit.Visibility = Visibility.Visible;
        _timeEdit.Focus();
        _timeEdit.CaretIndex = 0;
    }

    private int TimeEditMaxLength()
    {
        if (_model.Mode != TimerMode.UntilTime)
        {
            return 8;
        }

        return _model.Use24HourTime ? 5 : 8;
    }

    private string TimeEditToolTip()
    {
        if (_model.Mode != TimerMode.UntilTime)
        {
            return "mm:ss or h:mm:ss";
        }

        return _model.Use24HourTime ? "HH:mm" : "h:mm AM/PM";
    }

    private void OnTimeEditPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is not (Key.Back or Key.Delete))
        {
            return;
        }

        if (SelectionTouchesFixedTimeSeparator())
        {
            e.Handled = true;
            return;
        }

        var text = _timeEdit.Text ?? string.Empty;
        if (e.Key == Key.Back
            && _timeEdit.SelectionLength == 0
            && _timeEdit.SelectionStart > 0
            && text[_timeEdit.SelectionStart - 1] == ':')
        {
            _timeEdit.SelectionStart = Math.Max(0, _timeEdit.SelectionStart - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete
            && _timeEdit.SelectionLength == 0
            && _timeEdit.SelectionStart < text.Length
            && text[_timeEdit.SelectionStart] == ':')
        {
            _timeEdit.SelectionStart = Math.Min(text.Length, _timeEdit.SelectionStart + 1);
            e.Handled = true;
        }
    }

    private void OnTimeEditKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTimeEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTimeEdit();
            e.Handled = true;
        }
    }

    private void OnTimeEditPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SelectionTouchesFixedTimeSeparator() && e.Text.IndexOf(':') < 0)
        {
            e.Handled = true;
            return;
        }

        e.Handled = !IsValidTimeEditPartial(GetProposedText(_timeEdit, e.Text), _model.Mode, _model.Use24HourTime);
    }

    private void OnTimeEditPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(WpfDataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(WpfDataFormats.Text) as string ?? string.Empty;
        if ((SelectionTouchesFixedTimeSeparator() && text.IndexOf(':') < 0)
            || !IsValidTimeEditPartial(GetProposedText(_timeEdit, text), _model.Mode, _model.Use24HourTime))
        {
            e.CancelCommand();
        }
    }

    private void CommitTimeEdit()
    {
        if (!_editingTime)
        {
            return;
        }

        var input = _timeEdit.Text.Trim();
        if (_model.Mode == TimerMode.Countdown && TryParseDuration(input, out var seconds))
        {
            _model.SetCountdownSeconds(seconds);
            DefaultsChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (_model.Mode == TimerMode.UntilTime && TryParseTargetTime(input, _model.Use24HourTime, out var target))
        {
            _model.SetTargetTime(target);
            DefaultsChanged?.Invoke(this, EventArgs.Empty);
        }

        EndTimeEdit();
        Refresh();
    }

    private void CancelTimeEdit()
    {
        EndTimeEdit();
        Refresh();
    }

    private void EndTimeEdit()
    {
        _editingTime = false;
        _timeEdit.Visibility = Visibility.Collapsed;
        _timeText.Visibility = Visibility.Visible;
        Focus();
    }

    private static string GetProposedText(WpfTextBox textBox, string input)
    {
        var text = textBox.Text ?? string.Empty;
        var start = Math.Clamp(textBox.SelectionStart, 0, text.Length);
        var length = Math.Clamp(textBox.SelectionLength, 0, text.Length - start);
        return text.Remove(start, length).Insert(start, input);
    }

    private bool SelectionTouchesFixedTimeSeparator()
    {
        var text = _timeEdit.Text ?? string.Empty;
        if (_timeEdit.SelectionLength == 0 || text.Length == 0)
        {
            return false;
        }

        var start = Math.Clamp(_timeEdit.SelectionStart, 0, text.Length);
        var length = Math.Clamp(_timeEdit.SelectionLength, 0, text.Length - start);
        return length > 0 && text.AsSpan(start, length).Contains(':');
    }

    private static bool IsValidTimeEditPartial(string text, TimerMode mode, bool use24HourTime)
    {
        if (text.Length == 0)
        {
            return true;
        }

        if (mode == TimerMode.UntilTime && !use24HourTime)
        {
            return IsValidTwelveHourTargetPartial(text);
        }

        if (text.Any(ch => !char.IsDigit(ch) && ch != ':') || text.Contains("::", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = text.Split(':');
        var maxParts = mode == TimerMode.UntilTime ? 2 : 3;
        if (parts.Length > maxParts)
        {
            return false;
        }

        return parts.All(part => part.Length <= 2);
    }

    private static bool IsValidTwelveHourTargetPartial(string text)
    {
        if (text.Length > 8 || text.Contains("::", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (!char.IsDigit(ch) && ch != ':' && ch != ' ' && "apmAPM".IndexOf(ch) < 0)
            {
                return false;
            }
        }

        var upper = text.ToUpperInvariant();
        if (upper.Count(ch => ch == ':') > 1)
        {
            return false;
        }

        var markerIndex = upper.IndexOfAny(['A', 'P', 'M']);
        if (markerIndex >= 0)
        {
            var marker = upper[markerIndex..].TrimStart();
            if (!"AM".StartsWith(marker, StringComparison.Ordinal) && !"PM".StartsWith(marker, StringComparison.Ordinal))
            {
                return false;
            }

            var beforeMarker = upper[..markerIndex].TrimEnd();
            if (beforeMarker.Any(ch => ch is 'A' or 'P' or 'M'))
            {
                return false;
            }
        }

        var timePart = markerIndex >= 0 ? upper[..markerIndex].TrimEnd() : upper;
        var parts = timePart.Split(':');
        return parts.Length <= 2 && parts.All(part => part.Length <= 2);
    }

    // "mm:ss", "h:mm:ss", or a single number (minutes); returns total seconds (>= 1).
    private static bool TryParseDuration(string text, out int seconds)
    {
        seconds = 0;
        var parts = text.Split(':');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        int hours = 0, minutes = 0, secs = 0;
        if (parts.Length == 1)
        {
            if (!int.TryParse(parts[0], out minutes))
            {
                return false;
            }
        }
        else if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0], out minutes) || !int.TryParse(parts[1], out secs))
            {
                return false;
            }
        }
        else if (!int.TryParse(parts[0], out hours) || !int.TryParse(parts[1], out minutes) || !int.TryParse(parts[2], out secs))
        {
            return false;
        }

        if (hours < 0 || minutes < 0 || secs < 0)
        {
            return false;
        }

        seconds = (hours * 3600) + (minutes * 60) + secs;
        return seconds >= 1;
    }

    // Wall-clock time; rolls to tomorrow if already passed today.
    private static bool TryParseTargetTime(string text, bool use24HourTime, out DateTime target)
    {
        target = default;
        var now = DateTime.Now;
        if (!use24HourTime)
        {
            if (!DateTime.TryParseExact(
                text.Trim().ToUpperInvariant(),
                ["h:mm tt", "hh:mm tt", "h:mmtt", "hh:mmtt"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
            {
                return false;
            }

            target = new DateTime(now.Year, now.Month, now.Day, parsed.Hour, parsed.Minute, 0);
            if (target <= now)
            {
                target = target.AddDays(1);
            }

            return true;
        }

        var parts = text.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var minutes))
        {
            return false;
        }

        if (hours is < 0 or > 23 || minutes is < 0 or > 59)
        {
            return false;
        }

        target = new DateTime(now.Year, now.Month, now.Day, hours, minutes, 0);
        if (target <= now)
        {
            target = target.AddDays(1);
        }

        return true;
    }

    public TimerSettings CaptureDefaults() => new()
    {
        Mode = _model.Mode.ToString(),
        DurationSeconds = _model.CountdownSeconds,
        Label = _model.Label,
        Scale = _scale,
        Opacity = _opacity,
        Theme = _theme,
        TimeFormat = _model.TimeFormat,
        ProgressVisible = _progressVisible,
        LabelVisible = _model.LabelVisible,
        BlinkOnFinish = _blinkOnFinish
    };

    private WpfContextMenu BuildContextMenu()
    {
        var menu = new WpfContextMenu();
        menu.Opened += (_, _) => ReassertContextMenuTopmostSoon();
        menu.Items.Add(NewItem("Start / Pause", (_, _) => { _model.ToggleStartPause(_clock()); DefaultsChanged?.Invoke(this, EventArgs.Empty); Refresh(); }));
        menu.Items.Add(NewItem("Reset", (_, _) => { _model.Reset(_clock()); Refresh(); }));
        menu.Items.Add(new Separator());

        var mode = new WpfMenuItem { Header = "Mode" };
        foreach (var value in Enum.GetValues<TimerMode>())
        {
            var captured = value;
            mode.Items.Add(NewItem(ModeName(value), (_, _) => { _model.SetMode(captured, _clock()); DefaultsChanged?.Invoke(this, EventArgs.Empty); Refresh(); }));
        }

        menu.Items.Add(mode);
        menu.Items.Add(NewItem("Edit label", (_, _) => BeginLabelEdit()));
        menu.Items.Add(NewItem("Edit time", (_, _) => BeginTimeEdit()));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildStyleMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(NewItem("Close", (_, _) => Close()));
        AttachSubmenuTopmostHandlers(menu.Items);
        return menu;
    }

    private void ReassertContextMenuTopmostSoon()
    {
        ReassertContextMenuTopmost();
        _ = Dispatcher.BeginInvoke(
            new Action(ReassertContextMenuTopmost),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void ReassertContextMenuTopmost()
    {
        if (_block.ContextMenu is not { IsOpen: true } menu)
        {
            return;
        }

        var handles = new HashSet<IntPtr>();
        AddMenuVisualHandle(menu, handles);
        AddMenuItemHandles(menu.Items, handles);

        foreach (var handle in handles)
        {
            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HwndTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
        }
    }

    private void AttachSubmenuTopmostHandlers(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is not WpfMenuItem menuItem)
            {
                continue;
            }

            menuItem.SubmenuOpened += (_, _) => ReassertContextMenuTopmostSoon();
            AttachSubmenuTopmostHandlers(menuItem.Items);
        }
    }

    private static void AddMenuItemHandles(ItemCollection items, ISet<IntPtr> handles)
    {
        foreach (var item in items)
        {
            if (item is not WpfMenuItem menuItem)
            {
                continue;
            }

            AddMenuVisualHandle(menuItem, handles);
            AddMenuItemHandles(menuItem.Items, handles);
        }
    }

    private static void AddMenuVisualHandle(Visual visual, ISet<IntPtr> handles)
    {
        var handle = (PresentationSource.FromVisual(visual) as HwndSource)?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            handles.Add(handle);
        }
    }

    private WpfMenuItem BuildStyleMenu()
    {
        var style = new WpfMenuItem { Header = "Style" };

        var theme = new WpfMenuItem { Header = "Theme" };
        foreach (var option in new[] { "Light", "Dark", "Auto" })
        {
            var captured = option;
            theme.Items.Add(NewItem(option, (_, _) => { _theme = captured; ApplyStyle(); DefaultsChanged?.Invoke(this, EventArgs.Empty); }));
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
        style.Items.Add(NewItem("Progress on/off", (_, _) => { _progressVisible = !_progressVisible; ApplyStyle(); DefaultsChanged?.Invoke(this, EventArgs.Empty); }));
        style.Items.Add(NewItem("Label on/off", (_, _) => { _model.LabelVisible = !_model.LabelVisible; DefaultsChanged?.Invoke(this, EventArgs.Empty); Refresh(); }));
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
