using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// 「最新コピーがすぐに履歴へ反映されないことがある」バグの回帰防止。
/// 旧来の bool フラグ(SuppressNext)は、自前書き込みのこだまが届かないと
/// 次の本物コピーを誤って無視していた。シーケンス番号方式ではそれが起きない。
/// </summary>
public class SelfCopyGateTests
{
    [Fact]
    public void Echo_of_self_write_is_suppressed_once()
    {
        var gate = new SelfCopyGate();
        gate.ExpectSelfWrite(10);

        Assert.True(gate.ShouldSuppress(10));   // 自前書き込みのこだま → 無視
        Assert.False(gate.ShouldSuppress(10));  // 二度目は消費済みなので通す
    }

    [Fact] // ← これが本バグの凍結テスト
    public void Real_copy_with_different_sequence_is_never_suppressed()
    {
        var gate = new SelfCopyGate();
        gate.ExpectSelfWrite(10); // ペースト等で書き込んだ（こだまは結局届かない想定）

        // 本物の外部コピー。シーケンス番号は単調増加するので必ず別の値になる。
        // 旧 bool 実装ならここで取りこぼしていた。
        Assert.False(gate.ShouldSuppress(11));
        Assert.False(gate.ShouldSuppress(12));
    }

    [Fact]
    public void Delayed_echo_is_still_matched_after_a_real_copy()
    {
        var gate = new SelfCopyGate();
        gate.ExpectSelfWrite(10);

        // 番号不一致では予約を消さないので…
        Assert.False(gate.ShouldSuppress(11)); // 本物コピーは通る
        Assert.True(gate.ShouldSuppress(10));  // 遅れて届いた自前のこだまは無視できる
    }

    [Fact]
    public void No_expectation_means_nothing_is_suppressed()
    {
        var gate = new SelfCopyGate();
        Assert.False(gate.ShouldSuppress(0));
        Assert.False(gate.ShouldSuppress(1));
    }
}
