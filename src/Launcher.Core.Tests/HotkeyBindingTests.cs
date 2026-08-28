using Launcher.Core.Models;
using Xunit;

namespace Launcher.Core.Tests;

public sealed class HotkeyBindingTests
{
    [Fact]
    public void Default_isAltSpace()
    {
        HotkeyBinding binding = HotkeyBinding.CreateDefault();

        Assert.True(binding.Alt);
        Assert.False(binding.Control);
        Assert.False(binding.Shift);
        Assert.False(binding.Windows);
        Assert.Equal(HotkeyBinding.VirtualKeySpace, binding.Key);
        Assert.Equal("Alt + Space", binding.ToString());
    }

    [Fact]
    public void ABareKey_isNotValid()
    {
        // Registering a key with no modifier would swallow it system-wide.
        var binding = new HotkeyBinding { Key = 0x41 };

        Assert.False(binding.IsValid);
    }

    [Fact]
    public void ModifiersWithNoKey_areNotValid()
    {
        Assert.False(new HotkeyBinding { Control = true, Shift = true }.IsValid);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void AnyOneModifierPlusAKey_isValid(bool alt, bool control, bool shift, bool windows)
    {
        var binding = new HotkeyBinding
        {
            Alt = alt,
            Control = control,
            Shift = shift,
            Windows = windows,
            Key = 0x41,
        };

        Assert.True(binding.IsValid);
    }

    [Fact]
    public void ModifierFlags_alwaysIncludeNoRepeat()
    {
        // Without MOD_NOREPEAT, holding the combination fires continuously.
        uint flags = new HotkeyBinding { Alt = true, Key = 0x41 }.ToModifierFlags();

        Assert.Equal(HotkeyBinding.ModifierNoRepeat, flags & HotkeyBinding.ModifierNoRepeat);
        Assert.Equal(HotkeyBinding.ModifierAlt, flags & HotkeyBinding.ModifierAlt);
    }

    [Fact]
    public void ModifierFlags_combineEveryModifier()
    {
        uint flags = new HotkeyBinding
        {
            Alt = true,
            Control = true,
            Shift = true,
            Windows = true,
            Key = 0x41,
        }.ToModifierFlags();

        Assert.Equal(HotkeyBinding.ModifierAlt, flags & HotkeyBinding.ModifierAlt);
        Assert.Equal(HotkeyBinding.ModifierControl, flags & HotkeyBinding.ModifierControl);
        Assert.Equal(HotkeyBinding.ModifierShift, flags & HotkeyBinding.ModifierShift);
        Assert.Equal(HotkeyBinding.ModifierWindows, flags & HotkeyBinding.ModifierWindows);
    }

    [Fact]
    public void ToString_writesModifiersInWindowsOrder()
    {
        var binding = new HotkeyBinding
        {
            Shift = true,
            Alt = true,
            Control = true,
            Key = 0x4B,
        };

        Assert.Equal("Ctrl + Alt + Shift + K", binding.ToString());
    }

    [Theory]
    [InlineData(0x20, "Space")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x41, "A")]
    [InlineData(0x39, "9")]
    [InlineData(0x70, "F1")]
    [InlineData(0x7B, "F12")]
    [InlineData(0x26, "Up")]
    [InlineData(0x60, "Numpad 0")]
    public void KeyNames_areReadable(uint key, string expected)
    {
        Assert.Equal(expected, HotkeyBinding.DescribeKey(key));
    }

    [Fact]
    public void AnUnknownKey_fallsBackToItsCode()
    {
        Assert.Equal("0xFF", HotkeyBinding.DescribeKey(0xFF));
    }

    [Theory]
    [InlineData(0x10)] // Shift
    [InlineData(0x11)] // Control
    [InlineData(0x12)] // Alt
    [InlineData(0x5B)] // Left Windows
    [InlineData(0xA0)] // Left Shift
    public void ModifierKeys_areRecognised(uint key)
    {
        // While capturing, these mean "still holding modifiers", not "chose a key".
        Assert.True(HotkeyBinding.IsModifierKey(key));
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0x20)]
    [InlineData(0x70)]
    public void OrdinaryKeys_areNotModifiers(uint key)
    {
        Assert.False(HotkeyBinding.IsModifierKey(key));
    }

    [Fact]
    public void Clone_isIndependent()
    {
        HotkeyBinding original = HotkeyBinding.CreateDefault();
        HotkeyBinding copy = original.Clone();

        copy.Control = true;
        copy.Key = 0x41;

        Assert.False(original.Control);
        Assert.Equal(HotkeyBinding.VirtualKeySpace, original.Key);
    }

    [Fact]
    public void AnEmptyBinding_describesItselfAsNone()
    {
        Assert.Equal("None", new HotkeyBinding().ToString());
    }
}
