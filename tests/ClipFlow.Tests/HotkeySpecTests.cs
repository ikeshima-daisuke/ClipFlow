using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// HotkeySpec の現状の振る舞いを固定する特性化テスト。
/// 組み合わせ/連打のどちらのモードでも1つの値として受け渡せることを確認する。
/// </summary>
public class HotkeySpecTests
{
    [Fact]
    public void Combo_keeps_modifiers_and_virtual_key()
    {
        var spec = HotkeySpec.Combo((uint)(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT), (uint)NativeMethods.VK_V);

        Assert.False(spec.IsDoubleTap);
        Assert.Equal((uint)(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT), spec.Modifiers);
        Assert.Equal((uint)NativeMethods.VK_V, spec.VirtualKey);
    }

    [Fact]
    public void DoubleTap_keeps_single_modifier_bit_and_zero_virtual_key()
    {
        var spec = HotkeySpec.DoubleTap((uint)NativeMethods.MOD_CONTROL);

        Assert.True(spec.IsDoubleTap);
        Assert.Equal((uint)NativeMethods.MOD_CONTROL, spec.Modifiers);
        Assert.Equal(0u, spec.VirtualKey);
    }

    [Fact]
    public void Default_value_is_combo_mode_with_no_keys()
    {
        var spec = default(HotkeySpec);

        Assert.False(spec.IsDoubleTap);
        Assert.Equal(0u, spec.Modifiers);
        Assert.Equal(0u, spec.VirtualKey);
    }
}
