using System.Windows.Input;
using FocusTool.Win.Models;
using FocusTool.Win.Overlay;
using Forms = System.Windows.Forms;

namespace FocusTool.Win.Services;

internal sealed class AnnotationMouseController
{
    private readonly AnnotationDocument _annotations;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<AnnotationTool> _currentToolProvider;
    private readonly Func<double> _clock;
    private readonly Action _tryCompletePushToAnnotateExit;
    private readonly double _movementThresholdPixels;
    private ScreenPoint _lastSelectionMovePoint;
    private ScreenPoint _lastTextClickPoint;
    private ScreenPoint _lastObjectClickPoint;
    private ScreenPoint _pendingTextEditMovePoint;
    private bool _hasLastTextClick;
    private bool _hasLastObjectClick;
    private double _lastTextClickMs = double.NegativeInfinity;
    private double _lastObjectClickMs = double.NegativeInfinity;
    private bool _drawing;
    private bool _movingSelection;
    private bool _draggingAnnotationEditHandle;
    private bool _pendingTextEditMove;

    public AnnotationMouseController(
        AnnotationDocument annotations,
        Func<AppSettings> settingsProvider,
        Func<AnnotationTool> currentToolProvider,
        Func<double> clock,
        Action tryCompletePushToAnnotateExit,
        double movementThresholdPixels)
    {
        _annotations = annotations;
        _settingsProvider = settingsProvider;
        _currentToolProvider = currentToolProvider;
        _clock = clock;
        _tryCompletePushToAnnotateExit = tryCompletePushToAnnotateExit;
        _movementThresholdPixels = movementThresholdPixels;
    }

    public bool HasActiveOperation => _drawing || _movingSelection || _draggingAnnotationEditHandle;

    public void OnLeavingAnnotationInput()
    {
        if (_annotations.HasTextInput)
        {
            _annotations.CommitTextInput();
        }
        else
        {
            _annotations.CancelDraft();
        }

        _drawing = false;
        _movingSelection = false;
        _annotations.EndSelectionMove();
    }

    public void HandleMouseDown(ScreenPoint point)
    {
        var currentTool = _currentToolProvider();
        var settings = _settingsProvider();

        if (HandleTextObjectClick(point, currentTool))
        {
            return;
        }

        if (HandleObjectEditClick(point, currentTool))
        {
            return;
        }

        if (IsStepTool(currentTool) && _annotations.HitTestStep(point))
        {
            return;
        }

        if (currentTool == AnnotationTool.StepOval)
        {
            _annotations.AddPointShape(AnnotationTool.StepOval, point, settings);
            return;
        }

        if (currentTool == AnnotationTool.Text)
        {
            if (_annotations.HasDraftText)
            {
                _annotations.CommitTextDraft();
                _tryCompletePushToAnnotateExit();
                return;
            }

            _annotations.BeginText(point, settings);
            return;
        }

        if (currentTool == AnnotationTool.Move)
        {
            if (_annotations.BeginSelectionMove(point))
            {
                _movingSelection = true;
                _lastSelectionMovePoint = point;
                return;
            }

            _drawing = true;
            _annotations.BeginSelection(point);
            return;
        }

        _drawing = true;
        _annotations.BeginStroke(currentTool, point, settings);
    }

    public void HandleMouseMove(ScreenPoint point, ModifierKeys modifiers)
    {
        var currentTool = _currentToolProvider();

        if (_pendingTextEditMove)
        {
            if (point.DistanceTo(_pendingTextEditMovePoint) < _movementThresholdPixels * 4)
            {
                return;
            }

            _annotations.CommitTextInput();
            if (_annotations.BeginSelectionMove(_pendingTextEditMovePoint))
            {
                _movingSelection = true;
                _lastSelectionMovePoint = _pendingTextEditMovePoint;
                _annotations.MoveSelectionBy(point.X - _lastSelectionMovePoint.X, point.Y - _lastSelectionMovePoint.Y);
                _lastSelectionMovePoint = point;
            }

            _pendingTextEditMove = false;
            return;
        }

        if (_draggingAnnotationEditHandle)
        {
            _annotations.UpdateObjectEditHandleDrag(point, (modifiers & ModifierKeys.Shift) != 0);
            return;
        }

        if (_movingSelection)
        {
            _annotations.MoveSelectionBy(point.X - _lastSelectionMovePoint.X, point.Y - _lastSelectionMovePoint.Y);
            _lastSelectionMovePoint = point;
            return;
        }

        if (!_drawing)
        {
            return;
        }

        if (currentTool == AnnotationTool.Move)
        {
            _annotations.UpdateSelection(point);
            return;
        }

        _annotations.UpdateStroke(point, (modifiers & ModifierKeys.Shift) != 0);
    }

    public void HandleMouseUp(ScreenPoint point, ModifierKeys modifiers)
    {
        var currentTool = _currentToolProvider();

        if (_pendingTextEditMove)
        {
            _pendingTextEditMove = false;
            return;
        }

        if (_draggingAnnotationEditHandle)
        {
            _annotations.EndObjectEditHandleDrag();
            _draggingAnnotationEditHandle = false;
            _tryCompletePushToAnnotateExit();
            return;
        }

        if (_movingSelection)
        {
            _annotations.EndSelectionMove();
            _movingSelection = false;
            _tryCompletePushToAnnotateExit();
            return;
        }

        if (!_drawing)
        {
            return;
        }

        if (currentTool == AnnotationTool.Move)
        {
            _annotations.UpdateSelection(point);
            _annotations.CommitSelection();
            _drawing = false;
            _tryCompletePushToAnnotateExit();
            return;
        }

        _annotations.UpdateStroke(point, (modifiers & ModifierKeys.Shift) != 0);
        _annotations.CommitStroke();
        _drawing = false;
        _tryCompletePushToAnnotateExit();
    }

    public void HandleCaptureLost()
    {
        if (_movingSelection)
        {
            _annotations.EndSelectionMove();
            _movingSelection = false;
        }

        _pendingTextEditMove = false;

        if (_draggingAnnotationEditHandle)
        {
            _annotations.EndObjectEditHandleDrag();
            _draggingAnnotationEditHandle = false;
        }

        if (_drawing)
        {
            _annotations.CancelDraft();
            _drawing = false;
        }

        _tryCompletePushToAnnotateExit();
    }

    private bool HandleTextObjectClick(ScreenPoint point, AnnotationTool currentTool)
    {
        if (_annotations.IsEditingText)
        {
            if (!_annotations.TextEditContains(point))
            {
                _annotations.CommitTextInput();
                _pendingTextEditMove = false;
                ResetTextClickTracking();
                return true;
            }

            _pendingTextEditMove = true;
            _pendingTextEditMovePoint = point;
            ResetTextClickTracking();
            return true;
        }

        if (!_annotations.HitTestText(point))
        {
            ResetTextClickTracking();
            return false;
        }

        if (IsTextDoubleClick(point))
        {
            _annotations.CancelDraft();
            _annotations.TryBeginTextEditAt(point);
            ResetTextClickTracking();
            return true;
        }

        _lastTextClickPoint = point;
        _lastTextClickMs = _clock();
        _hasLastTextClick = true;
        ResetObjectClickTracking();
        if (_annotations.IsObjectEditing && _annotations.ObjectEditContains(point))
        {
            return false;
        }

        return currentTool != AnnotationTool.Move;
    }

    private bool HandleObjectEditClick(ScreenPoint point, AnnotationTool currentTool)
    {
        if (_annotations.IsObjectEditing)
        {
            if (_annotations.TryHitObjectEditHandle(point, out var handle)
                && _annotations.BeginObjectEditHandleDrag(handle))
            {
                _draggingAnnotationEditHandle = true;
                ResetObjectClickTracking();
                return true;
            }

            if (!_annotations.ObjectEditContains(point))
            {
                _annotations.EndObjectEdit();
                ResetObjectClickTracking();
                return true;
            }

            if (_annotations.BeginSelectionMove(point))
            {
                _movingSelection = true;
                _lastSelectionMovePoint = point;
                ResetObjectClickTracking();
                return true;
            }
        }

        if (currentTool == AnnotationTool.Move && _annotations.BeginSelectionMove(point))
        {
            _movingSelection = true;
            _lastSelectionMovePoint = point;
            ResetObjectClickTracking();
            return true;
        }

        if (!_annotations.HitTestShape(point))
        {
            ResetObjectClickTracking();
            return false;
        }

        if (IsObjectDoubleClick(point))
        {
            _annotations.CancelDraft();
            _annotations.TryBeginObjectEditAt(point);
            ResetObjectClickTracking();
            return true;
        }

        _lastObjectClickPoint = point;
        _lastObjectClickMs = _clock();
        _hasLastObjectClick = true;
        ResetTextClickTracking();
        return false;
    }

    private bool IsTextDoubleClick(ScreenPoint point)
    {
        if (!_hasLastTextClick)
        {
            return false;
        }

        var elapsedMs = _clock() - _lastTextClickMs;
        var size = Forms.SystemInformation.DoubleClickSize;
        return elapsedMs >= 0
            && elapsedMs <= Forms.SystemInformation.DoubleClickTime
            && Math.Abs(point.X - _lastTextClickPoint.X) <= size.Width / 2.0
            && Math.Abs(point.Y - _lastTextClickPoint.Y) <= size.Height / 2.0;
    }

    private bool IsObjectDoubleClick(ScreenPoint point)
    {
        if (!_hasLastObjectClick)
        {
            return false;
        }

        var elapsedMs = _clock() - _lastObjectClickMs;
        var size = Forms.SystemInformation.DoubleClickSize;
        return elapsedMs >= 0
            && elapsedMs <= Forms.SystemInformation.DoubleClickTime
            && Math.Abs(point.X - _lastObjectClickPoint.X) <= size.Width / 2.0
            && Math.Abs(point.Y - _lastObjectClickPoint.Y) <= size.Height / 2.0;
    }

    private void ResetTextClickTracking()
    {
        _hasLastTextClick = false;
        _lastTextClickMs = double.NegativeInfinity;
    }

    private void ResetObjectClickTracking()
    {
        _hasLastObjectClick = false;
        _lastObjectClickMs = double.NegativeInfinity;
    }

    private static bool IsStepTool(AnnotationTool tool)
    {
        return tool is AnnotationTool.StepOval or AnnotationTool.StepRect;
    }
}
