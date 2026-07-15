using System.Windows.Input;

namespace FocusTool.Win.Native;

internal readonly record struct Shortcut(ModifierKeys Modifiers, Key Key, int VirtualKey, string DisplayText)
{
    private const int VkXButton1 = 0x05;
    private const int VkXButton2 = 0x06;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkLeftWin = 0x5B;
    private const int VkRightWin = 0x5C;

    public bool IsMouseButton => VirtualKey is VkXButton1 or VkXButton2;

    public bool Matches(Key key, ModifierKeys modifiers)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return MatchesVirtualKey(virtualKey) && Normalize(modifiers) == Modifiers;
    }

    public bool IsPressed()
    {
        if ((Modifiers & ModifierKeys.Control) != 0 && !IsVirtualKeyPressed(VkControl))
        {
            return false;
        }

        if ((Modifiers & ModifierKeys.Alt) != 0 && !IsVirtualKeyPressed(VkAlt))
        {
            return false;
        }

        if ((Modifiers & ModifierKeys.Shift) != 0 && !IsVirtualKeyPressed(VkShift))
        {
            return false;
        }

        if ((Modifiers & ModifierKeys.Windows) != 0 && !IsVirtualKeyPressed(VkLeftWin) && !IsVirtualKeyPressed(VkRightWin))
        {
            return false;
        }

        return IsPrimaryInputPressed();
    }

    public bool IsAnyComponentPressed()
    {
        return IsPrimaryInputPressed()
            || (Modifiers & ModifierKeys.Control) != 0 && IsVirtualKeyPressed(VkControl)
            || (Modifiers & ModifierKeys.Alt) != 0 && IsVirtualKeyPressed(VkAlt)
            || (Modifiers & ModifierKeys.Shift) != 0 && IsVirtualKeyPressed(VkShift)
            || (Modifiers & ModifierKeys.Windows) != 0
                && (IsVirtualKeyPressed(VkLeftWin) || IsVirtualKeyPressed(VkRightWin));
    }

    public bool IsPressedWithExactModifiers()
    {
        var pressedModifiers = ModifierKeys.None;
        if (IsVirtualKeyPressed(VkControl))
        {
            pressedModifiers |= ModifierKeys.Control;
        }

        if (IsVirtualKeyPressed(VkAlt))
        {
            pressedModifiers |= ModifierKeys.Alt;
        }

        if (IsVirtualKeyPressed(VkShift))
        {
            pressedModifiers |= ModifierKeys.Shift;
        }

        if (IsVirtualKeyPressed(VkLeftWin) || IsVirtualKeyPressed(VkRightWin))
        {
            pressedModifiers |= ModifierKeys.Windows;
        }

        return pressedModifiers == Modifiers && IsPressed();
    }

    public uint ToNativeModifiers()
    {
        uint modifiers = NativeMethods.ModNoRepeat;

        if ((Modifiers & ModifierKeys.Alt) != 0)
        {
            modifiers |= NativeMethods.ModAlt;
        }

        if ((Modifiers & ModifierKeys.Control) != 0)
        {
            modifiers |= NativeMethods.ModControl;
        }

        if ((Modifiers & ModifierKeys.Shift) != 0)
        {
            modifiers |= NativeMethods.ModShift;
        }

        if ((Modifiers & ModifierKeys.Windows) != 0)
        {
            modifiers |= NativeMethods.ModWin;
        }

        return modifiers;
    }

    public static bool TryParse(string? value, out Shortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key key = Key.None;
        var virtualKey = 0;
        var hasPrimaryInput = false;

        foreach (var rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Trim();
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    continue;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    continue;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    continue;
                case "XBUTTON1":
                case "MOUSE4":
                    if (hasPrimaryInput)
                    {
                        return false;
                    }

                    hasPrimaryInput = true;
                    virtualKey = VkXButton1;
                    key = Key.None;
                    continue;
                case "XBUTTON2":
                case "MOUSE5":
                    if (hasPrimaryInput)
                    {
                        return false;
                    }

                    hasPrimaryInput = true;
                    virtualKey = VkXButton2;
                    key = Key.None;
                    continue;
                case "[":
                    key = Key.OemOpenBrackets;
                    break;
                case "]":
                    key = Key.OemCloseBrackets;
                    break;
                case "ESC":
                    key = Key.Escape;
                    break;
                case "DEL":
                    key = Key.Delete;
                    break;
                case "BACKSPACE":
                    key = Key.Back;
                    break;
                default:
                    key = ParseKeyToken(part);
                    break;
            }

            if (key == Key.None || hasPrimaryInput)
            {
                return false;
            }

            hasPrimaryInput = true;
            if (key != Key.None)
            {
                virtualKey = KeyInterop.VirtualKeyFromKey(key);
            }
        }

        if (!hasPrimaryInput || virtualKey == 0)
        {
            return false;
        }

        shortcut = new Shortcut(Normalize(modifiers), key, virtualKey, value);
        return true;
    }

    private static Key ParseKeyToken(string token)
    {
        if (token.Length == 1)
        {
            var ch = token[0];
            if (ch is >= 'A' and <= 'Z' || ch is >= 'a' and <= 'z')
            {
                return Enum.Parse<Key>(ch.ToString().ToUpperInvariant());
            }

            if (ch is >= '0' and <= '9')
            {
                return Enum.Parse<Key>("D" + ch);
            }
        }

        return Enum.TryParse<Key>(token, true, out var key) ? key : Key.None;
    }

    private bool MatchesVirtualKey(int virtualKey)
    {
        return virtualKey == VirtualKey
            || TryGetTopRowDigit(Key, out var digit) && virtualKey == VkNumpadDigit(digit);
    }

    private bool IsPrimaryInputPressed()
    {
        return IsVirtualKeyPressed(VirtualKey)
            || TryGetTopRowDigit(Key, out var digit) && IsVirtualKeyPressed(VkNumpadDigit(digit));
    }

    private static bool TryGetTopRowDigit(Key key, out int digit)
    {
        digit = key switch
        {
            Key.D0 => 0,
            Key.D1 => 1,
            Key.D2 => 2,
            Key.D3 => 3,
            Key.D4 => 4,
            Key.D5 => 5,
            Key.D6 => 6,
            Key.D7 => 7,
            Key.D8 => 8,
            Key.D9 => 9,
            _ => -1
        };

        return digit >= 0;
    }

    private static int VkNumpadDigit(int digit) => 0x60 + digit;

    private static bool IsVirtualKeyPressed(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static ModifierKeys Normalize(ModifierKeys modifiers)
    {
        return modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows);
    }
}
