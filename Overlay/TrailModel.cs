namespace FocusTool.Win.Overlay;

internal sealed class TrailModel
{
    private readonly List<TrailPoint> _points = [];

    public IReadOnlyList<TrailPoint> Points => _points;
    public double LastMovementMs { get; private set; } = -1;

    public void AddPoint(ScreenPoint point, double nowMs)
    {
        _points.Add(new TrailPoint(point.X, point.Y, nowMs));
        LastMovementMs = nowMs;
    }

    public void TouchLastPoint(ScreenPoint point, double nowMs)
    {
        if (_points.Count == 0)
        {
            AddPoint(point, nowMs);
            return;
        }

        _points[^1] = new TrailPoint(point.X, point.Y, nowMs);
        LastMovementMs = nowMs;
    }

    public void Clear()
    {
        _points.Clear();
        LastMovementMs = -1;
    }

    public void TrimWhileMoving(double nowMs, int trailLengthMs)
    {
        RemoveOlderThan(nowMs - trailLengthMs);
    }

    public void TrimWhileStationary(int trailLengthMs)
    {
        if (LastMovementMs < 0)
        {
            _points.Clear();
            return;
        }

        RemoveOlderThan(LastMovementMs - trailLengthMs);
    }

    private void RemoveOlderThan(double cutoffMs)
    {
        var firstVisible = 0;
        while (firstVisible < _points.Count && _points[firstVisible].TimeMs < cutoffMs)
        {
            firstVisible++;
        }

        if (firstVisible > 0)
        {
            _points.RemoveRange(0, firstVisible);
        }
    }
}
