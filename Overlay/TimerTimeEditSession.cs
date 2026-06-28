using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusTool.Win.Models;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;

namespace FocusTool.Win.Overlay;

// Inline editing of a timer's duration / target time. Validates keystrokes and
// pastes against TimerTimeEditor, protecting the fixed ':' separators, and parses
// the result on commit. Only meaningful for editable modes (Countdown / UntilTime).
internal sealed class TimerTimeEditSession
{
    private readonly WpfTextBox _edit;
    private readonly TextBlock _text;
    private readonly TimerModel _model;
    private readonly Action _onChanged;  // raise DefaultsChanged (after a successful parse)
    private readonly Action _refresh;    // Refresh
    private readonly Action _focusBack;  // return keyboard focus to the window

    public TimerTimeEditSession(
        WpfTextBox edit,
        TextBlock text,
        TimerModel model,
        Action onChanged,
        Action refresh,
        Action focusBack)
    {
        _edit = edit;
        _text = text;
        _model = model;
        _onChanged = onChanged;
        _refresh = refresh;
        _focusBack = focusBack;

        _edit.PreviewKeyDown += OnPreviewKeyDown;
        _edit.KeyDown += OnKeyDown;
        _edit.PreviewTextInput += OnPreviewTextInput;
        _edit.LostKeyboardFocus += (_, _) => Commit();
        WpfDataObject.AddPastingHandler(_edit, OnPaste);
    }

    public bool IsEditing { get; private set; }

    public void Begin()
    {
        if (!_model.CanEditTime)
        {
            return;
        }

        IsEditing = true;
        _edit.MaxLength = TimerTimeEditor.MaxLength(_model.Mode, _model.Use24HourTime);
        _edit.ToolTip = TimerTimeEditor.ToolTip(_model.Mode, _model.Use24HourTime);
        _edit.Text = _model.Mode == TimerMode.UntilTime ? _model.TargetTimeText() : _model.DurationText();
        _text.Visibility = Visibility.Collapsed;
        _edit.Visibility = Visibility.Visible;
        _edit.Focus();
        _edit.CaretIndex = 0;
    }

    public void Commit()
    {
        if (!IsEditing)
        {
            return;
        }

        var input = _edit.Text.Trim();
        if (_model.Mode == TimerMode.Countdown && TimerTimeEditor.TryParseDuration(input, out var seconds))
        {
            _model.SetCountdownSeconds(seconds);
            _onChanged();
        }
        else if (_model.Mode == TimerMode.UntilTime && TimerTimeEditor.TryParseTargetTime(input, _model.Use24HourTime, out var target))
        {
            _model.SetTargetTime(target);
            _onChanged();
        }

        End();
        _refresh();
    }

    public void Cancel()
    {
        End();
        _refresh();
    }

    private void End()
    {
        IsEditing = false;
        _edit.Visibility = Visibility.Collapsed;
        _text.Visibility = Visibility.Visible;
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

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is not (Key.Back or Key.Delete))
        {
            return;
        }

        if (SelectionTouchesFixedTimeSeparator())
        {
            e.Handled = true;
            return;
        }

        var text = _edit.Text ?? string.Empty;
        if (e.Key == Key.Back
            && _edit.SelectionLength == 0
            && _edit.SelectionStart > 0
            && text[_edit.SelectionStart - 1] == ':')
        {
            _edit.SelectionStart = Math.Max(0, _edit.SelectionStart - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete
            && _edit.SelectionLength == 0
            && _edit.SelectionStart < text.Length
            && text[_edit.SelectionStart] == ':')
        {
            _edit.SelectionStart = Math.Min(text.Length, _edit.SelectionStart + 1);
            e.Handled = true;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SelectionTouchesFixedTimeSeparator() && e.Text.IndexOf(':') < 0)
        {
            e.Handled = true;
            return;
        }

        e.Handled = !TimerTimeEditor.IsValidPartial(GetProposedText(_edit, e.Text), _model.Mode, _model.Use24HourTime);
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(WpfDataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(WpfDataFormats.Text) as string ?? string.Empty;
        if ((SelectionTouchesFixedTimeSeparator() && text.IndexOf(':') < 0)
            || !TimerTimeEditor.IsValidPartial(GetProposedText(_edit, text), _model.Mode, _model.Use24HourTime))
        {
            e.CancelCommand();
        }
    }

    private static string GetProposedText(WpfTextBox textBox, string input)
    {
        var text = textBox.Text ?? string.Empty;
        var start = Math.Clamp(textBox.SelectionStart, 0, text.Length);
        var length = Math.Clamp(textBox.SelectionLength, 0, text.Length - start);
        return text.Remove(start, length).Insert(start, input);
    }

    private bool SelectionTouchesFixedTimeSeparator()
    {
        var text = _edit.Text ?? string.Empty;
        if (_edit.SelectionLength == 0 || text.Length == 0)
        {
            return false;
        }

        var start = Math.Clamp(_edit.SelectionStart, 0, text.Length);
        var length = Math.Clamp(_edit.SelectionLength, 0, text.Length - start);
        return length > 0 && text.AsSpan(start, length).Contains(':');
    }
}
