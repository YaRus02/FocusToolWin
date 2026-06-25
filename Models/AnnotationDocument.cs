using System.Globalization;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

internal sealed class AnnotationDocument
{
    private const int MaximumHistoryEntries = 100;

    private readonly List<AnnotationShape> _shapes = [];
    private readonly Stack<List<AnnotationShape>> _undo = new();
    private readonly Stack<List<AnnotationShape>> _redo = new();
    private readonly List<int> _selectedIndices = [];
    private readonly Func<double> _clockProvider;
    private bool _historyMayContainTemporaryAnnotations;
    private bool _movingSelection;
    private bool _selectionUndoPushed;

    public IReadOnlyList<AnnotationShape> Shapes => _shapes;
    public AnnotationShape? Draft { get; private set; }
    public ScreenRect? SelectionBounds { get; private set; }
    public ScreenRect? SelectionDraftBounds => Draft?.Tool == AnnotationTool.Move
        ? ScreenRect.FromPoints(Draft.Start, Draft.End)
        : null;
    public bool HasDraftText => Draft?.Tool == AnnotationTool.Text;
    public bool HasSelection => _selectedIndices.Count > 0 && SelectionBounds is not null;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // Raised for structural changes (add/commit/undo/redo/clear/delete) - listeners
    // do a full UI sync. DraftProgressed is the high-frequency in-progress signal
    // (a stroke or drag gaining a sample) and only needs an overlay repaint.
    public event EventHandler? Changed;
    public event EventHandler? DraftProgressed;

    public AnnotationDocument(Func<double> clockProvider)
    {
        _clockProvider = clockProvider;
    }

    public void BeginStroke(AnnotationTool tool, ScreenPoint start, AppSettings settings)
    {
        CommitTextDraft();
        ClearSelectionCore();

        Draft = new AnnotationShape
        {
            Tool = tool,
            Start = start,
            End = start,
            Color = settings.AnnotationColor,
            Thickness = settings.AnnotationThickness,
            FontSize = settings.AnnotationFontSize
        };
        Draft.ApplyFadingSettings(settings);

        if (tool is AnnotationTool.Pencil or AnnotationTool.Highlighter)
        {
            Draft.Points.Add(start);
        }

        OnChanged();
    }

    public void AddPointShape(AnnotationTool tool, ScreenPoint point, AppSettings settings)
    {
        if (tool != AnnotationTool.StepOval)
        {
            throw new ArgumentOutOfRangeException(nameof(tool), tool, "Only point-based annotation tools can be added directly.");
        }

        CommitTextDraft();
        ClearSelectionCore();
        PushUndo();

        var shape = new AnnotationShape
        {
            Tool = tool,
            Start = point,
            End = point,
            Color = settings.AnnotationColor,
            Thickness = settings.AnnotationThickness,
            FontSize = settings.AnnotationFontSize
        };
        shape.ApplyFadingSettings(settings);
        shape.MarkCreated(_clockProvider());
        _shapes.Add(shape);

        OnChanged();
    }

    public void UpdateStroke(ScreenPoint current, bool shift)
    {
        if (Draft is null || Draft.Tool is AnnotationTool.Text or AnnotationTool.Move)
        {
            return;
        }

        if (Draft.Tool is AnnotationTool.Pencil or AnnotationTool.Highlighter)
        {
            if (Draft.Points.Count == 0 || Draft.Points[^1].DistanceTo(current) >= 1.0)
            {
                Draft.Points.Add(current);
            }
        }
        else
        {
            Draft.End = ApplyConstraint(Draft.Tool, Draft.Start, current, shift);
        }

        OnDraftProgressed();
    }

    public void CommitStroke()
    {
        if (Draft is null || Draft.Tool is AnnotationTool.Text or AnnotationTool.Move)
        {
            return;
        }

        if (IsMeaningful(Draft))
        {
            PushUndo();
            Draft.MarkCreated(_clockProvider());
            _shapes.Add(Draft.Clone());
        }

        Draft = null;
        OnChanged();
    }

    public void BeginSelection(ScreenPoint start)
    {
        CommitTextDraft();
        ClearSelectionCore();

        Draft = new AnnotationShape
        {
            Tool = AnnotationTool.Move,
            Start = start,
            End = start
        };

        OnChanged();
    }

    public void UpdateSelection(ScreenPoint current)
    {
        if (Draft?.Tool != AnnotationTool.Move)
        {
            return;
        }

        Draft.End = current;
        OnDraftProgressed();
    }

    public void CommitSelection()
    {
        if (Draft?.Tool != AnnotationTool.Move)
        {
            return;
        }

        var selectionRect = ScreenRect.FromPoints(Draft.Start, Draft.End);
        Draft = null;
        ClearSelectionCore();

        if (selectionRect.Width >= 4 && selectionRect.Height >= 4)
        {
            for (var i = 0; i < _shapes.Count; i++)
            {
                if (_shapes[i].IntersectsSelection(selectionRect))
                {
                    _selectedIndices.Add(i);
                }
            }

            if (_selectedIndices.Count > 0)
            {
                SelectionBounds = selectionRect;
            }
        }

        OnChanged();
    }

    public bool BeginSelectionMove(ScreenPoint point)
    {
        CommitTextDraft();

        if (!HasSelection || SelectionBounds is not { } bounds || !bounds.Contains(point))
        {
            return false;
        }

        Draft = null;
        _movingSelection = true;
        _selectionUndoPushed = false;
        return true;
    }

    public void MoveSelectionBy(double dx, double dy)
    {
        if (!_movingSelection || _selectedIndices.Count == 0 || (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01))
        {
            return;
        }

        // The first move of a drag pushes an undo entry, which changes undo/redo
        // availability and needs a full UI sync; subsequent moves are pure motion.
        var pushedUndo = false;
        if (!_selectionUndoPushed)
        {
            PushUndo();
            _selectionUndoPushed = true;
            pushedUndo = true;
        }

        foreach (var index in _selectedIndices)
        {
            if (index >= 0 && index < _shapes.Count)
            {
                _shapes[index].Offset(dx, dy);
            }
        }

        if (SelectionBounds is { } bounds)
        {
            SelectionBounds = bounds.Offset(dx, dy);
        }

        if (pushedUndo)
        {
            OnChanged();
        }
        else
        {
            OnDraftProgressed();
        }
    }

    public void EndSelectionMove()
    {
        _movingSelection = false;
        _selectionUndoPushed = false;
    }

    public void ClearSelection()
    {
        if (ClearSelectionCore())
        {
            OnChanged();
        }
    }

    public bool DeleteSelection()
    {
        CommitTextDraft();

        if (!HasSelection)
        {
            return false;
        }

        var indices = _selectedIndices
            .Where(index => index >= 0 && index < _shapes.Count)
            .Distinct()
            .OrderByDescending(index => index)
            .ToList();

        if (indices.Count == 0)
        {
            ClearSelection();
            return false;
        }

        PushUndo();
        foreach (var index in indices)
        {
            _shapes.RemoveAt(index);
        }

        ClearSelectionCore();
        OnChanged();
        return true;
    }

    public void BeginText(ScreenPoint point, AppSettings settings)
    {
        CommitTextDraft();
        ClearSelectionCore();

        Draft = new AnnotationShape
        {
            Tool = AnnotationTool.Text,
            Start = point,
            End = point,
            Color = settings.AnnotationColor,
            Thickness = settings.AnnotationThickness,
            FontSize = settings.AnnotationFontSize,
            Text = string.Empty
        };
        Draft.ApplyFadingSettings(settings);

        OnChanged();
    }

    public void AppendText(string text)
    {
        if (Draft?.Tool != AnnotationTool.Text || string.IsNullOrEmpty(text))
        {
            return;
        }

        Draft.Text += text;
        OnChanged();
    }

    public void BackspaceText()
    {
        if (Draft?.Tool != AnnotationTool.Text || Draft.Text.Length == 0)
        {
            return;
        }

        var elements = StringInfo.ParseCombiningCharacters(Draft.Text);
        Draft.Text = elements.Length > 1
            ? Draft.Text[..elements[^1]]
            : string.Empty;
        OnChanged();
    }

    public void CommitTextDraft()
    {
        if (Draft?.Tool != AnnotationTool.Text)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(Draft.Text))
        {
            PushUndo();
            Draft.MarkCreated(_clockProvider());
            _shapes.Add(Draft.Clone());
        }

        Draft = null;
        OnChanged();
    }

    public void CancelDraft()
    {
        if (Draft is null)
        {
            return;
        }

        Draft = null;
        OnChanged();
    }

    public void Undo()
    {
        CommitTextDraft();
        EndSelectionMove();

        var nowMs = _clockProvider();
        var shouldNormalizeTemporaryState = HasTemporaryAnnotationsInCurrentOrHistory();
        var removedExpired = shouldNormalizeTemporaryState && RemoveExpiredTemporaryAnnotationsCore(nowMs);
        var historyChanged = shouldNormalizeTemporaryState && NormalizeHistory(nowMs);

        if (_undo.Count == 0)
        {
            if (removedExpired || historyChanged)
            {
                OnChanged();
            }

            return;
        }

        PushHistory(_redo, Snapshot(nowMs));
        Restore(_undo.Pop(), nowMs);
        OnChanged();
    }

    public void Redo()
    {
        CommitTextDraft();
        EndSelectionMove();

        var nowMs = _clockProvider();
        var shouldNormalizeTemporaryState = HasTemporaryAnnotationsInCurrentOrHistory();
        var removedExpired = shouldNormalizeTemporaryState && RemoveExpiredTemporaryAnnotationsCore(nowMs);
        var historyChanged = shouldNormalizeTemporaryState && NormalizeHistory(nowMs);

        if (_redo.Count == 0)
        {
            if (removedExpired || historyChanged)
            {
                OnChanged();
            }

            return;
        }

        PushHistory(_undo, Snapshot(nowMs));
        Restore(_redo.Pop(), nowMs);
        OnChanged();
    }

    public void Clear()
    {
        var hadDraft = Draft is not null;
        var hadSelection = ClearSelectionCore();
        Draft = null;
        if (_shapes.Count == 0)
        {
            if (hadDraft || hadSelection)
            {
                OnChanged();
            }

            return;
        }

        PushUndo();
        _shapes.Clear();
        OnChanged();
    }

    public bool HasFadingTemporaryAnnotations(double nowMs)
    {
        return _shapes.Any(shape => shape.IsFadeInProgress(nowMs));
    }

    public bool RemoveExpiredTemporaryAnnotations(double nowMs)
    {
        if (!RemoveExpiredTemporaryAnnotationsCore(nowMs))
        {
            return false;
        }

        NormalizeHistory(nowMs);
        OnChanged();
        return true;
    }

    private void PushUndo()
    {
        var nowMs = _clockProvider();
        if (HasTemporaryAnnotationsInCurrentOrHistory())
        {
            RemoveExpiredTemporaryAnnotationsCore(nowMs);
            _ = NormalizeHistory(nowMs);
        }

        PushHistory(_undo, Snapshot(nowMs));
        _redo.Clear();
        if (_historyMayContainTemporaryAnnotations)
        {
            RefreshHistoryTemporaryFlag();
        }
    }

    private void PushHistory(Stack<List<AnnotationShape>> history, List<AnnotationShape> snapshot)
    {
        PushBounded(history, snapshot);
        if (!_historyMayContainTemporaryAnnotations && snapshot.Any(shape => shape.IsTemporary))
        {
            _historyMayContainTemporaryAnnotations = true;
        }
    }

    private static void PushBounded(
        Stack<List<AnnotationShape>> history,
        List<AnnotationShape> snapshot)
    {
        history.Push(snapshot);
        if (history.Count <= MaximumHistoryEntries)
        {
            return;
        }

        var retained = history
            .Take(MaximumHistoryEntries)
            .Reverse()
            .ToArray();
        history.Clear();
        foreach (var item in retained)
        {
            history.Push(item);
        }
    }

    private List<AnnotationShape> Snapshot(double nowMs)
    {
        return _shapes
            .Where(shape => !shape.IsExpired(nowMs))
            .Select(shape => shape.Clone())
            .ToList();
    }

    private void Restore(IEnumerable<AnnotationShape> snapshot, double nowMs)
    {
        _shapes.Clear();
        _shapes.AddRange(snapshot
            .Where(shape => !shape.IsExpired(nowMs))
            .Select(shape => shape.Clone()));
        Draft = null;
        ClearSelectionCore();
    }

    private bool RemoveExpiredTemporaryAnnotationsCore(double nowMs)
    {
        var removed = false;
        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            if (_shapes[i].IsExpired(nowMs))
            {
                _shapes.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            ClearSelectionCore();
        }

        return removed;
    }

    private bool NormalizeHistory(double nowMs)
    {
        var current = Snapshot(nowMs);
        var changed = NormalizeHistoryStack(_undo, current, nowMs)
            | NormalizeHistoryStack(_redo, current, nowMs);
        RefreshHistoryTemporaryFlag();
        return changed;
    }

    private bool HasTemporaryAnnotationsInCurrentOrHistory()
    {
        return _historyMayContainTemporaryAnnotations || _shapes.Any(shape => shape.IsTemporary);
    }

    private void RefreshHistoryTemporaryFlag()
    {
        _historyMayContainTemporaryAnnotations = HistoryContainsTemporaryAnnotations(_undo)
            || HistoryContainsTemporaryAnnotations(_redo);
    }

    private static bool HistoryContainsTemporaryAnnotations(Stack<List<AnnotationShape>> history)
    {
        return history.Any(snapshot => snapshot.Any(shape => shape.IsTemporary));
    }

    private static bool NormalizeHistoryStack(
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

    private static bool ShapesEqual(AnnotationShape left, AnnotationShape right)
    {
        return left.Tool == right.Tool
            && left.Start.Equals(right.Start)
            && left.End.Equals(right.End)
            && left.Points.SequenceEqual(right.Points)
            && string.Equals(left.Color, right.Color, StringComparison.Ordinal)
            && Math.Abs(left.Thickness - right.Thickness) < 0.0001
            && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
            && Math.Abs(left.FontSize - right.FontSize) < 0.0001
            && left.IsTemporary == right.IsTemporary
            && Math.Abs(left.CreatedAtMs - right.CreatedAtMs) < 0.0001
            && left.TemporaryVisibleMs == right.TemporaryVisibleMs
            && left.TemporaryFadeMs == right.TemporaryFadeMs;
    }

    private bool ClearSelectionCore()
    {
        var hadSelection = _selectedIndices.Count > 0 || SelectionBounds is not null || _movingSelection;
        _selectedIndices.Clear();
        SelectionBounds = null;
        _movingSelection = false;
        _selectionUndoPushed = false;
        return hadSelection;
    }

    private static bool IsMeaningful(AnnotationShape shape)
    {
        return shape.Tool switch
        {
            AnnotationTool.Pencil or AnnotationTool.Highlighter => shape.Points.Count > 1,
            AnnotationTool.StepOval => true,
            _ => shape.Start.DistanceTo(shape.End) >= 2.0
        };
    }

    private static ScreenPoint ApplyConstraint(AnnotationTool tool, ScreenPoint start, ScreenPoint current, bool shift)
    {
        if (!shift)
        {
            return current;
        }

        var dx = current.X - start.X;
        var dy = current.Y - start.Y;

        if (tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.StepRect)
        {
            var size = Math.Max(Math.Abs(dx), Math.Abs(dy));
            // Use a non-zero sign so an axis-aligned drag (dx==0 or dy==0) still
            // produces a square instead of collapsing to a zero-width/height shape.
            var signX = dx < 0 ? -1 : 1;
            var signY = dy < 0 ? -1 : 1;
            return new ScreenPoint(start.X + signX * size, start.Y + signY * size);
        }

        if (tool == AnnotationTool.Line)
        {
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.01)
            {
                return current;
            }

            var snappedAngle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
            return new ScreenPoint(start.X + Math.Cos(snappedAngle) * length, start.Y + Math.Sin(snappedAngle) * length);
        }

        return current;
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnDraftProgressed()
    {
        DraftProgressed?.Invoke(this, EventArgs.Empty);
    }
}
