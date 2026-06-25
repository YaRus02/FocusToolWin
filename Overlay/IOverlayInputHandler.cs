using System.Windows.Input;
using FocusTool.Win.Models;

namespace FocusTool.Win.Overlay;

internal interface IOverlayInputHandler
{
    InteractionMode Mode { get; }
    void HandleOverlayMouseDown(ScreenPoint point, MouseButton button, ModifierKeys modifiers);
    void HandleOverlayMouseMove(ScreenPoint point, ModifierKeys modifiers);
    void HandleOverlayMouseUp(ScreenPoint point, MouseButton button, ModifierKeys modifiers);
    bool HandleOverlayMouseWheel(ScreenPoint point, int delta, ModifierKeys modifiers);
    void HandleOverlayCaptureLost();
    bool HandleOverlayKeyDown(Key key, ModifierKeys modifiers);
    void HandleOverlayTextInput(string text);
}
