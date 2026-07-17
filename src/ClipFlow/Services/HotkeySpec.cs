namespace ClipFlow.Services;

/// <summary>
/// ホットキー設定を「組み合わせ（Ctrl+Shift+V等）」と「修飾キー単体の連打（Ctrl連打等）」の
/// どちらのモードでも表せる値。<see cref="AppSettings"/>・<see cref="HotkeyDialog"/>・
/// <see cref="App"/> の間でモードを問わず1つの値として受け渡すために使う。
/// </summary>
public readonly struct HotkeySpec
{
    public bool IsDoubleTap { get; }

    /// <summary>組み合わせモード: MOD_* のビットOR。連打モード: 対象となる単一の MOD_* ビット。</summary>
    public uint Modifiers { get; }

    /// <summary>組み合わせモードでのみ意味を持つ仮想キーコード。</summary>
    public uint VirtualKey { get; }

    private HotkeySpec(bool isDoubleTap, uint modifiers, uint virtualKey)
    {
        IsDoubleTap = isDoubleTap;
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public static HotkeySpec Combo(uint modifiers, uint virtualKey) => new(false, modifiers, virtualKey);

    public static HotkeySpec DoubleTap(uint modifierBit) => new(true, modifierBit, 0);
}
