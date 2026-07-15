using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusTool.Win.Models;
using FocusTool.Win.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfToolTip = System.Windows.Controls.ToolTip;
using MediaColor = System.Windows.Media.Color;

namespace FocusTool.Win.Overlay;

// Shared visual constants and stateless control factories for the toolbar.
// Used by both the layout builder (construction) and the window (live state binding).
internal static class ToolbarStyles
{
    public static readonly WpfBrush PanelBrush = new SolidColorBrush(MediaColor.FromArgb(238, 30, 30, 30));
    public static readonly WpfBrush ContextBrush = new SolidColorBrush(MediaColor.FromArgb(238, 38, 38, 38));
    public static readonly WpfBrush ButtonBrush = new SolidColorBrush(MediaColor.FromRgb(48, 48, 48));
    public static readonly WpfBrush ActiveBrush = new SolidColorBrush(MediaColor.FromRgb(32, 128, 255));
    public static readonly WpfBrush ToolbarBorderBrush = new SolidColorBrush(MediaColor.FromArgb(120, 255, 255, 255));
    public static readonly WpfBrush ActiveBorderBrush = new SolidColorBrush(Colors.White);
    public static readonly WpfBrush DisabledBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 39));
    public static readonly WpfBrush LabelBrush = new SolidColorBrush(MediaColor.FromArgb(170, 255, 255, 255));
    public static readonly WpfBrush CaretBrush = new SolidColorBrush(MediaColor.FromArgb(130, 255, 255, 255));
    public static readonly WpfBrush CaretActiveBrush = WpfBrushes.White;

    public static StackPanel CreateRow()
    {
        return new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static WpfButton CreateButton(string text, string tooltip, RoutedEventHandler onClick, double width = 50)
    {
        var button = new WpfButton
        {
            Content = text,
            Width = width,
            Height = 26,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(4, 0, 4, 0),
            Background = ButtonBrush,
            BorderBrush = ToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            Foreground = WpfBrushes.White,
            Cursor = WpfCursors.Hand,
            FontSize = 11,
            HorizontalContentAlignment = WpfHorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        SetToolTip(button, tooltip);
        button.Click += onClick;
        return button;
    }

    public static void SetToolTip(FrameworkElement element, string text)
    {
        var toolTip = new WpfToolTip { Content = text };
        WpfTopmostToolTipHelper.Attach(toolTip);
        element.ToolTip = toolTip;
    }

    public static WpfButton CreateStepperButton(string text, string tooltip, RoutedEventHandler onClick)
    {
        var button = CreateButton(text, tooltip, onClick, width: 22);
        button.Height = 24;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0);
        return button;
    }

    public static WpfButton CreateInlineOptionsButton(string tooltip, Action onClick)
    {
        var button = CreateButton("v", tooltip, (_, _) => onClick(), width: 18);
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0, 0, 1, 0);
        return button;
    }

    public static UIElement CreateSeparator()
    {
        return new Border
        {
            Width = 1,
            Height = 24,
            Background = new SolidColorBrush(MediaColor.FromArgb(80, 255, 255, 255)),
            Margin = new Thickness(5, 0, 5, 0)
        };
    }

    public static void UpdateColorSwatches(List<WpfButton> buttons, IReadOnlyList<string> presets, string currentColor)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            var colorText = i < presets.Count ? presets[i] : "#FFFFFFFF";
            if (AppSettings.TryParseColor(colorText, out var color))
            {
                buttons[i].Background = new SolidColorBrush(color);
            }

            var selected = string.Equals(colorText, currentColor, StringComparison.OrdinalIgnoreCase);
            buttons[i].BorderBrush = selected ? ActiveBorderBrush : ToolbarBorderBrush;
            buttons[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    public static void SetButtonActive(WpfButton button, bool active)
    {
        button.Background = active ? ActiveBrush : ButtonBrush;
        button.BorderBrush = active ? ActiveBorderBrush : ToolbarBorderBrush;
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    public static void SetButtonEnabled(WpfButton button, bool enabled)
    {
        button.Background = enabled ? ButtonBrush : DisabledBrush;
        button.Foreground = enabled ? WpfBrushes.White : new SolidColorBrush(MediaColor.FromRgb(140, 140, 140));
    }
}
