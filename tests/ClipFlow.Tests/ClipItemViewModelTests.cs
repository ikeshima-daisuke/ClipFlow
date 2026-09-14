using System.IO;
using ClipFlow.Models;
using ClipFlow.ViewModels;

namespace ClipFlow.Tests;

/// <summary>
/// ClipItemViewModel の現状の振る舞いを固定する特性化テスト。
/// 種別の判定・書式の有無・経過時間の表示文言・ピン状態の書き戻しを確認する。
/// </summary>
public class ClipItemViewModelTests
{
    [Theory]
    [InlineData(ClipKind.Text, true, false, false)]
    [InlineData(ClipKind.Image, false, true, false)]
    [InlineData(ClipKind.Files, false, false, true)]
    public void Kind_flags_follow_item_kind(ClipKind kind, bool isText, bool isImage, bool isFiles)
    {
        var vm = new ClipItemViewModel(new ClipItem { Kind = kind });

        Assert.Equal(isText, vm.IsText);
        Assert.Equal(isImage, vm.IsImage);
        Assert.Equal(isFiles, vm.IsFiles);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("<b>x</b>", null, true)]
    [InlineData(null, @"{\rtf1 x}", true)]
    public void HasFormatting_is_true_when_html_or_rtf_exists(string? html, string? rtf, bool expected)
    {
        var vm = new ClipItemViewModel(new ClipItem { Html = html, Rtf = rtf });

        Assert.Equal(expected, vm.HasFormatting);
    }

    [Fact]
    public void Preview_and_FullText_pass_through_item_values()
    {
        var vm = new ClipItemViewModel(new ClipItem { Preview = "短い", Text = "全文テキスト" });

        Assert.Equal("短い", vm.Preview);
        Assert.Equal("全文テキスト", vm.FullText);
    }

    [Fact]
    public void FullText_is_empty_when_text_is_null()
    {
        var vm = new ClipItemViewModel(new ClipItem { Text = null });

        Assert.Equal(string.Empty, vm.FullText);
    }

    [Fact]
    public void Images_are_null_for_non_image_items_even_with_paths()
    {
        var vm = new ClipItemViewModel(new ClipItem { Kind = ClipKind.Text, ThumbPath = "thumb.png", ImagePath = "image.png" });

        Assert.Null(vm.Thumbnail);
        Assert.Null(vm.FullImage);
    }

    [Fact]
    public void Images_are_null_when_image_files_are_missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"clipflow-missing-{Guid.NewGuid():N}.png");
        var vm = new ClipItemViewModel(new ClipItem { Kind = ClipKind.Image, ThumbPath = missing, ImagePath = missing });

        Assert.Null(vm.Thumbnail);
        Assert.Null(vm.FullImage);
    }

    [Theory]
    [InlineData(0.5, "たった今")]
    [InlineData(-10.0, "たった今")] // 未来の時刻(時計ずれ等)も「たった今」になる
    [InlineData(5.5, "5分前")]
    [InlineData(59.5, "59分前")]
    [InlineData(210.0, "3時間前")]
    [InlineData(3600.0, "2日前")]
    public void TimeAgo_buckets_elapsed_minutes(double minutesAgo, string expected)
    {
        var vm = new ClipItemViewModel(new ClipItem { CreatedAt = DateTime.Now.AddMinutes(-minutesAgo) });

        Assert.Equal(expected, vm.TimeAgo);
    }

    [Fact]
    public void Pinned_starts_from_item_and_writes_back_to_item()
    {
        var item = new ClipItem { Pinned = true };
        var vm = new ClipItemViewModel(item);

        Assert.True(vm.Pinned);

        vm.Pinned = false;

        Assert.False(item.Pinned);
    }
}
