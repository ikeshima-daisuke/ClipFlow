using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// GitHub Releases の tag_name（"v1.2.3" / プレリリース付き等）から Version を取り出すパース処理を固定する。
/// ネットワーク呼び出し自体（UpdateChecker.CheckAsync）は副作用が大きいためテストしない。
/// </summary>
public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V2.0.0", 2, 0, 0)]
    [InlineData("v1.2.3-beta.1", 1, 2, 3)]
    public void ParseVersion_extracts_semantic_version(string tag, int major, int minor, int patch)
    {
        var v = UpdateChecker.ParseVersion(tag);

        Assert.NotNull(v);
        Assert.Equal(major, v!.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void ParseVersion_returns_null_for_invalid_input(string? tag)
    {
        Assert.Null(UpdateChecker.ParseVersion(tag));
    }
}
