using System.Text;
using System.Text.Json.Serialization;

namespace Launcher.Core.Models;

/// <summary>
/// A system-wide hotkey. Stored as modifier flags plus a virtual-key code so it survives a
/// round trip through JSON without depending on any UI type.
/// </summary>
public sealed class HotkeyBinding
{
    /// <summary>MOD_ALT.</summary>
    public const uint ModifierAlt = 0x0001;

    /// <summary>MOD_CONTROL.</summary>
    public const uint ModifierControl = 0x0002;

    /// <summary>MOD_SHIFT.</summary>
    public const uint ModifierShift = 0x0004;

    /// <summary>MOD_WIN.</summary>
    public const uint ModifierWindows = 0x0008;

    /// <summary>MOD_NOREPEAT - holding the keys must not fire repeatedly.</summary>
    public const uint ModifierNoRepeat = 0x4000;

    /// <summary>VK_SPACE.</summary>
    public const uint VirtualKeySpace = 0x20;

    public bool Alt { get; set; }

    public bool Control { get; set; }

    public bool Shift { get; set; }

    public bool Windows { get; set; }

    /// <summary>Virtual-key code. Zero means "no key chosen".</summary>
    public uint Key { get; set; }

    /// <summary>SPEC.md's default: Alt+Space.</summary>
    public static HotkeyBinding CreateDefault() => new()
    {
        Alt = true,
        Key = VirtualKeySpace,
    };

    /// <summary>
    /// A hotkey needs a key and at least one modifier. Registering a bare key would
    /// swallow it system-wide, which is never what the user meant.
    /// </summary>
    [JsonIgnore]
    public bool IsValid => Key != 0 && (Alt || Control || Shift || Windows);

    /// <summary>Modifier flags for <c>RegisterHotKey</c>, including MOD_NOREPEAT.</summary>
    public uint ToModifierFlags()
    {
        uint flags = ModifierNoRepeat;

        if (Alt)
        {
            flags |= ModifierAlt;
        }

        if (Control)
        {
            flags |= ModifierControl;
        }

        if (Shift)
        {
            flags |= ModifierShift;
        }

        if (Windows)
        {
            flags |= ModifierWindows;
        }

        return flags;
    }

    public HotkeyBinding Clone() => new()
    {
        Alt = Alt,
        Control = Control,
        Shift = Shift,
        Windows = Windows,
        Key = Key,
    };

    /// <summary>Human-readable form, e.g. "Alt + Space".</summary>
    public override string ToString()
    {
        if (Key == 0 && !Alt && !Control && !Shift && !Windows)
        {
            return "None";
        }

        var text = new StringBuilder();

        // Ordered the way Windows writes shortcuts, not the order they were pressed.
        if (Control)
        {
            text.Append("Ctrl + ");
        }

        if (Alt)
        {
            text.Append("Alt + ");
        }

        if (Shift)
        {
            text.Append("Shift + ");
        }

        if (Windows)
        {
            text.Append("Win + ");
        }

        text.Append(DescribeKey(Key));
        return text.ToString();
    }

    /// <summary>Name for a virtual-key code. Falls back to the hex code for exotic keys.</summary>
    public static string DescribeKey(uint key) => key switch
    {
        0 => "…",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        0xBD => "-",
        0xBB => "=",
        >= 0x30 and <= 0x39 => ((char)key).ToString(),
        >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x60 and <= 0x69 => "Numpad " + (key - 0x60),
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
        _ => "0x" + key.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// True for keys that are only modifiers. Used while capturing a binding, where the
    /// user is still holding Alt and has not yet pressed the real key.
    /// </summary>
    public static bool IsModifierKey(uint key) => key is
        0x10 or 0x11 or 0x12 or       // Shift, Control, Alt
        0x5B or 0x5C or               // Left/Right Windows
        0xA0 or 0xA1 or               // Left/Right Shift
        0xA2 or 0xA3 or               // Left/Right Control
        0xA4 or 0xA5;                 // Left/Right Alt
}
