using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// 「ポップアップが画面に取り残されていないか」の判定ロジック（<see cref="PopupDismissPolicy"/>）を固定する。
/// WPF の <c>OnDeactivated</c> は前面化に失敗した回（＝一度もアクティブになっていない回）には
/// 永久に来ないため、OSレベルの前面ウィンドウを見張って取り残しを検出する必要がある。
/// </summary>
public class PopupDismissPolicyTests
{
    private static readonly TimeSpan AfterGrace = PopupDismissPolicy.ActivationGrace + TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan WithinGrace = PopupDismissPolicy.ActivationGrace - TimeSpan.FromMilliseconds(1);

    [Fact]
    public void Hidden_window_with_a_leftover_preview_closes_the_preview()
    {
        // 本体が隠れているのにプレビューだけ開いている＝別ウィンドウが画面に取り残された状態
        var action = PopupDismissPolicy.Decide(
            windowVisible: false, previewOpen: true, foregroundIsOurs: false, sinceShown: AfterGrace);

        Assert.Equal(DismissAction.ClosePreview, action);
    }

    [Fact]
    public void Hidden_window_without_preview_needs_nothing()
    {
        var action = PopupDismissPolicy.Decide(
            windowVisible: false, previewOpen: false, foregroundIsOurs: false, sinceShown: AfterGrace);

        Assert.Equal(DismissAction.None, action);
    }

    [Fact]
    public void Visible_and_foreground_is_ours_stays_open()
    {
        var action = PopupDismissPolicy.Decide(
            windowVisible: true, previewOpen: true, foregroundIsOurs: true, sinceShown: AfterGrace);

        Assert.Equal(DismissAction.None, action);
    }

    [Fact]
    public void Visible_but_foreground_went_elsewhere_hides_the_window()
    {
        // OnDeactivated が来なかった（＝一度も前面になれなかった）回の救済
        var action = PopupDismissPolicy.Decide(
            windowVisible: true, previewOpen: false, foregroundIsOurs: false, sinceShown: AfterGrace);

        Assert.Equal(DismissAction.HideWindow, action);
    }

    [Fact]
    public void Foreground_check_is_suspended_right_after_showing()
    {
        // 表示直後は前面化が完了していないことがあるので、猶予中は隠さない（出した瞬間に消える事故防止）
        var action = PopupDismissPolicy.Decide(
            windowVisible: true, previewOpen: false, foregroundIsOurs: false, sinceShown: WithinGrace);

        Assert.Equal(DismissAction.None, action);
    }

    [Fact]
    public void Grace_boundary_is_inclusive()
    {
        var action = PopupDismissPolicy.Decide(
            windowVisible: true, previewOpen: false, foregroundIsOurs: false,
            sinceShown: PopupDismissPolicy.ActivationGrace);

        Assert.Equal(DismissAction.HideWindow, action);
    }

    [Fact]
    public void Hiding_the_window_takes_priority_over_a_still_open_preview()
    {
        // 本体を隠せばプレビューも道連れで閉じるので、まず本体を隠す指示だけ返す
        var action = PopupDismissPolicy.Decide(
            windowVisible: true, previewOpen: true, foregroundIsOurs: false, sinceShown: AfterGrace);

        Assert.Equal(DismissAction.HideWindow, action);
    }
}
