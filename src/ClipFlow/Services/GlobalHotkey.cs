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
    private uint _modifiers;
    private uint _virtualKey;

    public event Action? Pressed;

    public GlobalHotkey(IntPtr hwnd, uint modifiers, uint virtualKey)
    {
        _hwnd = hwnd;
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("HWND からソースを取得できません。");
        _source.AddHook(WndProc);
        _registered = NativeMethods.RegisterHotKey(
            _hwnd, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
    }

    /// <summary>登録に成功したか（他アプリが同じキーを取っていると false）。</summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// 別の組み合わせへ登録し直す。同じIDを使い回す都合上、いったん解除してから登録する。
    /// 失敗時（他アプリが既に使用中）は、直前まで有効だった組み合わせへの復帰を試みたうえで
    /// false を返す（ショートカットが完全に効かなくなる状態を避けるため）。
    /// </summary>
    public bool Rebind(uint modifiers, uint virtualKey)
    {
        var previousModifiers = _modifiers;
        var previousVirtualKey = _virtualKey;
        var hadPrevious = _registered;

        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }

        _registered = NativeMethods.RegisterHotKey(
            _hwnd, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
        if (_registered)
        {
            _modifiers = modifiers;
            _virtualKey = virtualKey;
            return true;
        }

        if (hadPrevious)
        {
            _registered = NativeMethods.RegisterHotKey(
                _hwnd, HotkeyId, previousModifiers | NativeMethods.MOD_NOREPEAT, previousVirtualKey);
            if (_registered)
            {
                _modifiers = previousModifiers;
                _virtualKey = previousVirtualKey;
            }
        }
        return false;
    }

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
