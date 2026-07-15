using Shortcut = FocusTool.Win.Native.Shortcut;

namespace FocusTool.Win.Services;

internal enum HoldShortcutReleasePolicy
{
    ChordBreak,
    AllComponentsReleased
}

internal sealed class HoldShortcutSession
{
    private readonly Func<Shortcut, bool> _isChordPressed;
    private readonly Func<Shortcut, bool> _isAnyComponentPressed;
    private Shortcut _shortcut;
    private HoldShortcutReleasePolicy _releasePolicy;

    public HoldShortcutSession(
        Func<Shortcut, bool>? isChordPressed = null,
        Func<Shortcut, bool>? isAnyComponentPressed = null)
    {
        _isChordPressed = isChordPressed ?? (static shortcut => shortcut.IsPressed());
        _isAnyComponentPressed = isAnyComponentPressed ?? (static shortcut => shortcut.IsAnyComponentPressed());
    }

    public bool Active { get; private set; }
    public Shortcut Shortcut => _shortcut;

    public void Begin(Shortcut shortcut, HoldShortcutReleasePolicy releasePolicy)
    {
        _shortcut = shortcut;
        _releasePolicy = releasePolicy;
        Active = shortcut != default;
    }

    public bool ShouldRemainActive()
    {
        if (!Active)
        {
            return false;
        }

        return _releasePolicy switch
        {
            HoldShortcutReleasePolicy.ChordBreak => _isChordPressed(_shortcut),
            HoldShortcutReleasePolicy.AllComponentsReleased => _isAnyComponentPressed(_shortcut),
            _ => false
        };
    }

    public void End()
    {
        Active = false;
        _shortcut = default;
    }
}
