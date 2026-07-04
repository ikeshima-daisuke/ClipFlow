using System;
using System.IO;
using System.Linq;
using ClipFlow.Models;
using ClipFlow.Services;

namespace ClipFlow.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly HistoryStore _store;
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

    public HistoryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ClipFlowTests", Guid.NewGuid().ToString("N"));
        _store = new HistoryStore(Path.Combine(_dir, "test.db"), Path.Combine(_dir, "images"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 無視 */ }
    }

    private static ClipItem Text(string s, int secs, bool pinned = false) => new()
    {
        Kind = ClipKind.Text,
        Text = s,
        Preview = s,
        Hash = "t:" + s,
        Pinned = pinned,
        CreatedAt = Base.AddSeconds(secs),
    };

    [Fact]
    public void Add_persists_and_returns_newest_first()
    {
        _store.Add(Text("A", 0));
        _store.Add(Text("B", 1));

        var all = _store.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Equal("B", all[0].Text); // 新しい方が先頭
        Assert.Equal("A", all[1].Text);
    }

    [Fact]
    public void Duplicate_hash_is_bumped_to_top_not_duplicated()
    {
        _store.Add(Text("A", 0));
        _store.Add(Text("B", 1));
        _store.Add(Text("A", 2)); // 同じ内容を再コピー

        var all = _store.GetAll();

        Assert.Equal(2, all.Count);       // 重複追加されない
        Assert.Equal("A", all[0].Text);   // 先頭へ繰り上がる
    }

    [Fact]
    public void Exceeding_100_evicts_oldest_unpinned()
    {
        for (int i = 0; i < 105; i++)
            _store.Add(Text($"item{i}", i));

        var all = _store.GetAll();

        Assert.Equal(100, all.Count);
        Assert.Equal("item104", all[0].Text);                  // 最新が残る
        Assert.DoesNotContain(all, x => x.Text == "item0");    // 最古は削除
        Assert.DoesNotContain(all, x => x.Text == "item4");
        Assert.Contains(all, x => x.Text == "item5");          // 境界
    }

    [Fact]
    public void Pinned_items_survive_eviction()
    {
        _store.Add(Text("PINNED", 0, pinned: true)); // 古いがピン留め
        for (int i = 1; i <= 105; i++)
            _store.Add(Text($"item{i}", i));

        var all = _store.GetAll();

        Assert.Contains(all, x => x.Text == "PINNED");                       // 残存
        Assert.Equal(100, all.Count(x => !x.Pinned));                        // 非ピンは100で頭打ち
    }

    [Fact]
    public void Delete_removes_only_that_item()
    {
        var a = _store.Add(Text("A", 0));
        _store.Add(Text("B", 1));

        _store.Delete(a.Id);

        var all = _store.GetAll();
        Assert.Single(all);
        Assert.Equal("B", all[0].Text);
    }

    [Fact]
    public void Html_and_rtf_round_trip_when_present()
    {
        var item = new ClipItem
        {
            Kind = ClipKind.Text,
            Text = "plain",
            Html = "<html>...</html>",
            Rtf = @"{\rtf1 ...}",
            Preview = "plain",
            Hash = "t:plain",
            CreatedAt = Base,
        };

        _store.Add(item);

        var all = _store.GetAll();
        Assert.Single(all);
        Assert.Equal("<html>...</html>", all[0].Html);
        Assert.Equal(@"{\rtf1 ...}", all[0].Rtf);
    }

    [Fact]
    public void Html_and_rtf_are_null_when_not_captured()
    {
        _store.Add(Text("plain only", 0));

        var all = _store.GetAll();
        Assert.Null(all[0].Html);
        Assert.Null(all[0].Rtf);
    }

    [Fact]
    public void Files_kind_persists_path_list_in_text()
    {
        var paths = new[] { @"C:\docs\a.pdf", @"C:\docs\b.pdf" };
        var item = new ClipItem
        {
            Kind = ClipKind.Files,
            Text = ClipItem.JoinFilePaths(paths),
            Preview = "a.pdf 他1件",
            Hash = "f:test",
            CreatedAt = Base,
        };

        _store.Add(item);

        var all = _store.GetAll();
        Assert.Single(all);
        Assert.Equal(ClipKind.Files, all[0].Kind);
        Assert.Equal(paths, ClipItem.SplitFilePaths(all[0].Text));
    }

    [Fact]
    public void MaxItems_zero_or_less_means_unlimited()
    {
        var unlimited = new HistoryStore(Path.Combine(_dir, "unlimited.db"), Path.Combine(_dir, "unlimited_images"), maxItems: 0);
        for (int i = 0; i < 150; i++)
            unlimited.Add(Text($"item{i}", i));

        Assert.Equal(150, unlimited.GetAll().Count);
    }

    [Fact]
    public void MaxItems_can_be_raised_at_runtime_but_already_evicted_items_stay_gone()
    {
        var limited = new HistoryStore(Path.Combine(_dir, "raise.db"), Path.Combine(_dir, "raise_images"), maxItems: 3);
        for (int i = 0; i < 5; i++)
            limited.Add(Text($"item{i}", i));
        Assert.Equal(3, limited.GetAll().Count); // 上限3で既に2件evict済み

        limited.MaxItems = 0; // 無制限へ変更
        for (int i = 5; i < 10; i++)
            limited.Add(Text($"item{i}", i));

        Assert.Equal(8, limited.GetAll().Count); // 残っていた3 + 新規5。evict済みの2件は戻らない
    }

    [Fact]
    public void Clear_keeps_pinned_when_requested()
    {
        _store.Add(Text("A", 0));
        _store.Add(Text("P", 1, pinned: true));

        _store.Clear(includePinned: false);

        var all = _store.GetAll();
        Assert.Single(all);
        Assert.Equal("P", all[0].Text);
    }
}
