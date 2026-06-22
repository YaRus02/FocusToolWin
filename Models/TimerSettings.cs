namespace FocusTool.Win.Models;

// Persisted defaults for newly created timers plus the live style applied to them.
// Open timers themselves are transient (not restored across restarts).
public sealed class TimerSettings
{
    public const int MaxDurationSeconds = (99 * 3600) + (59 * 60) + 59;

    public string Mode { get; set; } = "Countdown";
    public int DurationSeconds { get; set; } = 300;
    public string Label { get; set; } = string.Empty;
    public List<string> LabelHistory { get; set; } = [];
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;
    public string Theme { get; set; } = "Dark";
    public string TimeFormat { get; set; } = "24";
    public bool ProgressVisible { get; set; } = true;
    public bool LabelVisible { get; set; } = true;
    public bool BlinkOnFinish { get; set; } = true;

    public TimerMode GetMode() =>
        Enum.TryParse<TimerMode>(Mode ?? string.Empty, true, out var mode) ? mode : TimerMode.Countdown;

    public TimerSettings Clone() => new()
    {
        Mode = Mode ?? "Countdown",
        DurationSeconds = DurationSeconds,
        Label = Label ?? string.Empty,
        LabelHistory = LabelHistory is null ? [] : [.. LabelHistory],
        Scale = Scale,
        Opacity = Opacity,
        Theme = Theme ?? "Dark",
        TimeFormat = TimeFormat ?? "24",
        ProgressVisible = ProgressVisible,
        LabelVisible = LabelVisible,
        BlinkOnFinish = BlinkOnFinish
    };

    public void Normalize()
    {
        Mode = (Enum.TryParse<TimerMode>(Mode ?? string.Empty, true, out var mode) ? mode : TimerMode.Countdown).ToString();
        DurationSeconds = Math.Clamp(DurationSeconds, 1, MaxDurationSeconds);
        Scale = Math.Clamp(Scale, 0.6, 2.5);
        Opacity = Math.Clamp(Opacity, 0.2, 1.0);
        Label ??= string.Empty;

        if (!IsKnown(Theme, "Light", "Dark", "Auto"))
        {
            Theme = "Dark";
        }

        if (!IsKnown(TimeFormat, "24", "12"))
        {
            TimeFormat = "24";
        }

        LabelHistory ??= [];
        if (LabelHistory.Count > 10)
        {
            LabelHistory.RemoveRange(10, LabelHistory.Count - 10);
        }
    }

    private static bool IsKnown(string? value, params string[] allowed)
    {
        if (value is null)
        {
            return false;
        }

        foreach (var option in allowed)
        {
            if (value.Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
