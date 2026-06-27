using FocusTool.Win.Models;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class RegionMaskController
{
    private readonly List<RegionMask> _masks = [];
    private readonly RectEditSession _edit = new();
    private RegionMask? _editingMask;
    private int _selectedMaskId = -1;
    private int _nextId = 1;

    public IReadOnlyList<RegionMask> Masks => _masks;
    public int Count => _masks.Count;
    public bool HasMasks => _masks.Count > 0;
    public int SelectedMaskId => _selectedMaskId;
    public bool IsMoving => _editingMask is not null && _edit.IsMoving;
    public bool IsResizing => _editingMask is not null && _edit.IsResizing;

    public void Clear()
    {
        _masks.Clear();
        CancelEdit();
        _selectedMaskId = -1;
    }

    public RegionMask Add(ScreenRect rect, AppSettings settings)
    {
        var mask = new RegionMask(_nextId++, rect, settings);
        _masks.Add(mask);
        _selectedMaskId = mask.Id;
        return mask;
    }

    public bool Delete(int maskId)
    {
        var index = _masks.FindIndex(mask => mask.Id == maskId);
        if (index < 0)
        {
            return false;
        }

        _masks.RemoveAt(index);
        CancelEdit();
        if (_selectedMaskId == maskId)
        {
            _selectedMaskId = -1;
        }

        return true;
    }

    public bool DeleteSelected()
    {
        return _selectedMaskId >= 0 && Delete(_selectedMaskId);
    }

    public bool TryHit(ScreenPoint point, out RegionMask mask)
    {
        for (var i = _masks.Count - 1; i >= 0; i--)
        {
            if (_masks[i].Contains(point))
            {
                mask = _masks[i];
                return true;
            }
        }

        mask = null!;
        return false;
    }

    public bool TryGetSelected(out RegionMask mask)
    {
        mask = _masks.FirstOrDefault(item => item.Id == _selectedMaskId)!;
        if (mask is not null)
        {
            return true;
        }

        _selectedMaskId = -1;
        return false;
    }

    public bool TryGetSelectedOrHit(ScreenPoint point, out RegionMask mask)
    {
        if (TryGetSelected(out mask))
        {
            return true;
        }

        if (!TryHit(point, out mask))
        {
            return false;
        }

        _selectedMaskId = mask.Id;
        return true;
    }

    public bool TryHitResizeHandle(ScreenPoint point, out RegionMask mask, out RectResizeHandle handle)
    {
        if (TryGetSelected(out var selectedMask))
        {
            if (RectGeometry.TryHitResizeHandle(selectedMask.Rect, point, out handle))
            {
                mask = selectedMask;
                return true;
            }

            mask = null!;
            handle = RectResizeHandle.None;
            return false;
        }

        for (var i = _masks.Count - 1; i >= 0; i--)
        {
            if (RectGeometry.TryHitResizeHandle(_masks[i].Rect, point, out handle))
            {
                mask = _masks[i];
                return true;
            }
        }

        mask = null!;
        handle = RectResizeHandle.None;
        return false;
    }

    public void Select(int maskId)
    {
        _selectedMaskId = maskId;
    }

    public void ClearSelection()
    {
        _selectedMaskId = -1;
    }

    public RegionMaskStyle? GetStyle(int maskId)
    {
        return _masks.FirstOrDefault(mask => mask.Id == maskId)?.Style;
    }

    public bool SetStyle(int maskId, RegionMaskStyle style)
    {
        var mask = _masks.FirstOrDefault(item => item.Id == maskId);
        if (mask is null || mask.Style == style)
        {
            return false;
        }

        mask.SetStyle(style);
        return true;
    }

    public bool SetSelectedColor(string color)
    {
        if (!TryGetSelected(out var mask))
        {
            return false;
        }

        mask.SetColor(color);
        return true;
    }

    public bool SetSelectedOpacity(double opacity)
    {
        if (!TryGetSelected(out var mask))
        {
            return false;
        }

        mask.SetOpacity(opacity);
        return true;
    }

    public void BeginResize(RegionMask mask, RectResizeHandle handle)
    {
        _editingMask = mask;
        _selectedMaskId = mask.Id;
        _edit.BeginResize(mask.Rect, handle);
    }

    public void BeginMove(RegionMask mask, ScreenPoint point)
    {
        _editingMask = mask;
        _selectedMaskId = mask.Id;
        _edit.BeginMove(point);
    }

    public bool UpdateEdit(ScreenPoint point)
    {
        if (_editingMask is null)
        {
            return false;
        }

        if (_edit.IsResizing)
        {
            _editingMask.SetRect(_edit.Resize(point));
            return true;
        }

        if (_edit.IsMoving)
        {
            _editingMask.SetRect(_edit.Move(_editingMask.Rect, point));
            return true;
        }

        return false;
    }

    public bool EndPointerAction()
    {
        if (_editingMask is null)
        {
            return false;
        }

        _editingMask = null;
        _edit.EndPointerAction();
        return true;
    }

    public void CancelEdit()
    {
        _editingMask = null;
        _edit.Cancel();
    }
}
