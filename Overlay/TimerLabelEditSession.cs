using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusTool.Win.Models;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace FocusTool.Win.Overlay;

// Inline editing of a timer's label. Swaps the label TextBlock for an editable
// TextBox, commits on Enter / focus loss, cancels on Escape.
internal sealed class TimerLabelEditSession
{
    private readonly WpfTextBox _edit;
    private readonly TextBlock _text;
    private readonly TimerModel _model;
    private readonly Action _onChanged;           // raise DefaultsChanged + Refresh
    private readonly Action<string> _onCommitted; // raise LabelCommitted (non-empty)
    private readonly Action _onCancelled;          // Refresh
    private readonly Action _focusBack;            // return keyboard focus to the window

    public TimerLabelEditSession(
        WpfTextBox edit,
        TextBlock text,
        TimerModel model,
        Action onChanged,
        Action<string> onCommitted,
        Action onCancelled,
        Action focusBack)
    {
        _edit = edit;
        _text = text;
        _model = model;
        _onChanged = onChanged;
        _onCommitted = onCommitted;
        _onCancelled = onCancelled;
        _focusBack = focusBack;

        _edit.KeyDown += OnKeyDown;
        _edit.LostKeyboardFocus += (_, _) => Commit();
    }

    public bool IsEditing { get; private set; }

    public void Begin()
    {
        IsEditing = true;
        _edit.Text = _model.Label;
        _text.Visibility = Visibility.Collapsed;
        _edit.Visibility = Visibility.Visible;
        _edit.Focus();
        _edit.SelectAll();
    }

    public void Commit()
    {
        if (!IsEditing)
        {
            return;
        }

        var label = _edit.Text.Trim();
        _model.Label = label;
        End();
        if (label.Length > 0)
        {
            _onCommitted(label);
        }

        _onChanged();
    }

    public void Cancel()
    {
        End();
        _onCancelled();
    }

    private void End()
    {
        IsEditing = false;
        _edit.Visibility = Visibility.Collapsed;
        _focusBack();
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }
}
