using System.Globalization;
using FocusTool.Win.Models;

namespace FocusTool.Win.Overlay;

internal static class TimerTimeEditor
{
    public static int MaxLength(TimerMode mode, bool use24HourTime)
    {
        if (mode != TimerMode.UntilTime)
        {
            return 8;
        }

        return use24HourTime ? 8 : 11;
    }

    public static string ToolTip(TimerMode mode, bool use24HourTime)
    {
        if (mode != TimerMode.UntilTime)
        {
            return "hh:mm:ss";
        }

        return use24HourTime ? "HH:mm:ss" : "h:mm:ss AM/PM";
    }

    public static bool IsValidPartial(string text, TimerMode mode, bool use24HourTime)
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
        if (parts.Length > 3)
        {
            return false;
        }

        return parts.All(part => part.Length <= 2);
    }

    public static bool TryParseDuration(string text, out int seconds)
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

    public static bool TryParseTargetTime(string text, bool use24HourTime, out DateTime target)
    {
        target = default;
        var now = DateTime.Now;
        if (!use24HourTime)
        {
            if (!DateTime.TryParseExact(
                text.Trim().ToUpperInvariant(),
                [
                    "h:mm tt", "hh:mm tt", "h:mmtt", "hh:mmtt",
                    "h:mm:ss tt", "hh:mm:ss tt", "h:mm:sstt", "hh:mm:sstt"
                ],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
            {
                return false;
            }

            target = new DateTime(now.Year, now.Month, now.Day, parsed.Hour, parsed.Minute, parsed.Second);
            if (target <= now)
            {
                target = target.AddDays(1);
            }

            return true;
        }

        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var minutes))
        {
            return false;
        }

        var seconds = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], out seconds))
        {
            return false;
        }

        if (hours is < 0 or > 23 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
        {
            return false;
        }

        target = new DateTime(now.Year, now.Month, now.Day, hours, minutes, seconds);
        if (target <= now)
        {
            target = target.AddDays(1);
        }

        return true;
    }

    private static bool IsValidTwelveHourTargetPartial(string text)
    {
        if (text.Length > 11 || text.Contains("::", StringComparison.Ordinal))
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
        return parts.Length <= 3 && parts.All(part => part.Length <= 2);
    }
}
