using System.Collections.Generic;
using System.Windows.Input;

namespace ClipFlow.Services;

/// <summary>
/// グローバルホットキーの Win32 表現（MOD_* のビットOR + 仮想キーコード）と、
/// キャプチャ用ダイアログが使う WPF の ModifierKeys/Key との相互変換・表示文字列化。
/// </summary>
internal static class HotkeyFormat
{
    public static uint ToWin32Modifiers(ModifierKeys mods)
    {
        uint result = 0;
        if (mods.HasFlag(ModifierKeys.Control)) result |= NativeMethods.MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Alt)) result |= NativeMethods.MOD_ALT;
        if (mods.HasFlag(ModifierKeys.Shift)) result |= NativeMethods.MOD_SHIFT;
        if (mods.HasFlag(ModifierKeys.Windows)) result |= NativeMethods.MOD_WIN;
        return result;
    }

    public static uint ToVirtualKey(Key key) => (uint)KeyInterop.VirtualKeyFromKey(key);

    /// <summary>Win32のMOD_*ビットマスクと仮想キーコードから "Ctrl+Shift+V" のような表示文字列を作る。</summary>
    public static string Format(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");

        var key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
        parts.Add(key == Key.None ? $"VK_{virtualKey:X2}" : key.ToString());

        return string.Join("+", parts);
    }

    /// <summary>組み合わせ・連打の両モードに対応した表示文字列を作る。</summary>
    public static string Format(HotkeySpec spec) => spec.IsDoubleTap
        ? $"{ModifierName(spec.Modifiers)}連打"
        : Format(spec.Modifiers, spec.VirtualKey);

    /// <summary>単一の MOD_* ビットから "Ctrl" のような表示名を作る（連打モードの対象キー表示用）。</summary>
    public static string ModifierName(uint modifierBit) => modifierBit switch
    {
        NativeMethods.MOD_CONTROL => "Ctrl",
        NativeMethods.MOD_ALT => "Alt",
        NativeMethods.MOD_SHIFT => "Shift",
        NativeMethods.MOD_WIN => "Win",
        _ => "?",
    };
}
