using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace FocusTool.Win.Overlay;

// Resolves a timer's color palette from its theme name ("Light" / "Dark" / "Auto").
// Auto follows the current Windows apps theme.
internal sealed class TimerTheme
{
    public TimerTheme(string name)
    {
        Name = name;
    }

    public string Name { get; set; }

    public bool IsAuto => Name.Equals("Auto", StringComparison.OrdinalIgnoreCase);

    public bool IsLight
    {
        get
        {
            if (Name.Equals("Light", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsAuto)
            {
                return IsSystemLightTheme();
            }

            return false;
        }
    }

    public MediaColor BaseColor => IsLight ? MediaColor.FromRgb(244, 244, 244) : MediaColor.FromRgb(28, 28, 28);

    public MediaColor TextColor => IsLight ? MediaColor.FromRgb(26, 26, 26) : MediaColors.White;

    public MediaColor LabelColor => IsLight
        ? MediaColor.FromRgb(42, 42, 42)
        : MediaColor.FromArgb(0xDD, 255, 255, 255);

    public MediaColor AccentColor => IsLight
        ? MediaColor.FromRgb(46, 46, 46)
        : MediaColors.White;

    public MediaColor InactiveBorderColor => IsLight
        ? MediaColor.FromArgb(100, 0, 0, 0)
        : MediaColor.FromArgb(100, 255, 255, 255);

    public MediaColor ProgressTrackColor => IsLight
        ? MediaColor.FromArgb(70, 0, 0, 0)
        : MediaColor.FromArgb(70, 255, 255, 255);

    public static double BlinkAmount(double nowMs) =>
        0.5 + (0.5 * Math.Sin(nowMs / 145.0));

    public static MediaColor Blend(MediaColor left, MediaColor right, double amount)
    {
        var clamped = Math.Clamp(amount, 0, 1);
        return MediaColor.FromArgb(
            (byte)Math.Round(left.A + ((right.A - left.A) * clamped)),
            (byte)Math.Round(left.R + ((right.R - left.R) * clamped)),
            (byte)Math.Round(left.G + ((right.G - left.G) * clamped)),
            (byte)Math.Round(left.B + ((right.B - left.B) * clamped)));
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
}
