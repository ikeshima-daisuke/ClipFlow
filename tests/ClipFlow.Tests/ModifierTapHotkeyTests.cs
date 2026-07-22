using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// 低レベルキーボードフックのコールバック内で重い処理を走らせないことを固定する。
/// Windows は WH_KEYBOARD_LL のコールバックが LowLevelHooksTimeout（既定300ms）を超えると
/// フックを無言で外す。ハンドラ（ポップアップ表示）をコールバック内で同期実行していると、
/// 一度表示しただけで以後ホットキーが永久に効かなくなる。
/// </summary>
public class ModifierTapHotkeyTests
{
    /// <summary>対象修飾キーを2回叩く一連のキーイベントを流す。</summary>
    private static void FeedDoubleTap(ModifierTapHotkey hotkey, ushort vk, DateTime start)
    {
        hotkey.ProcessKeyEvent(vk, isDown: true, start);
        hotkey.ProcessKeyEvent(vk, isDown: false, start.AddMilliseconds(30));
        hotkey.ProcessKeyEvent(vk, isDown: true, start.AddMilliseconds(120));
        hotkey.ProcessKeyEvent(vk, isDown: false, start.AddMilliseconds(150));
    }

    [Fact]
    public void DoubleTap_does_not_invoke_handler_inside_the_hook_callback()
    {
        var queued = new List<Action>();
        using var hotkey = new ModifierTapHotkey(NativeMethods.MOD_CONTROL, queued.Add, installHook: false);
        bool fired = false;
        hotkey.Pressed += () => fired = true;

        FeedDoubleTap(hotkey, NativeMethods.VK_LCONTROL, DateTime.UtcNow);

        // フック内では呼ばない（＝コールバックは即座に返る）
        Assert.False(fired);
        Assert.Single(queued);

        // 投函された処理を後から実行すると初めてハンドラが動く
        queued[0]();
        Assert.True(fired);
    }

    [Fact]
    public void Single_tap_dispatches_nothing()
    {
        var queued = new List<Action>();
        using var hotkey = new ModifierTapHotkey(NativeMethods.MOD_CONTROL, queued.Add, installHook: false);

        var now = DateTime.UtcNow;
        hotkey.ProcessKeyEvent(NativeMethods.VK_LCONTROL, isDown: true, now);
        hotkey.ProcessKeyEvent(NativeMethods.VK_LCONTROL, isDown: false, now.AddMilliseconds(30));

        Assert.Empty(queued);
    }

    [Fact]
    public void Rebind_switches_the_watched_modifier()
    {
        var queued = new List<Action>();
        using var hotkey = new ModifierTapHotkey(NativeMethods.MOD_CONTROL, queued.Add, installHook: false);

        hotkey.Rebind(NativeMethods.MOD_SHIFT);

        FeedDoubleTap(hotkey, NativeMethods.VK_LCONTROL, DateTime.UtcNow);
        Assert.Empty(queued);

        FeedDoubleTap(hotkey, NativeMethods.VK_LSHIFT, DateTime.UtcNow);
        Assert.Single(queued);
    }
}
