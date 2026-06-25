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
    private List<AnnotationShape>? _textEditUndoSnapshot;
    private AnnotationShape? _editingTextOriginal;
    private int _editingTextIndex = -1;
    private int _objectEditIndex = -1;
    private AnnotationEditHandle _objectEditHandle = AnnotationEditHandle.None;
    private bool _objectEditHandleUndoPushed;
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
    public bool IsEditingText => IsValidEditingTextIndex();
    public bool HasTextInput => HasDraftText || IsEditingText;
    public bool IsObjectEditing => IsValidObjectEditIndex();
    public AnnotationShape? ObjectEditShape => IsValidObjectEditIndex() ? _shapes[_objectEditIndex] : null;
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
        CommitTextInput();
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

        CommitTextInput();
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
        CommitTextInput();
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
        CommitTextInput();

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

    public bool TryHitObjectEditHandle(ScreenPoint point, out AnnotationEditHandle handle)
    {
        if (!IsValidObjectEditIndex())
        {
            handle = AnnotationEditHandle.None;
            return false;
        }

        return TryHitEditHandle(_shapes[_objectEditIndex], point, out handle);
    }

    public bool BeginObjectEditHandleDrag(AnnotationEditHandle handle)
    {
        if (!IsValidObjectEditIndex() || handle == AnnotationEditHandle.None)
        {
            return false;
        }

        _objectEditHandle = handle;
        _objectEditHandleUndoPushed = false;
        return true;
    }

    public void UpdateObjectEditHandleDrag(ScreenPoint point, bool shift)
    {
        if (!IsValidObjectEditIndex() || _objectEditHandle == AnnotationEditHandle.None)
        {
            return;
        }

        var shape = _shapes[_objectEditIndex];
        if (!CanResizeObjectEditShape(shape.Tool))
        {
            return;
        }

        if (!_objectEditHandleUndoPushed)
        {
            PushUndo();
            _objectEditHandleUndoPushed = true;
            OnChanged();
        }

        ResizeObjectEditShape(shape, _objectEditHandle, point, shift);
        SelectionBounds = shape.GetBounds();
        OnDraftProgressed();
    }

    public void EndObjectEditHandleDrag()
    {
        _objectEditHandle = AnnotationEditHandle.None;
        _objectEditHandleUndoPushed = false;
    }

    public void ClearSelection()
    {
        if (ClearSelectionCore())
        {
            OnChanged();
        }
    }

    public bool HitTestText(ScreenPoint point)
    {
        return TryFindTextAt(point, out _);
    }

    public bool HitTestShape(ScreenPoint point)
    {
        return TryFindShapeAt(point, out _);
    }

    public bool TryBeginTextEditAt(ScreenPoint point)
    {
        CommitTextInput();
        if (!TryFindTextAt(point, out var index))
        {
            return false;
        }

        _editingTextIndex = index;
        _editingTextOriginal = _shapes[index].Clone();
        _textEditUndoSnapshot = Snapshot(_clockProvider());
        SelectSingleIndexCore(index);
        OnChanged();
        return true;
    }

    public bool TryBeginObjectEditAt(ScreenPoint point)
    {
        CommitTextInput();
        if (!TryFindShapeAt(point, out var index))
        {
            return false;
        }

        SelectSingleIndexCore(index, objectEdit: true);
        OnChanged();
        return true;
    }

    public bool ObjectEditContains(ScreenPoint point)
    {
        return IsValidObjectEditIndex()
            && SelectionBounds is { } bounds
            && bounds.Contains(point);
    }

    public void EndObjectEdit()
    {
        if (!IsObjectEditing)
        {
            return;
        }

        ClearSelectionCore();
        OnChanged();
    }

    public bool TextEditContains(ScreenPoint point)
    {
        return IsValidEditingTextIndex() && _shapes[_editingTextIndex].GetBounds().Contains(point);
    }

    public bool IsTextBeingEdited(AnnotationShape shape)
    {
        return IsValidEditingTextIndex() && ReferenceEquals(_shapes[_editingTextIndex], shape);
    }

    public bool ApplyColorToSelection(string color)
    {
        if (IsValidEditingTextIndex())
        {
            _shapes[_editingTextIndex].Color = color;
            RefreshSelectionBoundsCore();
            OnChanged();
            return true;
        }

        var indices = SelectedShapeIndices().ToList();
        if (indices.Count == 0 || indices.All(index => string.Equals(_shapes[index].Color, color, StringComparison.Ordinal)))
        {
            return false;
        }

        PushUndo();
        foreach (var index in indices)
        {
            _shapes[index].Color = color;
        }

        RefreshSelectionBoundsCore();
        OnChanged();
        return true;
    }

    public bool AdjustSelectedTextFontSize(double delta)
    {
        if (Math.Abs(delta) < 0.001)
        {
            return false;
        }

        if (IsValidEditingTextIndex())
        {
            var shape = _shapes[_editingTextIndex];
            var next = ClampFontSize(shape.FontSize + delta);
            if (Math.Abs(shape.FontSize - next) < 0.001)
            {
                return false;
            }

            shape.FontSize = next;
            RefreshSelectionBoundsCore();
            OnChanged();
            return true;
        }

        var indices = SelectedShapeIndices()
            .Where(index => _shapes[index].Tool == AnnotationTool.Text)
            .ToList();
        if (indices.Count == 0)
        {
            return false;
        }

        var changed = indices.Any(index => Math.Abs(_shapes[index].FontSize - ClampFontSize(_shapes[index].FontSize + delta)) >= 0.001);
        if (!changed)
        {
            return false;
        }

        PushUndo();
        foreach (var index in indices)
        {
            _shapes[index].FontSize = ClampFontSize(_shapes[index].FontSize + delta);
        }

        RefreshSelectionBoundsCore();
        OnChanged();
        return true;
    }

    public bool AdjustSelectedThickness(double delta)
    {
        if (Math.Abs(delta) < 0.001)
        {
            return false;
        }

        var indices = SelectedShapeIndices()
            .Where(index => _shapes[index].Tool != AnnotationTool.Text)
            .ToList();
        if (indices.Count == 0)
        {
            return false;
        }

        var changed = indices.Any(index => Math.Abs(_shapes[index].Thickness - ClampThickness(_shapes[index].Thickness + delta)) >= 0.001);
        if (!changed)
        {
            return false;
        }

        PushUndo();
        foreach (var index in indices)
        {
            _shapes[index].Thickness = ClampThickness(_shapes[index].Thickness + delta);
        }

        RefreshSelectionBoundsCore();
        OnChanged();
        return true;
    }

    public bool DeleteSelection()
    {
        CommitTextInput();

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
        CommitTextInput();
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
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (Draft?.Tool == AnnotationTool.Text)
        {
            Draft.Text += text;
            OnChanged();
            return;
        }

        if (IsValidEditingTextIndex())
        {
            _shapes[_editingTextIndex].Text += text;
            RefreshSelectionBoundsCore();
            OnChanged();
        }
    }

    public void BackspaceText()
    {
        if (Draft?.Tool == AnnotationTool.Text)
        {
            if (Draft.Text.Length == 0)
            {
                return;
            }

            Draft.Text = RemoveLastTextElement(Draft.Text);
            OnChanged();
            return;
        }

        if (!IsValidEditingTextIndex() || _shapes[_editingTextIndex].Text.Length == 0)
        {
            return;
        }

        _shapes[_editingTextIndex].Text = RemoveLastTextElement(_shapes[_editingTextIndex].Text);
        RefreshSelectionBoundsCore();
        OnChanged();
    }

    public void DeleteText()
    {
        BackspaceText();
    }

    public void CommitTextInput()
    {
        if (IsEditingText)
        {
            CommitTextEdit();
            return;
        }

        CommitTextDraft();
    }

    public void CancelTextInput()
    {
        if (IsEditingText)
        {
            CancelTextEdit();
            return;
        }

        CancelDraft();
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

    public void CommitTextEdit()
    {
        if (!IsValidEditingTextIndex())
        {
            ClearTextEditCore();
            return;
        }

        var index = _editingTextIndex;
        var shape = _shapes[index];
        var removeShape = string.IsNullOrWhiteSpace(shape.Text);
        var changed = _editingTextOriginal is null || removeShape || !ShapesEqual(shape, _editingTextOriginal);
        if (changed && _textEditUndoSnapshot is not null)
        {
            PushUndoSnapshot(_textEditUndoSnapshot);
        }

        if (removeShape)
        {
            _shapes.RemoveAt(index);
            ClearSelectionCore();
        }
        else
        {
            SelectSingleIndexCore(index);
        }

        ClearTextEditCore(keepSelection: true);
        OnChanged();
    }

    public void CancelTextEdit()
    {
        if (!IsValidEditingTextIndex())
        {
            ClearTextEditCore();
            return;
        }

        var index = _editingTextIndex;
        if (_editingTextOriginal is not null)
        {
            _shapes[index] = _editingTextOriginal.Clone();
            SelectSingleIndexCore(index);
        }

        ClearTextEditCore(keepSelection: true);
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
        CommitTextInput();
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
        CommitTextInput();
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
        var hadTextEdit = IsEditingText;
        var hadSelection = ClearSelectionCore();
        Draft = null;
        ClearTextEditCore();
        if (_shapes.Count == 0)
        {
            if (hadDraft || hadTextEdit || hadSelection)
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

    private void PushUndoSnapshot(List<AnnotationShape> snapshot)
    {
        PushHistory(_undo, snapshot.Select(shape => shape.Clone()).ToList());
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

    private IEnumerable<int> SelectedShapeIndices()
    {
        return _selectedIndices
            .Where(index => index >= 0 && index < _shapes.Count)
            .Distinct();
    }

    private void Restore(IEnumerable<AnnotationShape> snapshot, double nowMs)
    {
        _shapes.Clear();
        _shapes.AddRange(snapshot
            .Where(shape => !shape.IsExpired(nowMs))
            .Select(shape => shape.Clone()));
        Draft = null;
        ClearTextEditCore();
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
        var hadSelection = _selectedIndices.Count > 0 || SelectionBounds is not null || _movingSelection || _objectEditIndex >= 0;
        _selectedIndices.Clear();
        SelectionBounds = null;
        _movingSelection = false;
        _selectionUndoPushed = false;
        _objectEditIndex = -1;
        _objectEditHandle = AnnotationEditHandle.None;
        _objectEditHandleUndoPushed = false;
        return hadSelection;
    }

    private bool TryFindShapeAt(ScreenPoint point, out int index)
    {
        var hitRect = new ScreenRect(point.X, point.Y, point.X, point.Y).Inflate(6);
        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            if (_shapes[i].Tool == AnnotationTool.Text
                ? _shapes[i].GetBounds().Contains(point)
                : _shapes[i].IntersectsSelection(hitRect))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool TryHitEditHandle(AnnotationShape shape, ScreenPoint point, out AnnotationEditHandle handle)
    {
        const double hitRadius = 10;
        var hitRadiusSquared = hitRadius * hitRadius;
        if (shape.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            if (DistanceSquared(point, shape.Start) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.Start;
                return true;
            }

            if (DistanceSquared(point, shape.End) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.End;
                return true;
            }
        }
        else if (shape.Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.StepRect)
        {
            var rect = ScreenRect.FromPoints(shape.Start, shape.End);
            if (DistanceSquared(point, new ScreenPoint(rect.Left, rect.Top)) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.TopLeft;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Right, rect.Top)) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.TopRight;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Left, rect.Bottom)) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.BottomLeft;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Right, rect.Bottom)) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.BottomRight;
                return true;
            }
        }

        handle = AnnotationEditHandle.None;
        return false;
    }

    private static void ResizeObjectEditShape(AnnotationShape shape, AnnotationEditHandle handle, ScreenPoint point, bool shift)
    {
        if (shape.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            if (handle == AnnotationEditHandle.Start)
            {
                var nextStart = shift ? ApplyConstraint(AnnotationTool.Line, shape.End, point, shift: true) : point;
                shape.SetEndpoints(nextStart, shape.End);
            }
            else if (handle == AnnotationEditHandle.End)
            {
                var nextEnd = shift ? ApplyConstraint(AnnotationTool.Line, shape.Start, point, shift: true) : point;
                shape.SetEndpoints(shape.Start, nextEnd);
            }

            return;
        }

        if (shape.Tool is not (AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.StepRect))
        {
            return;
        }

        var rect = ScreenRect.FromPoints(shape.Start, shape.End);
        var anchor = handle switch
        {
            AnnotationEditHandle.TopLeft => new ScreenPoint(rect.Right, rect.Bottom),
            AnnotationEditHandle.TopRight => new ScreenPoint(rect.Left, rect.Bottom),
            AnnotationEditHandle.BottomLeft => new ScreenPoint(rect.Right, rect.Top),
            AnnotationEditHandle.BottomRight => new ScreenPoint(rect.Left, rect.Top),
            _ => shape.Start
        };
        var next = ApplyConstraint(shape.Tool, anchor, point, shift);
        shape.SetEndpoints(anchor, next);
    }

    private static bool CanResizeObjectEditShape(AnnotationTool tool)
    {
        return tool is AnnotationTool.Rectangle
            or AnnotationTool.Ellipse
            or AnnotationTool.StepRect
            or AnnotationTool.Line
            or AnnotationTool.Arrow;
    }

    private static double DistanceSquared(ScreenPoint first, ScreenPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private bool TryFindTextAt(ScreenPoint point, out int index)
    {
        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            if (_shapes[i].Tool == AnnotationTool.Text && _shapes[i].GetBounds().Contains(point))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private void SelectSingleIndexCore(int index, bool objectEdit = false)
    {
        ClearSelectionCore();
        if (index < 0 || index >= _shapes.Count)
        {
            return;
        }

        _selectedIndices.Add(index);
        _objectEditIndex = objectEdit ? index : -1;
        SelectionBounds = _shapes[index].GetBounds();
    }

    private void RefreshSelectionBoundsCore()
    {
        ScreenRect? bounds = null;
        foreach (var index in SelectedShapeIndices())
        {
            bounds = bounds is { } current
                ? current.Union(_shapes[index].GetBounds())
                : _shapes[index].GetBounds();
        }

        SelectionBounds = bounds;
    }

    private bool IsValidEditingTextIndex()
    {
        return _editingTextIndex >= 0
            && _editingTextIndex < _shapes.Count
            && _shapes[_editingTextIndex].Tool == AnnotationTool.Text;
    }

    private bool IsValidObjectEditIndex()
    {
        return _objectEditIndex >= 0
            && _objectEditIndex < _shapes.Count
            && _selectedIndices.Count == 1
            && _selectedIndices[0] == _objectEditIndex;
    }

    private void ClearTextEditCore(bool keepSelection = false)
    {
        _editingTextIndex = -1;
        _editingTextOriginal = null;
        _textEditUndoSnapshot = null;
        if (!keepSelection)
        {
            ClearSelectionCore();
        }
    }

    private static string RemoveLastTextElement(string text)
    {
        var elements = StringInfo.ParseCombiningCharacters(text);
        return elements.Length > 1
            ? text[..elements[^1]]
            : string.Empty;
    }

    private static double ClampFontSize(double value)
    {
        return Math.Clamp(value, 8, 96);
    }

    private static double ClampThickness(double value)
    {
        return Math.Clamp(value, 1, 32);
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
