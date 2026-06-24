using System;
using System.Windows.Interop;

namespace ClipFlow.Services;

/// <summary>
/// グローバルホットキー登録。既定は Ctrl+Shift+V。
/// 指定 HWND のメッセージループで WM_HOTKEY を拾って通知する。
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0xC1F0;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey(IntPtr hwnd, uint modifiers, uint virtualKey)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("HWND からソースを取得できません。");
        _source.AddHook(WndProc);
        _registered = NativeMethods.RegisterHotKey(
            _hwnd, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
    }

    /// <summary>登録に成功したか（他アプリが同じキーを取っていると false）。</summary>
    public bool IsRegistered => _registered;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        _source.RemoveHook(WndProc);
    }
}
