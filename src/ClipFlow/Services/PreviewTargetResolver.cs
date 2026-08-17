namespace ClipFlow.Services;

/// <summary>
/// 「いまプレビューに出すべき項目」を決める純粋なロジック（UI非依存）。
/// 入力は2系統ある — マウスオーバーと選択（矢印キー／クリック）。
/// ホバー中はホバー先が勝ち、一覧から外れたら選択項目のプレビューへ戻る。
/// 最初のホバーだけ遅延（<see cref="CommitPendingHover"/> の呼び出し）を挟むのは、
/// 一覧を横切っただけでポップアップが点滅するのを防ぐため。
/// </summary>
public sealed class PreviewTargetResolver<T> where T : class
{
    private T? _selected;
    private T? _hovered;
    private T? _pending;

    /// <summary>いまプレビューすべき項目。null ならポップアップを閉じる。</summary>
    public T? Target => _hovered ?? _selected;

    /// <summary>遅延待ちのホバーがあるか（呼び出し側がタイマーを回すかの判断に使う）。</summary>
    public bool HasPendingHover => _pending != null;

    /// <summary>
    /// 選択が変わった。ホバー由来の表示は捨て、キーボード操作を優先させる
    /// （矢印キーでスクロールした結果カーソル下に来ただけの項目に持っていかれないため）。
    /// </summary>
    /// <returns>プレビュー対象が変わったか。</returns>
    public bool Select(T? item)
    {
        var before = Target;
        _selected = item;
        _hovered = null;
        _pending = null;
        return !ReferenceEquals(before, Target);
    }

    /// <summary>
    /// 項目の上にマウスが乗った。まだホバー表示が出ていなければ遅延待ちにし、
    /// 既に出ているなら遅延なしで切り替える（なぞる操作に追従させるため）。
    /// </summary>
    /// <returns>プレビュー対象が変わったか。false でも <see cref="HasPendingHover"/> が true なら待機中。</returns>
    public bool HoverEnter(T item)
    {
        if (ReferenceEquals(item, _hovered))
        {
            _pending = null;
            return false;
        }

        _pending = item;
        return _hovered != null && CommitPendingHover();
    }

    /// <summary>遅延が経過した。待機中のホバーを表示対象へ昇格させる。</summary>
    /// <returns>プレビュー対象が変わったか。</returns>
    public bool CommitPendingHover()
    {
        if (_pending == null) return false;

        var before = Target;
        _hovered = _pending;
        _pending = null;
        return !ReferenceEquals(before, Target);
    }

    /// <summary>一覧の外（または項目のない余白）へマウスが出た。選択項目のプレビューへ戻す。</summary>
    /// <returns>プレビュー対象が変わったか。</returns>
    public bool HoverLeave()
    {
        var before = Target;
        _hovered = null;
        _pending = null;
        return !ReferenceEquals(before, Target);
    }

    /// <summary>ポップアップを閉じるとき（ウィンドウを隠すとき）に全状態を捨てる。</summary>
    public void Reset()
    {
        _selected = null;
        _hovered = null;
        _pending = null;
    }
}
