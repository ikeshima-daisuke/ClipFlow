using ClipFlow.Models;

namespace ClipFlow.Tests;

/// <summary>
/// ファイルコピー（Kind==Files）は Text 欄に改行区切りでパス一覧を保存する。
/// 結合/分解が対称であることと、余分な空行を無視することを固定する。
/// </summary>
public class ClipItemTests
{
    [Fact]
    public void JoinFilePaths_then_SplitFilePaths_round_trips()
    {
        var paths = new[] { @"C:\a\file1.txt", @"C:\b\file2.png" };

        var joined = ClipItem.JoinFilePaths(paths);
        var split = ClipItem.SplitFilePaths(joined);

        Assert.Equal(paths, split);
    }

    [Fact]
    public void SplitFilePaths_ignores_empty_lines()
    {
        var split = ClipItem.SplitFilePaths("C:\\a\\file1.txt\n\nC:\\b\\file2.png\n");

        Assert.Equal(new[] { @"C:\a\file1.txt", @"C:\b\file2.png" }, split);
    }

    [Fact]
    public void SplitFilePaths_of_null_or_empty_returns_empty_array()
    {
        Assert.Empty(ClipItem.SplitFilePaths(null));
        Assert.Empty(ClipItem.SplitFilePaths(string.Empty));
    }
}
