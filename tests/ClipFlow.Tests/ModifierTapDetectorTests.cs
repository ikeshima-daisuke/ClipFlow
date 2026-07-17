using System;
using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// Ctrl連打などの修飾キー単体2回押し判定（<see cref="ModifierTapDetector"/>）を固定する。
/// 「押しっぱなしで他キーを押す（Ctrl+C 等）と、その後の単発 Ctrl 押下を誤って
/// 2回押しと判定してしまう」誤爆パターンを主眼に置く。
/// </summary>
public class ModifierTapDetectorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const ushort VkLControl = 0xA2;
    private const ushort VkRControl = 0xA3;
    private const ushort VkC = 0x43;
    private const ushort VkLShift = 0xA0;

    private static ModifierTapDetector CreateCtrlDetector() =>
        new(NativeMethods.MOD_CONTROL, TimeSpan.FromMilliseconds(400));

    [Fact]
    public void Quick_double_tap_of_target_modifier_fires()
    {
        var d = CreateCtrlDetector();

        d.Feed(VkLControl, isKeyDown: true, T0);
        Assert.False(d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(50))); // 1回目

        d.Feed(VkLControl, isKeyDown: true, T0.AddMilliseconds(150));
        Assert.True(d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(200))); // 2回目 → 確定
    }

    [Fact]
    public void Slow_second_tap_beyond_threshold_does_not_fire()
    {
        var d = CreateCtrlDetector();

        d.Feed(VkLControl, isKeyDown: true, T0);
        d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(50));

        d.Feed(VkLControl, isKeyDown: true, T0.AddMilliseconds(600));
        Assert.False(d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(650))); // 間隔が空きすぎ
    }

    [Fact] // ← Ctrl+C の直後に単発 Ctrl を押しても誤爆しないことの凍結テスト
    public void Ctrl_combined_with_another_key_does_not_count_as_a_tap()
    {
        var d = CreateCtrlDetector();

        d.Feed(VkLControl, isKeyDown: true, T0);
        d.Feed(VkC, isKeyDown: true, T0.AddMilliseconds(20)); // Ctrl+C
        d.Feed(VkC, isKeyDown: false, T0.AddMilliseconds(40));
        Assert.False(d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(60))); // 組み合わせなので不発

        // 直後に単発でCtrlをもう一度押しても、直前は「組み合わせ」だったので初回タップ扱い
        d.Feed(VkLControl, isKeyDown: true, T0.AddMilliseconds(100));
        Assert.False(d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(120)));
    }

    [Fact]
    public void Ctrl_held_together_with_shift_does_not_count_as_a_tap()
    {
        var d = CreateCtrlDetector();

        d.Feed(VkLControl, isKeyDown: true, T0);
        d.Feed(VkLShift, isKeyDown: true, T0.AddMilliseconds(10)); // Ctrl+Shift
        d.Feed(VkLShift, isKeyDown: false, T0.AddMilliseconds(30));
        Assert.False(d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(50)));
    }

    [Fact]
    public void Left_and_right_control_are_treated_as_the_same_group()
    {
        var d = CreateCtrlDetector();

        d.Feed(VkLControl, isKeyDown: true, T0);
        d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(50));

        d.Feed(VkRControl, isKeyDown: true, T0.AddMilliseconds(150));
        Assert.True(d.Feed(VkRControl, isKeyDown: false, T0.AddMilliseconds(200)));
    }

    [Fact]
    public void Double_tap_of_a_different_modifier_is_ignored()
    {
        var d = CreateCtrlDetector(); // Ctrl監視中にShiftを連打しても反応しない

        d.Feed(VkLShift, isKeyDown: true, T0);
        d.Feed(VkLShift, isKeyDown: false, T0.AddMilliseconds(50));
        d.Feed(VkLShift, isKeyDown: true, T0.AddMilliseconds(150));
        Assert.False(d.Feed(VkLShift, isKeyDown: false, T0.AddMilliseconds(200)));
    }

    [Fact]
    public void After_firing_a_third_tap_alone_does_not_immediately_refire()
    {
        var d = CreateCtrlDetector();

        d.Feed(VkLControl, isKeyDown: true, T0);
        d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(50));
        d.Feed(VkLControl, isKeyDown: true, T0.AddMilliseconds(150));
        Assert.True(d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(200))); // 1-2回目で確定

        d.Feed(VkLControl, isKeyDown: true, T0.AddMilliseconds(300));
        Assert.False(d.Feed(VkLControl, isKeyDown: false, T0.AddMilliseconds(350))); // 3回目単独は不発
    }
}
