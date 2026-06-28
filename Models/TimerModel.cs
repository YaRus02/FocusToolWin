using System.Globalization;

namespace FocusTool.Win.Models;

internal enum TimerState
{
    Idle,
    Running,
    Paused,
    Overtime
}

internal readonly record struct TimerSnapshot(
    string TimeText,
    string Label,
    double Progress,
    bool HasProgress,
    TimerState State,
    bool LabelVisible);

// Pure timer logic, driven by a monotonic clock (nowMs) for elapsed-based modes and
// by wall-clock time for Clock / Until time. Rendering and input live in TimerWindow.
internal sealed class TimerModel
{
    private bool _running;
    private double _accumulatedSeconds;
    private double _runStartMs;
    private int _countdownSeconds;
    private DateTime _targetTime;
    private DateTime _untilStart;
    private string _timeFormat;

    public TimerModel(TimerSettings defaults, double nowMs)
    {
        Label = defaults.Label ?? string.Empty;
        LabelVisible = defaults.LabelVisible;
        Mode = defaults.GetMode();
        _timeFormat = NormalizeTimeFormat(defaults.TimeFormat);
        _countdownSeconds = Math.Clamp(defaults.DurationSeconds, 1, TimerSettings.MaxDurationSeconds);
        _targetTime = DateTime.Now.AddSeconds(_countdownSeconds);
        InitializeForMode(nowMs);
    }

    public TimerMode Mode { get; private set; }
    public string Label { get; set; }
    public bool LabelVisible { get; set; }
    public int CountdownSeconds => _countdownSeconds;
    public bool CanReset => Mode is TimerMode.Countdown or TimerMode.Stopwatch;
    public bool CanEditTime => Mode is TimerMode.Countdown or TimerMode.UntilTime;
    public string TimeFormat => _timeFormat;
    public bool Use24HourTime => _timeFormat == "24";

    public void SetMode(TimerMode mode, double nowMs)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        InitializeForMode(nowMs);
    }

    public void CycleMode(double nowMs)
    {
        var next = Mode switch
        {
            TimerMode.Countdown => TimerMode.Stopwatch,
            TimerMode.Stopwatch => TimerMode.Clock,
            TimerMode.Clock => TimerMode.UntilTime,
            _ => TimerMode.Countdown
        };

        SetMode(next, nowMs);
    }

    public void ToggleStartPause(double nowMs)
    {
        if (Mode is not (TimerMode.Countdown or TimerMode.Stopwatch))
        {
            return;
        }

        if (_running)
        {
            _accumulatedSeconds += (nowMs - _runStartMs) / 1000.0;
            _running = false;
        }
        else
        {
            _runStartMs = nowMs;
            _running = true;
        }
    }

    public bool Reset(double nowMs)
    {
        // Wall-clock modes (Clock / Until time) have no meaningful reset: their progress
        // is continuous against real time, so resetting the window would desync the bar
        // from the displayed remaining time.
        if (!CanReset)
        {
            return false;
        }

        _accumulatedSeconds = 0;
        _running = false;
        _runStartMs = nowMs;
        return true;
    }

    // Positive grows the countdown duration / pushes the target later; negative shrinks it.
    public void Adjust(int deltaSeconds, double nowMs)
    {
        _ = nowMs;
        switch (Mode)
        {
            case TimerMode.Countdown:
                _countdownSeconds = Math.Clamp(_countdownSeconds + deltaSeconds, 1, TimerSettings.MaxDurationSeconds);
                break;
            case TimerMode.UntilTime:
                _targetTime = _targetTime.AddSeconds(deltaSeconds);
                if (_targetTime <= _untilStart)
                {
                    _targetTime = _untilStart.AddSeconds(1);
                }

                break;
        }
    }

    public void SetCountdownSeconds(int seconds)
    {
        _countdownSeconds = Math.Clamp(seconds, 1, TimerSettings.MaxDurationSeconds);
        _accumulatedSeconds = 0;
        _running = false;
    }

    public void SetTargetTime(DateTime target)
    {
        _untilStart = DateTime.Now;
        _targetTime = target <= _untilStart ? _untilStart.AddSeconds(1) : target;
    }

    public void SetTimeFormat(string timeFormat)
    {
        _timeFormat = NormalizeTimeFormat(timeFormat);
    }

    public string DurationText() => FormatDuration(_countdownSeconds);

    public string TargetTimeText() =>
        _targetTime.ToString(Use24HourTime ? "HH:mm:ss" : "h:mm:ss tt", CultureInfo.InvariantCulture);

    public TimerSnapshot GetSnapshot(double nowMs)
    {
        return Mode switch
        {
            TimerMode.Countdown => CountdownSnapshot(nowMs),
            TimerMode.Stopwatch => StopwatchSnapshot(nowMs),
            TimerMode.Clock => new TimerSnapshot(
                DateTime.Now.ToString(Use24HourTime ? "HH:mm:ss" : "h:mm:ss tt", CultureInfo.InvariantCulture),
                Label,
                0,
                false,
                TimerState.Running,
                LabelVisible),
            _ => UntilTimeSnapshot()
        };
    }

    private double ElapsedSeconds(double nowMs) =>
        _accumulatedSeconds + (_running ? (nowMs - _runStartMs) / 1000.0 : 0);

    private TimerSnapshot CountdownSnapshot(double nowMs)
    {
        var elapsed = ElapsedSeconds(nowMs);
        var remaining = _countdownSeconds - elapsed;
        if (remaining <= 0)
        {
            return new TimerSnapshot("+" + FormatDuration(-remaining), Label, 1, true, TimerState.Overtime, LabelVisible);
        }

        var progress = _countdownSeconds > 0 ? Math.Clamp(elapsed / _countdownSeconds, 0, 1) : 0;
        var state = _running ? TimerState.Running : (_accumulatedSeconds > 0 ? TimerState.Paused : TimerState.Idle);
        return new TimerSnapshot(FormatDuration(remaining), Label, progress, true, state, LabelVisible);
    }

    private TimerSnapshot StopwatchSnapshot(double nowMs)
    {
        var elapsed = ElapsedSeconds(nowMs);
        var state = _running ? TimerState.Running : (elapsed > 0 ? TimerState.Paused : TimerState.Idle);
        return new TimerSnapshot(FormatDuration(elapsed), Label, 0, false, state, LabelVisible);
    }

    private TimerSnapshot UntilTimeSnapshot()
    {
        var now = DateTime.Now;
        var remaining = (_targetTime - now).TotalSeconds;
        if (remaining <= 0)
        {
            return new TimerSnapshot("+" + FormatDuration(-remaining), Label, 1, true, TimerState.Overtime, LabelVisible);
        }

        var total = (_targetTime - _untilStart).TotalSeconds;
        var progress = total > 0 ? Math.Clamp((now - _untilStart).TotalSeconds / total, 0, 1) : 0;
        return new TimerSnapshot(FormatDuration(remaining), Label, progress, true, TimerState.Running, LabelVisible);
    }

    private void InitializeForMode(double nowMs)
    {
        _running = false;
        _accumulatedSeconds = 0;
        _runStartMs = nowMs;
        if (Mode == TimerMode.UntilTime)
        {
            _untilStart = DateTime.Now;
            _targetTime = _untilStart.AddSeconds(_countdownSeconds);
        }
    }

    private static string FormatDuration(double totalSeconds)
    {
        var seconds = (int)Math.Floor(Math.Abs(totalSeconds) + 0.0001);
        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;
        var secs = seconds % 60;
        return $"{hours:00}:{minutes:00}:{secs:00}";
    }

    private static string NormalizeTimeFormat(string? timeFormat) =>
        string.Equals(timeFormat, "12", StringComparison.OrdinalIgnoreCase) ? "12" : "24";
}
