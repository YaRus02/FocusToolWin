using System.Globalization;
using System.Windows.Media.Imaging;
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
            Draft.End = AnnotationGeometry.ApplyConstraint(Draft.Tool, Draft.Start, current, shift);
        }

        OnDraftProgressed();
    }

    public void CommitStroke()
    {
        if (Draft is null || Draft.Tool is AnnotationTool.Text or AnnotationTool.Move)
        {
            return;
        }

        if (AnnotationGeometry.IsMeaningful(Draft))
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
                RefreshSelectionBoundsCore();
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

        return AnnotationHitTesting.TryHitEditHandle(_shapes[_objectEditIndex], point, out handle);
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
        if (!AnnotationGeometry.CanResize(shape.Tool))
        {
            return;
        }

        if (!_objectEditHandleUndoPushed)
        {
            PushUndo();
            _objectEditHandleUndoPushed = true;
            OnChanged();
        }

        AnnotationGeometry.ResizeShape(shape, _objectEditHandle, point, shift);
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
        return AnnotationHitTesting.TryFindTextAt(_shapes, point, out _);
    }

    public bool HitTestShape(ScreenPoint point)
    {
        return AnnotationHitTesting.TryFindShapeAt(_shapes, point, out _);
    }

    public bool HitTestStep(ScreenPoint point)
    {
        return AnnotationHitTesting.TryFindShapeAt(_shapes, point, out var index)
            && _shapes[index].Tool is AnnotationTool.StepOval or AnnotationTool.StepRect;
    }

    public bool TryBeginTextEditAt(ScreenPoint point)
    {
        CommitTextInput();
        if (!AnnotationHitTesting.TryFindTextAt(_shapes, point, out var index))
        {
            return false;
        }

        _editingTextIndex = index;
        _editingTextOriginal = _shapes[index].Clone();
        _textEditUndoSnapshot = Snapshot(_clockProvider());
        SelectSingleIndexCore(index, objectEdit: true);
        OnChanged();
        return true;
    }

    public bool TryBeginObjectEditAt(ScreenPoint point)
    {
        CommitTextInput();
        if (!AnnotationHitTesting.TryFindShapeAt(_shapes, point, out var index))
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

        var indices = SelectedShapeIndices()
            .Where(index => _shapes[index].Tool != AnnotationTool.Image)
            .ToList();
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
            var next = AnnotationGeometry.ClampFontSize(shape.FontSize + delta);
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

        var changed = indices.Any(index => Math.Abs(_shapes[index].FontSize - AnnotationGeometry.ClampFontSize(_shapes[index].FontSize + delta)) >= 0.001);
        if (!changed)
        {
            return false;
        }

        PushUndo();
        foreach (var index in indices)
        {
            _shapes[index].FontSize = AnnotationGeometry.ClampFontSize(_shapes[index].FontSize + delta);
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
            .Where(index => _shapes[index].Tool is not AnnotationTool.Text and not AnnotationTool.Image)
            .ToList();
        if (indices.Count == 0)
        {
            return false;
        }

        var changed = indices.Any(index => Math.Abs(_shapes[index].Thickness - AnnotationGeometry.ClampThickness(_shapes[index].Thickness + delta)) >= 0.001);
        if (!changed)
        {
            return false;
        }

        PushUndo();
        foreach (var index in indices)
        {
            _shapes[index].Thickness = AnnotationGeometry.ClampThickness(_shapes[index].Thickness + delta);
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

    public bool AddPastedText(string text, ScreenPoint point, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        CommitTextInput();
        ClearSelectionCore();
        PushUndo();

        var shape = new AnnotationShape
        {
            Tool = AnnotationTool.Text,
            Start = point,
            End = point,
            Color = settings.AnnotationColor,
            Thickness = settings.AnnotationThickness,
            FontSize = settings.AnnotationFontSize,
            Text = text.Replace("\r\n", "\n").Replace('\r', '\n')
        };
        shape.MarkCreated(_clockProvider());
        _shapes.Add(shape);
        SelectSingleIndexCore(_shapes.Count - 1, objectEdit: true);

        OnChanged();
        return true;
    }

    public bool AddPastedImage(BitmapSource image, ScreenRect rect)
    {
        if (rect.Width < 1 || rect.Height < 1)
        {
            return false;
        }

        CommitTextInput();
        ClearSelectionCore();
        PushUndo();

        var shape = new AnnotationShape
        {
            Tool = AnnotationTool.Image,
            Start = new ScreenPoint(rect.Left, rect.Top),
            End = new ScreenPoint(rect.Right, rect.Bottom),
            Image = image
        };
        shape.MarkCreated(_clockProvider());
        _shapes.Add(shape);
        SelectSingleIndexCore(_shapes.Count - 1, objectEdit: true);

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
        var changed = _editingTextOriginal is null || removeShape || !AnnotationHistory.ShapesEqual(shape, _editingTextOriginal);
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
            SelectSingleIndexCore(index, objectEdit: true);
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
            SelectSingleIndexCore(index, objectEdit: true);
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
        AnnotationHistory.PushBounded(history, snapshot, MaximumHistoryEntries);
        if (!_historyMayContainTemporaryAnnotations && snapshot.Any(shape => shape.IsTemporary))
        {
            _historyMayContainTemporaryAnnotations = true;
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
        var changed = AnnotationHistory.NormalizeStack(_undo, current, nowMs)
            | AnnotationHistory.NormalizeStack(_redo, current, nowMs);
        RefreshHistoryTemporaryFlag();
        return changed;
    }

    private bool HasTemporaryAnnotationsInCurrentOrHistory()
    {
        return _historyMayContainTemporaryAnnotations || _shapes.Any(shape => shape.IsTemporary);
    }

    private void RefreshHistoryTemporaryFlag()
    {
        _historyMayContainTemporaryAnnotations = AnnotationHistory.HistoryContainsTemporaryAnnotations(_undo)
            || AnnotationHistory.HistoryContainsTemporaryAnnotations(_redo);
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

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnDraftProgressed()
    {
        DraftProgressed?.Invoke(this, EventArgs.Empty);
    }
}
