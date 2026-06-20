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

        if (tool is AnnotationTool.Pencil or AnnotationTool.Highlighter)
        {
            Draft.Points.Add(start);
        }

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

        if (_undo.Count == 0)
        {
            return;
        }

        PushBounded(_redo, Snapshot());
        Restore(_undo.Pop());
        OnChanged();
    }

    public void Redo()
    {
        CommitTextDraft();
        EndSelectionMove();

        if (_redo.Count == 0)
        {
            return;
        }

        PushBounded(_undo, Snapshot());
        Restore(_redo.Pop());
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

    private void PushUndo()
    {
        PushBounded(_undo, Snapshot());
        _redo.Clear();
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

    private List<AnnotationShape> Snapshot()
    {
        return _shapes.Select(shape => shape.Clone()).ToList();
    }

    private void Restore(IEnumerable<AnnotationShape> snapshot)
    {
        _shapes.Clear();
        _shapes.AddRange(snapshot.Select(shape => shape.Clone()));
        Draft = null;
        ClearSelectionCore();
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
        return (shape.Tool == AnnotationTool.Pencil || shape.Tool == AnnotationTool.Highlighter)
            ? shape.Points.Count > 1
            : shape.Start.DistanceTo(shape.End) >= 2.0;
    }

    private static ScreenPoint ApplyConstraint(AnnotationTool tool, ScreenPoint start, ScreenPoint current, bool shift)
    {
        if (!shift)
        {
            return current;
        }

        var dx = current.X - start.X;
        var dy = current.Y - start.Y;

        if (tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse)
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
