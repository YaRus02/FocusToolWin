using System.Windows;
using System.Windows.Controls;
using FocusTool.Win.Models;
using WpfButton = System.Windows.Controls.Button;

namespace FocusTool.Win.Overlay;

// Holds references to every toolbar control. Populated by ToolbarLayoutBuilder
// during construction and read by the window for live state binding.
internal sealed class ToolbarControls
{
    public readonly Dictionary<AnnotationTool, WpfButton> ToolButtons = [];
    public readonly List<WpfButton> ColorButtons = [];
    public readonly List<WpfButton> LaserColorButtons = [];
    public readonly List<WpfButton> HighlightColorButtons = [];
    public readonly List<WpfButton> MaskColorButtons = [];
    public readonly Dictionary<string, UIElement> Rows = [];
    public readonly Dictionary<string, WpfButton> Carets = [];

    public WpfButton LaserButton = null!;
    public WpfButton HighlightButton = null!;
    public WpfButton DrawButton = null!;
    public WpfButton SpotButton = null!;
    public WpfButton ZoomButton = null!;
    public WpfButton PinButton = null!;
    public WpfButton MaskButton = null!;
    public WpfButton BoardButton = null!;
    public WpfButton TimerButton = null!;

    public WpfButton LaserAlwaysButton = null!;
    public WpfButton LaserHoldButton = null!;
    public WpfButton GlowButton = null!;
    public TextBlock TrailText = null!;
    public WpfButton HighlightAlwaysButton = null!;
    public WpfButton HighlightHoldButton = null!;
    public WpfButton HighlightPulseButton = null!;
    public TextBlock HighlightRadiusText = null!;
    public TextBlock ThicknessText = null!;
    public TextBlock FontText = null!;
    public WpfButton StepButton = null!;
    public WpfButton StepOptionsButton = null!;
    public UIElement StepOptionsRow = null!;
    public WpfButton StepOvalButton = null!;
    public WpfButton StepRectButton = null!;
    public WpfButton FadeButton = null!;
    public WpfButton FadeOptionsButton = null!;
    public UIElement FadeOptionsRow = null!;
    public TextBlock FadeVisibleText = null!;
    public TextBlock FadeDurationText = null!;
    public WpfButton UndoButton = null!;
    public WpfButton RedoButton = null!;
    public WpfButton ClearButton = null!;
    public TextBlock SpotRadiusText = null!;
    public TextBlock SpotDimText = null!;
    public WpfButton SpotRegionButton = null!;
    public WpfButton ClearSpotRegionsButton = null!;
    public TextBlock ZoomZoomText = null!;
    public TextBlock ZoomRadiusText = null!;
    public TextBlock PinZoomText = null!;
    public TextBlock PinFpsText = null!;
    public WpfButton PinOptionsButton = null!;
    public UIElement PinOptionsRow = null!;
    public WpfButton ClosePinsButton = null!;
    public TextBlock MaskOpacityText = null!;
    public WpfButton ClearMaskButton = null!;
    public WpfButton BoardScreenButton = null!;
    public WpfButton BoardBlackButton = null!;
    public WpfButton BoardWhiteButton = null!;
    public WpfButton ShotRegionButton = null!;
    public WpfButton CloseTimersButton = null!;

    public Border ContextualHost = null!;
    public UIElement ExpandedRoot = null!;
    public UIElement CollapsedRoot = null!;
    public Border ActiveDot = null!;
}
