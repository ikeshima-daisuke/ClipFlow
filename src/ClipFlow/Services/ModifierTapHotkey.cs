using System;
using System.Runtime.InteropServices;

namespace ClipFlow.Services;

/// <summary>
/// 修飾キー単体の素早い2回押し（例: Ctrl連打）を検出するグローバルトリガー。
/// RegisterHotKey は修飾キー単体を扱えないため、WH_KEYBOARD_LL の低レベルキーボードフックで
/// 押下/離上イベントを拾い、実際の判定は <see cref="ModifierTapDetector"/>（テスト可能な純粋クラス）
/// に委譲する。
/// </summary>
internal sealed class ModifierTapHotkey : IHotkeyTrigger
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(400);

    // フック解除までデリゲートをフィールドで保持し続けないと GC に回収され、
    // ネイティブ側からのコールバックが不正なアドレスを呼ぶことになる。
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private ModifierTapDetector _detector;
    private IntPtr _hookHandle;

    public event Action? Pressed;

    public ModifierTapHotkey(uint modifierBit)
    {
        _detector = new ModifierTapDetector(modifierBit, Threshold);
        _proc = HookCallback;
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
    }

    /// <summary>フックの設置に成功したか（通常はほぼ常に true）。</summary>
    public bool IsRegistered => _hookHandle != IntPtr.Zero;

    /// <summary>監視対象の修飾キーを変更する（フックは張り直さず判定器だけ差し替える）。</summary>
    public void Rebind(uint modifierBit)
    {
        _detector = new ModifierTapDetector(modifierBit, Threshold);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            bool isDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
            bool isUp = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (_detector.Feed((ushort)data.vkCode, isDown, DateTime.UtcNow))
                    Pressed?.Invoke();
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
