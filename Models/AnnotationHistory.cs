namespace FocusTool.Win.Models;

internal static class AnnotationHistory
{
    public static void PushBounded(
        Stack<List<AnnotationShape>> history,
        List<AnnotationShape> snapshot,
        int maximumEntries)
    {
        history.Push(snapshot);
        if (history.Count <= maximumEntries)
        {
            return;
        }

        var retained = history
            .Take(maximumEntries)
            .Reverse()
            .ToArray();
        history.Clear();
        foreach (var item in retained)
        {
            history.Push(item);
        }
    }

    public static bool HistoryContainsTemporaryAnnotations(Stack<List<AnnotationShape>> history)
    {
        return history.Any(snapshot => snapshot.Any(shape => shape.IsTemporary));
    }

    public static bool NormalizeStack(
        Stack<List<AnnotationShape>> history,
        IReadOnlyList<AnnotationShape> current,
        double nowMs)
    {
        if (history.Count == 0)
        {
            return false;
        }

        var original = history.ToList();
        var normalized = new List<List<AnnotationShape>>();
        var previous = current;
        foreach (var snapshot in original)
        {
            var filtered = snapshot
                .Where(shape => !shape.IsExpired(nowMs))
                .Select(shape => shape.Clone())
                .ToList();

            if (SnapshotsEqual(filtered, previous))
            {
                continue;
            }

            normalized.Add(filtered);
            previous = filtered;
        }

        var changed = normalized.Count != original.Count;
        if (!changed)
        {
            for (var i = 0; i < original.Count; i++)
            {
                if (!SnapshotsEqual(original[i], normalized[i]))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return false;
        }

        history.Clear();
        for (var i = normalized.Count - 1; i >= 0; i--)
        {
            history.Push(normalized[i]);
        }

        return true;
    }

    public static bool ShapesEqual(AnnotationShape left, AnnotationShape right)
    {
        return left.Tool == right.Tool
            && left.Start.Equals(right.Start)
            && left.End.Equals(right.End)
            && left.Points.SequenceEqual(right.Points)
            && string.Equals(left.Color, right.Color, StringComparison.Ordinal)
            && Math.Abs(left.Thickness - right.Thickness) < 0.0001
            && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
            && ReferenceEquals(left.Image, right.Image)
            && Math.Abs(left.FontSize - right.FontSize) < 0.0001
            && left.IsTemporary == right.IsTemporary
            && Math.Abs(left.CreatedAtMs - right.CreatedAtMs) < 0.0001
            && left.TemporaryVisibleMs == right.TemporaryVisibleMs
            && left.TemporaryFadeMs == right.TemporaryFadeMs;
    }

    private static bool SnapshotsEqual(
        IReadOnlyList<AnnotationShape> left,
        IReadOnlyList<AnnotationShape> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!ShapesEqual(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }
}
