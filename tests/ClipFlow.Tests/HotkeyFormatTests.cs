using System.Windows.Input;
using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// ホットキーのWin32表現（MOD_*ビットマスク+仮想キー）とWPFのModifierKeys/Keyとの変換、
/// および表示文字列の組み立てを固定する。
/// </summary>
public class HotkeyFormatTests
{
    [Fact]
    public void ToWin32Modifiers_combines_all_flags()
    {
        var mods = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows;

        var result = HotkeyFormat.ToWin32Modifiers(mods);

        Assert.Equal(
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN,
            result);
    }

    [Fact]
    public void ToVirtualKey_converts_wpf_key_to_win32_vk()
    {
        Assert.Equal((uint)NativeMethods.VK_V, HotkeyFormat.ToVirtualKey(Key.V));
    }

    [Fact]
    public void Format_default_combo_is_ctrl_shift_v()
    {
        var text = HotkeyFormat.Format(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, NativeMethods.VK_V);

        Assert.Equal("Ctrl+Shift+V", text);
    }

    [Fact]
    public void Format_orders_modifiers_ctrl_alt_shift_win()
    {
        var mods = NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT | NativeMethods.MOD_ALT | NativeMethods.MOD_CONTROL;

        var text = HotkeyFormat.Format(mods, NativeMethods.VK_V);

        Assert.Equal("Ctrl+Alt+Shift+Win+V", text);
    }
}
