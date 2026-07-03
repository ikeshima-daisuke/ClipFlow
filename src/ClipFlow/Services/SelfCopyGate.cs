namespace ClipFlow.Services;

/// <summary>
/// 自前のクリップボード書き込み（コピー/ペースト）の「こだま」だけを無視するためのゲート。
///
/// 旧実装は単純な bool（SuppressNext）で「次に来た通知を1回だけ無視」していた。
/// しかし自前書き込みの通知が届かない／本物のコピーと前後すると、フラグが立ちっぱなしになり
/// 次の本物のコピーを取りこぼしていた（＝最新コピーが履歴に出ないことがある）。
///
/// クリップボードのシーケンス番号（書き込みごとに単調増加）で一致判定すれば、
/// 取りこぼした「こだま」予約が将来の本物コピーを誤って無視することは原理的に起きない。
/// </summary>
public sealed class SelfCopyGate
{
    private uint? _expectedSequence;

    /// <summary>自前で書き込んだ直後のシーケンス番号を登録する。</summary>
    public void ExpectSelfWrite(uint sequence) => _expectedSequence = sequence;

    /// <summary>
    /// 入ってきたクリップボード変更のシーケンス番号を渡す。
    /// 自前書き込みのこだま（番号一致）なら true を返し、その予約を一度きりで消費する。
    /// 別の番号（=本物のコピー）は決して無視しない。番号一致以外では予約を消さないので、
    /// 遅れて届いたこだまも取りこぼさず無視できる。
    /// </summary>
    public bool ShouldSuppress(uint sequence)
    {
        if (_expectedSequence is uint expected && expected == sequence)
        {
            _expectedSequence = null;
            return true;
        }
        return false;
    }
}
