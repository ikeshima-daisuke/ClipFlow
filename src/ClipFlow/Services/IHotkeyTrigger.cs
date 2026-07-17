using System;

namespace ClipFlow.Services;

/// <summary>
/// 履歴ポップアップを呼び出すグローバルトリガーの共通インターフェース。
/// 通常の修飾+キーの組み合わせ（<see cref="GlobalHotkey"/>）と、
/// 修飾キー単体の連打（<see cref="ModifierTapHotkey"/>）を App から同じ扱いで使うために用意する。
/// </summary>
internal interface IHotkeyTrigger : IDisposable
{
    event Action? Pressed;

    /// <summary>登録に成功したか（組み合わせモードでは他アプリとの競合で false になりうる）。</summary>
    bool IsRegistered { get; }
}
