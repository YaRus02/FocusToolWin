using FocusTool.Win.Models;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class BoardController
{
    private readonly CaptureController _capture;
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Action<InteractionMode> _setMode;

    public BoardController(
        CaptureController capture,
        Func<InteractionMode> modeProvider,
        Action<InteractionMode> setMode)
    {
        _capture = capture;
        _modeProvider = modeProvider;
        _setMode = setMode;
    }

    public ScreenBoardFrame? Frame { get; private set; }

    public void ToggleScreenBoard()
    {
        if (_modeProvider() == InteractionMode.ScreenBoard)
        {
            _setMode(InteractionMode.Passthrough);
            return;
        }

        _ = EnterScreenBoardAsync();
    }

    public void ToggleBlackScreen()
    {
        _setMode(_modeProvider() == InteractionMode.BlackScreen ? InteractionMode.Passthrough : InteractionMode.BlackScreen);
    }

    public void ToggleWhiteScreen()
    {
        _setMode(_modeProvider() == InteractionMode.WhiteScreen ? InteractionMode.Passthrough : InteractionMode.WhiteScreen);
    }

    public void SaveSnapshot()
    {
        _ = _capture.SaveScreenBoardSnapshotAsync(Frame);
    }

    public void ClearFrame()
    {
        Frame = null;
    }

    private async Task EnterScreenBoardAsync()
    {
        var previousMode = _modeProvider();
        await _capture.EnterScreenBoardAsync(
            frame =>
            {
                Frame = frame;
                _setMode(InteractionMode.ScreenBoard);
            },
            () => _setMode(previousMode));
    }
}
