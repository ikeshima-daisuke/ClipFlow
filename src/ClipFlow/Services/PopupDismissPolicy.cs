using System;

namespace ClipFlow.Services;

/// <summary>見張りタイマーが打つべき手。</summary>
public enum DismissAction
{
    /// <summary>取り残しなし。</summary>
    None,

    /// <summary>本体ポップアップを隠す（プレビューも道連れで閉じる）。</summary>
    HideWindow,

    /// <summary>本体は隠れているのに開いたままのプレビューを閉じる。</summary>
    ClosePreview,
}

/// <summary>
/// 「ポップアップが画面に取り残されていないか」を判定する純粋ロジック（UI非依存）。
///
/// 本体を隠す経路は WPF の <c>OnDeactivated</c>（フォーカス喪失）とマウスアウトの2つだが、
/// どちらも取りこぼす条件がある — 前面化に失敗した回は <c>WM_ACTIVATE</c> を受けないので
/// <c>OnDeactivated</c> が永久に来ず、カーソルがウィンドウに入らなければ <c>MouseLeave</c> も来ない。
/// そこでイベントに頼らず、OSレベルの前面ウィンドウを定期的に見張って取り残しを検出する。
/// プレビューは本体とは別の最上位ウィンドウなので、本体が隠れているのに開いていたら単独で閉じる。
/// </summary>
public static class PopupDismissPolicy
{
    /// <summary>
    /// 表示直後、前面化の判定を見送る猶予。<c>Show()</c> 直後は前面移譲がまだ進行中で
    /// 前面ウィンドウが他プロセスのままのことがあり、そこで隠すと出した瞬間に消えてしまう。
    /// </summary>
    public static readonly TimeSpan ActivationGrace = TimeSpan.FromMilliseconds(600);

    /// <param name="windowVisible">本体ポップアップが表示中か。</param>
    /// <param name="previewOpen">プレビューポップアップが開いているか。</param>
    /// <param name="foregroundIsOurs">OSレベルの前面ウィンドウが自プロセスのものか（不明な場合も true 扱いにして誤爆を避ける）。</param>
    /// <param name="sinceShown">本体を表示してからの経過時間。</param>
    public static DismissAction Decide(bool windowVisible, bool previewOpen, bool foregroundIsOurs, TimeSpan sinceShown)
    {
        if (!windowVisible)
            return previewOpen ? DismissAction.ClosePreview : DismissAction.None;

        if (!foregroundIsOurs && sinceShown >= ActivationGrace)
            return DismissAction.HideWindow;

        return DismissAction.None;
    }
}
