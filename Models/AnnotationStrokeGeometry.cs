using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

// Pure screen-space geometry shared by live rendering, snapshots and cheap
// verification checks. Raw input points remain untouched on AnnotationShape.
internal static class AnnotationStrokeGeometry
{
    public static IReadOnlyList<ScreenPoint> Smooth(
        IReadOnlyList<ScreenPoint> rawPoints,
        StrokeSmoothingLevel level,
        bool finalize = false)
    {
        if (level == StrokeSmoothingLevel.Off || rawPoints.Count < 3)
        {
            return rawPoints;
        }

        var spacing = level == StrokeSmoothingLevel.Strong ? 1.4 : 1.8;
        var sampled = ResampleUniform(rawPoints, spacing);
        if (sampled.Count < 3)
        {
            return sampled;
        }

        var cornerRadius = Math.Max(2, (int)Math.Round(8 / spacing));
        var protectedCorners = FindProtectedCorners(sampled, cornerRadius);
        var radius = level switch
        {
            StrokeSmoothingLevel.Strong when finalize => 7,
            StrokeSmoothingLevel.Strong => 4,
            _ when finalize => 4,
            _ => 2
        };
        var passes = level switch
        {
            StrokeSmoothingLevel.Strong when finalize => 3,
            StrokeSmoothingLevel.Strong => 2,
            _ when finalize => 2,
            _ => 1
        };
        var strength = level == StrokeSmoothingLevel.Strong ? 0.88 : 0.76;
        IReadOnlyList<ScreenPoint> result = sampled;
        for (var pass = 0; pass < passes; pass++)
        {
            result = SmoothPass(result, protectedCorners, radius, strength, finalize);
        }

        // Input and output must remain anchored to the actual gesture. This also
        // prevents the finalization pass from making a completed stroke jump.
        var anchored = result.ToArray();
        anchored[0] = rawPoints[0];
        anchored[^1] = rawPoints[^1];
        return anchored;
    }

    private static IReadOnlyList<ScreenPoint> SmoothPass(
        IReadOnlyList<ScreenPoint> points,
        IReadOnlySet<int> protectedCorners,
        int radius,
        double strength,
        bool finalize)
    {
        var result = new ScreenPoint[points.Count];
        result[0] = points[0];
        result[^1] = points[^1];
        var sigma = Math.Max(1, radius * 0.52);
        var denominator = 2 * sigma * sigma;
        for (var i = 1; i < points.Count - 1; i++)
        {
            if (protectedCorners.Contains(i))
            {
                result[i] = points[i];
                continue;
            }

            var start = Math.Max(0, i - radius);
            var end = Math.Min(points.Count - 1, i + radius);
            foreach (var corner in protectedCorners)
            {
                if (corner < i)
                {
                    start = Math.Max(start, corner);
                }
                else if (corner > i)
                {
                    end = Math.Min(end, corner);
                }
            }

            var totalWeight = 0.0;
            var x = 0.0;
            var y = 0.0;
            for (var sample = start; sample <= end; sample++)
            {
                var distance = sample - i;
                var weight = Math.Exp(-(distance * distance) / denominator);
                totalWeight += weight;
                x += points[sample].X * weight;
                y += points[sample].Y * weight;
            }

            var appliedStrength = strength;
            if (!finalize)
            {
                // Keep the newest part of the live stroke responsive. Once the
                // gesture ends the final pass removes the remaining tail noise.
                var edgeDistance = Math.Min(i, points.Count - 1 - i);
                appliedStrength *= Math.Min(1, edgeDistance / (double)Math.Max(1, radius));
            }

            var averageX = x / totalWeight;
            var averageY = y / totalWeight;
            result[i] = new ScreenPoint(
                points[i].X + (averageX - points[i].X) * appliedStrength,
                points[i].Y + (averageY - points[i].Y) * appliedStrength);
        }

        return result;
    }

    public static FixedNibGeometry BuildFixedNibGeometry(
        IReadOnlyList<ScreenPoint> centerLine,
        double nibWidth,
        double nibHeight)
    {
        if (centerLine.Count == 0)
        {
            return FixedNibGeometry.Empty;
        }

        var halfWidth = Math.Max(0.5, nibWidth / 2);
        var halfHeight = Math.Max(0.5, nibHeight / 2);
        var figureCapacity = Math.Max(1, centerLine.Count - 1);
        var points = new List<ScreenPoint>(figureCapacity * 6);
        var figureEnds = new List<int>(figureCapacity);
        if (centerLine.Count == 1)
        {
            AppendRectangle(centerLine[0], halfWidth, halfHeight, points);
            figureEnds.Add(points.Count);
            return new FixedNibGeometry(points, figureEnds);
        }

        for (var i = 1; i < centerLine.Count; i++)
        {
            var start = centerLine[i - 1];
            var end = centerLine[i];
            if (start.DistanceTo(end) < 0.001)
            {
                continue;
            }

            AppendSweepHull(start, end, halfWidth, halfHeight, points);
            figureEnds.Add(points.Count);
        }

        if (figureEnds.Count == 0)
        {
            AppendRectangle(centerLine[0], halfWidth, halfHeight, points);
            figureEnds.Add(points.Count);
        }

        return new FixedNibGeometry(points, figureEnds);
    }

    private static List<ScreenPoint> ResampleUniform(IReadOnlyList<ScreenPoint> points, double spacing)
    {
        var result = new List<ScreenPoint>(points.Count) { points[0] };
        var previous = points[0];
        var distanceSinceSample = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var end = points[i];
            var segmentLength = previous.DistanceTo(end);
            while (segmentLength > 0.0001 && distanceSinceSample + segmentLength >= spacing)
            {
                var distanceToSample = spacing - distanceSinceSample;
                var ratio = distanceToSample / segmentLength;
                previous = new ScreenPoint(
                    previous.X + (end.X - previous.X) * ratio,
                    previous.Y + (end.Y - previous.Y) * ratio);
                result.Add(previous);
                segmentLength = previous.DistanceTo(end);
                distanceSinceSample = 0;
            }

            distanceSinceSample += segmentLength;
            previous = end;
        }

        if (result[^1].DistanceTo(points[^1]) > 0.001)
        {
            result.Add(points[^1]);
        }
        else
        {
            result[^1] = points[^1];
        }

        return result;
    }

    private static IReadOnlySet<int> FindProtectedCorners(IReadOnlyList<ScreenPoint> points, int radius)
    {
        var candidates = new List<(int Index, double Cosine)>();
        for (var i = radius; i < points.Count - radius; i++)
        {
            var cosine = TurnCosine(points[i - radius], points[i], points[i + radius]);
            if (cosine < 0.18)
            {
                candidates.Add((i, cosine));
            }
        }

        var result = new HashSet<int>();
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Cosine))
        {
            if (result.All(index => Math.Abs(index - candidate.Index) > radius))
            {
                result.Add(candidate.Index);
            }
        }

        return result;
    }

    private static double TurnCosine(ScreenPoint previous, ScreenPoint current, ScreenPoint next)
    {
        var firstX = current.X - previous.X;
        var firstY = current.Y - previous.Y;
        var secondX = next.X - current.X;
        var secondY = next.Y - current.Y;
        var firstLength = Math.Sqrt(firstX * firstX + firstY * firstY);
        var secondLength = Math.Sqrt(secondX * secondX + secondY * secondY);
        return firstLength < 0.001 || secondLength < 0.001
            ? 1
            : (firstX * secondX + firstY * secondY) / (firstLength * secondLength);
    }

    private static void AppendRectangle(
        ScreenPoint center,
        double halfWidth,
        double halfHeight,
        ICollection<ScreenPoint> output)
    {
        output.Add(center.Offset(-halfWidth, -halfHeight));
        output.Add(center.Offset(halfWidth, -halfHeight));
        output.Add(center.Offset(halfWidth, halfHeight));
        output.Add(center.Offset(-halfWidth, halfHeight));
    }

    private static void AppendSweepHull(
        ScreenPoint start,
        ScreenPoint end,
        double halfWidth,
        double halfHeight,
        ICollection<ScreenPoint> output)
    {
        // Stack storage keeps live highlighter rendering allocation-free per
        // segment. Only the two flat output buffers grow with stroke length.
        Span<ScreenPoint> candidates = stackalloc ScreenPoint[8];
        candidates[0] = start.Offset(-halfWidth, -halfHeight);
        candidates[1] = start.Offset(halfWidth, -halfHeight);
        candidates[2] = start.Offset(halfWidth, halfHeight);
        candidates[3] = start.Offset(-halfWidth, halfHeight);
        candidates[4] = end.Offset(-halfWidth, -halfHeight);
        candidates[5] = end.Offset(halfWidth, -halfHeight);
        candidates[6] = end.Offset(halfWidth, halfHeight);
        candidates[7] = end.Offset(-halfWidth, halfHeight);
        SortPoints(candidates);

        var uniqueCount = 1;
        for (var i = 1; i < candidates.Length; i++)
        {
            if (candidates[i] != candidates[uniqueCount - 1])
            {
                candidates[uniqueCount++] = candidates[i];
            }
        }

        Span<ScreenPoint> hull = stackalloc ScreenPoint[16];
        var hullCount = 0;
        for (var i = 0; i < uniqueCount; i++)
        {
            while (hullCount >= 2 && Cross(hull[hullCount - 2], hull[hullCount - 1], candidates[i]) <= 0)
            {
                hullCount--;
            }

            hull[hullCount++] = candidates[i];
        }

        var upperStart = hullCount + 1;
        for (var i = uniqueCount - 2; i >= 0; i--)
        {
            while (hullCount >= upperStart && Cross(hull[hullCount - 2], hull[hullCount - 1], candidates[i]) <= 0)
            {
                hullCount--;
            }

            hull[hullCount++] = candidates[i];
        }

        if (hullCount > 1)
        {
            hullCount--;
        }

        for (var i = 0; i < hullCount; i++)
        {
            output.Add(hull[i]);
        }
    }

    private static void SortPoints(Span<ScreenPoint> points)
    {
        for (var i = 1; i < points.Length; i++)
        {
            var current = points[i];
            var index = i - 1;
            while (index >= 0 && ComparePoints(points[index], current) > 0)
            {
                points[index + 1] = points[index];
                index--;
            }

            points[index + 1] = current;
        }
    }

    private static int ComparePoints(ScreenPoint left, ScreenPoint right)
    {
        var xComparison = left.X.CompareTo(right.X);
        return xComparison != 0 ? xComparison : left.Y.CompareTo(right.Y);
    }

    private static double Cross(ScreenPoint origin, ScreenPoint first, ScreenPoint second)
    {
        return (first.X - origin.X) * (second.Y - origin.Y)
            - (first.Y - origin.Y) * (second.X - origin.X);
    }
}

internal readonly record struct FixedNibGeometry(
    IReadOnlyList<ScreenPoint> Points,
    IReadOnlyList<int> FigureEnds)
{
    public static FixedNibGeometry Empty { get; } = new([], []);
    public bool IsEmpty => FigureEnds.Count == 0;
}
