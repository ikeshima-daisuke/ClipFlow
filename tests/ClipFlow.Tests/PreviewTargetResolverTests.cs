using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// プレビュー対象の決定ロジック（<see cref="PreviewTargetResolver{T}"/>）を固定する。
/// 「マウスオーバー」と「選択（矢印キー）」の2系統が同時に来たときの優先順位と、
/// なぞっている間に出っぱなし／消えっぱなしにならないことが主眼。
/// </summary>
public class PreviewTargetResolverTests
{
    private const string A = "A";
    private const string B = "B";

    private static PreviewTargetResolver<string> Create() => new();

    [Fact]
    public void Nothing_selected_or_hovered_means_no_preview()
    {
        var r = Create();

        Assert.Null(r.Target);
        Assert.False(r.HasPendingHover);
    }

    [Fact]
    public void Selection_alone_previews_the_selected_item()
    {
        var r = Create();

        Assert.True(r.Select(A));
        Assert.Equal(A, r.Target);
    }

    [Fact] // ← 一覧を横切っただけで出ないよう、最初のホバーは遅延を挟む
    public void First_hover_waits_for_the_delay_before_showing()
    {
        var r = Create();
        r.Select(A);

        Assert.False(r.HoverEnter(B));   // まだ切り替わらない
        Assert.Equal(A, r.Target);
        Assert.True(r.HasPendingHover);

        Assert.True(r.CommitPendingHover());
        Assert.Equal(B, r.Target);
        Assert.False(r.HasPendingHover);
    }

    [Fact] // ← すでに出ている状態でのなぞり移動は遅延なしで追従させる
    public void Hover_switches_immediately_once_the_preview_is_already_shown()
    {
        var r = Create();
        r.HoverEnter(A);
        r.CommitPendingHover();

        Assert.True(r.HoverEnter(B));
        Assert.Equal(B, r.Target);
        Assert.False(r.HasPendingHover);
    }

    [Fact]
    public void Re_entering_the_same_hovered_item_changes_nothing()
    {
        var r = Create();
        r.HoverEnter(A);
        r.CommitPendingHover();

        Assert.False(r.HoverEnter(A));
        Assert.False(r.HasPendingHover);
        Assert.Equal(A, r.Target);
    }

    [Fact]
    public void Leaving_the_list_falls_back_to_the_selected_item()
    {
        var r = Create();
        r.Select(A);
        r.HoverEnter(B);
        r.CommitPendingHover();

        Assert.True(r.HoverLeave());
        Assert.Equal(A, r.Target);
    }

    [Fact]
    public void Leaving_the_list_cancels_a_hover_that_never_showed()
    {
        var r = Create();
        r.Select(A);
        r.HoverEnter(B);

        Assert.False(r.HoverLeave());        // 表示対象は変わっていない
        Assert.False(r.HasPendingHover);     // 待機中のホバーも取り消す
        Assert.False(r.CommitPendingHover()); // 遅延タイマーが遅れて発火しても昇格しない
        Assert.Equal(A, r.Target);
    }

    [Fact] // ← 矢印キーで動かした直後にホバーが割り込んで戻らないこと
    public void Selection_wins_over_an_existing_hover()
    {
        var r = Create();
        r.HoverEnter(A);
        r.CommitPendingHover();

        Assert.True(r.Select(B));
        Assert.Equal(B, r.Target);
        Assert.False(r.HasPendingHover);
    }

    [Fact]
    public void Selection_also_cancels_a_pending_hover()
    {
        var r = Create();
        r.Select(A);
        r.HoverEnter(B);

        r.Select(A);
        Assert.False(r.HasPendingHover);
        Assert.False(r.CommitPendingHover());
        Assert.Equal(A, r.Target);
    }

    [Fact]
    public void Reset_drops_everything()
    {
        var r = Create();
        r.Select(A);
        r.HoverEnter(B);

        r.Reset();

        Assert.Null(r.Target);
        Assert.False(r.HasPendingHover);
    }

    [Fact]
    public void Clearing_the_selection_hides_the_preview()
    {
        var r = Create();
        r.Select(A);

        Assert.True(r.Select(null));
        Assert.Null(r.Target);
    }
}
