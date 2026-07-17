using System;
using System.Collections.Generic;

namespace ClipFlow.Services;

/// <summary>
/// 修飾キー単体の「素早い2回押し」を検出する純粋なロジック。OS依存部分（低レベルフック）を
/// 持たず、キーの押下/離上イベントを渡すだけで判定できるためテストで固定する
/// （<see cref="ModifierTapHotkey"/> から呼ばれる）。
/// 対象キーが押しっぱなしの間に他のキー（対象外の修飾キーも含む）が押されたら「組み合わせ」
/// とみなし連打判定から除外する。これがないと Ctrl+C の直後の単発 Ctrl 押下などで誤爆する。
/// </summary>
internal sealed class ModifierTapDetector
{
    private readonly uint _targetModifier;
    private readonly TimeSpan _threshold;
    private readonly Dictionary<uint, State> _states = new();

    public ModifierTapDetector(uint targetModifier, TimeSpan threshold)
    {
        _targetModifier = targetModifier;
        _threshold = threshold;
    }

    private sealed class State
    {
        public bool IsDown;
        public bool CombinedWithOther;
        public DateTime? LastCleanTapUp;
    }

    /// <summary>キーイベントを1件渡す。対象修飾キーの2回押しが確定した瞬間だけ true を返す。</summary>
    public bool Feed(ushort virtualKeyCode, bool isKeyDown, DateTime now)
    {
        var group = ModifierGroupOf(virtualKeyCode);

        if (group == 0)
        {
            if (isKeyDown)
                MarkAllHeldAsCombined();
            return false;
        }

        var state = GetState(group);

        if (isKeyDown)
        {
            if (group != _targetModifier)
            {
                MarkAllHeldAsCombined();
                state.IsDown = true;
                return false;
            }

            if (!state.IsDown)
            {
                state.IsDown = true;
                state.CombinedWithOther = false;
            }
            return false;
        }

        state.IsDown = false;
        if (group != _targetModifier)
            return false;

        if (state.CombinedWithOther)
        {
            state.CombinedWithOther = false;
            state.LastCleanTapUp = null;
            return false;
        }

        if (state.LastCleanTapUp is DateTime last && now - last <= _threshold)
        {
            state.LastCleanTapUp = null;
            return true;
        }

        state.LastCleanTapUp = now;
        return false;
    }

    private void MarkAllHeldAsCombined()
    {
        foreach (var state in _states.Values)
            if (state.IsDown) state.CombinedWithOther = true;
    }

    private State GetState(uint group)
    {
        if (!_states.TryGetValue(group, out var state))
        {
            state = new State();
            _states[group] = state;
        }
        return state;
    }

    private static uint ModifierGroupOf(ushort vk) => vk switch
    {
        NativeMethods.VK_CONTROL or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL => NativeMethods.MOD_CONTROL,
        NativeMethods.VK_SHIFT or NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT => NativeMethods.MOD_SHIFT,
        NativeMethods.VK_MENU or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU => NativeMethods.MOD_ALT,
        NativeMethods.VK_LWIN or NativeMethods.VK_RWIN => NativeMethods.MOD_WIN,
        _ => 0u,
    };
}
