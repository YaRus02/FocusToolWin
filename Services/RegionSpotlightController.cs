using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class RegionSpotlightController
{
    private readonly List<ScreenRect> _regions = [];
    private readonly RectEditSession _edit = new();

    public IReadOnlyList<ScreenRect> Regions => _regions;
    public int Count => _regions.Count;
    public bool HasRegions => _regions.Count > 0;
    public int SelectedIndex { get; private set; } = -1;
    public bool IsMoving => IsValidIndex(SelectedIndex) && _edit.IsMoving;
    public bool IsResizing => IsValidIndex(SelectedIndex) && _edit.IsResizing;

    public void SelectLast()
    {
        SelectedIndex = _regions.Count - 1;
        _edit.Cancel();
    }

    public void Clear()
    {
        _regions.Clear();
        ResetEditState();
    }

    public void ResetEditState()
    {
        SelectedIndex = -1;
        _edit.Cancel();
    }

    public void CancelEdit()
    {
        _edit.Cancel();
    }

    public void ClearSelection()
    {
        SelectedIndex = -1;
        _edit.Cancel();
    }

    public void Add(ScreenRect rect)
    {
        _regions.Add(rect);
        SelectedIndex = _regions.Count - 1;
    }

    public bool DeleteSelected()
    {
        if (!IsValidIndex(SelectedIndex))
        {
            return false;
        }

        _regions.RemoveAt(SelectedIndex);
        if (_regions.Count == 0)
        {
            ResetEditState();
        }
        else
        {
            SelectedIndex = Math.Min(SelectedIndex, _regions.Count - 1);
            _edit.Cancel();
        }

        return true;
    }

    public bool TryHitResizeHandle(ScreenPoint point, out int index, out RectResizeHandle handle)
    {
        for (var i = _regions.Count - 1; i >= 0; i--)
        {
            if (RectGeometry.TryHitResizeHandle(_regions[i], point, out handle))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        handle = RectResizeHandle.None;
        return false;
    }

    public bool TryHit(ScreenPoint point, out int index)
    {
        for (var i = _regions.Count - 1; i >= 0; i--)
        {
            if (_regions[i].Contains(point))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public void BeginResize(int index, RectResizeHandle handle)
    {
        if (!IsValidIndex(index))
        {
            return;
        }

        SelectedIndex = index;
        _edit.BeginResize(_regions[index], handle);
    }

    public void BeginMove(int index, ScreenPoint point)
    {
        if (!IsValidIndex(index))
        {
            return;
        }

        SelectedIndex = index;
        _edit.BeginMove(point);
    }

    public bool UpdateEdit(ScreenPoint point)
    {
        if (!IsValidIndex(SelectedIndex))
        {
            return false;
        }

        if (_edit.IsResizing)
        {
            _regions[SelectedIndex] = _edit.Resize(point);
            return true;
        }

        if (_edit.IsMoving)
        {
            _regions[SelectedIndex] = _edit.Move(_regions[SelectedIndex], point);
            return true;
        }

        return false;
    }

    public bool EndPointerAction()
    {
        if (!IsMoving && !IsResizing)
        {
            return false;
        }

        _edit.EndPointerAction();
        return true;
    }

    public bool NudgeSelected(double dx, double dy)
    {
        if (!IsValidIndex(SelectedIndex))
        {
            return false;
        }

        _regions[SelectedIndex] = _regions[SelectedIndex].Offset(dx, dy);
        return true;
    }

    public bool IsValidIndex(int index)
    {
        return index >= 0 && index < _regions.Count;
    }
}
