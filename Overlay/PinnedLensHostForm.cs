using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FocusTool.Win.Native;
using FocusTool.Win.Services;

namespace FocusTool.Win.Overlay;

internal sealed class PinnedLensHostForm : Form
{
    private readonly PinnedLensHostWindow _owner;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _freezeItem;
    private readonly ToolStripMenuItem _zoomInItem;
    private readonly ToolStripMenuItem _zoomOutItem;
    private readonly List<System.Windows.Forms.Timer> _topmostTimers = [];
    private bool _freezeTogglePending;

    public PinnedLensHostForm(PinnedLensHostWindow owner)
    {
        _owner = owner;
        Text = "FocusTool Pinned Lens";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(28, 28, 28);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = new Rectangle(-32000, -32000, 1, 1);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        _freezeItem = new ToolStripMenuItem("Freeze", null, (_, _) => ToggleFrozenAfterMenuCloses());
        _zoomInItem = new ToolStripMenuItem("Zoom in", null, (_, _) => _owner.AdjustZoom(PinnedLensHostWindow.ZoomStep));
        _zoomOutItem = new ToolStripMenuItem("Zoom out", null, (_, _) => _owner.AdjustZoom(-PinnedLensHostWindow.ZoomStep));
        _menu.Items.Add(_freezeItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_zoomInItem);
        _menu.Items.Add(_zoomOutItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Close", null, (_, _) => Close()));
        _menu.Items.Add(new ToolStripMenuItem("Close all", null, (_, _) => _owner.CloseAllRequested?.Invoke()));
        _menu.Opening += (_, _) => UpdateMenuState();
        TopmostContextMenuHelper.Attach(_menu, _topmostTimers);
        ContextMenuStrip = _menu;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WsExToolWindow;
            cp.ExStyle |= NativeMethods.WsExNoActivate;
            cp.ExStyle &= ~NativeMethods.WsExAppWindow;
            return cp;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _owner.RequestSelection();

        if (e.Button == MouseButtons.Left)
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, NativeMethods.WmNcLButtonDown, NativeMethods.HtCaption, IntPtr.Zero);
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (WindowState != FormWindowState.Normal)
        {
            return;
        }

        _owner.SyncWindowBoundsFromNative(clamp: true);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt)) == Keys.Control)
        {
            _owner.AdjustZoom(e.Delta > 0 ? PinnedLensHostWindow.ZoomStep : -PinnedLensHostWindow.ZoomStep);
            return;
        }

        base.OnMouseWheel(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _owner.PaintFrozenFrame(
            e.Graphics,
            new Rectangle(
                PinnedLensHostWindow.BorderThickness,
                PinnedLensHostWindow.BorderThickness,
                Math.Max(0, ClientSize.Width - PinnedLensHostWindow.BorderThickness * 2),
                Math.Max(0, ClientSize.Height - PinnedLensHostWindow.BorderThickness * 2)));
        var color = _owner.IsFrozen
            ? Color.FromArgb(230, 90, 190, 255)
            : _owner.IsSelected
                ? Color.FromArgb(235, 255, 214, 80)
                : Color.FromArgb(220, 255, 255, 255);
        using var pen = new Pen(color, PinnedLensHostWindow.BorderThickness);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void OnContextMenuStripChanged(EventArgs e)
    {
        base.OnContextMenuStripChanged(e);
        UpdateMenuState();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        UpdateMenuState();
        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TopmostContextMenuHelper.DisposeTimers(_topmostTimers);
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    public void ReassertContextMenuTopmost()
    {
        TopmostContextMenuHelper.ReassertIfVisible(_menu);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmMoving && m.LParam != IntPtr.Zero)
        {
            var rect = Marshal.PtrToStructure<NativeMethods.Rect>(m.LParam);
            var clamped = PinnedLensHostWindow.ClampWindowBoundsToNearestWorkingArea(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
            rect.Left = clamped.Left;
            rect.Top = clamped.Top;
            rect.Right = clamped.Right;
            rect.Bottom = clamped.Bottom;
            Marshal.StructureToPtr(rect, m.LParam, false);
            _owner.SetWindowBoundsFromHostMove(clamped);
        }

        base.WndProc(ref m);
    }

    private void UpdateMenuState()
    {
        _freezeItem.Text = _owner.IsFrozen ? "Unfreeze" : "Freeze";
        _freezeItem.Enabled = !_freezeTogglePending;
        _zoomInItem.Enabled = !_freezeTogglePending && _owner.Zoom < _owner.MaximumZoom - 0.001;
        _zoomOutItem.Enabled = !_freezeTogglePending && _owner.Zoom > 1.0 + 0.001;
    }

    private async void ToggleFrozenAfterMenuCloses()
    {
        if (_freezeTogglePending)
        {
            return;
        }

        if (_owner.IsFrozen)
        {
            _owner.Resume();
            UpdateMenuState();
            return;
        }

        _freezeTogglePending = true;
        UpdateMenuState();

        try
        {
            if (_menu.Visible)
            {
                _menu.Close(ToolStripDropDownCloseReason.ItemClicked);
            }

            await Task.Delay(PinnedLensHostWindow.FreezeAfterMenuDelayMs);

            if (!IsDisposed && IsHandleCreated)
            {
                await _owner.SetFrozenAsync(true);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _freezeTogglePending = false;
                UpdateMenuState();
            }
        }
    }
}
