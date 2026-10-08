using System;
using System.IO;
using ClipFlow.Services;

namespace ClipFlow.Tests;

public class AppLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "clipflow-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string LogPath => Path.Combine(_dir, "clipflow.log");

    [Fact]
    public void Write_AppendsLineWithLevelAndMessage_CreatingDirectory()
    {
        var log = new AppLog(LogPath);
        log.Write("INFO", "起動");
        log.Write("ERROR", "失敗");

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("INFO", lines[0]);
        Assert.EndsWith("起動", lines[0]);
        Assert.Contains("ERROR", lines[1]);
    }

    [Fact]
    public void Write_IncludesExceptionDetails()
    {
        var log = new AppLog(LogPath);
        log.Write("ERROR", "未処理", new InvalidOperationException("boom"));

        var text = File.ReadAllText(LogPath);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void Write_RotatesWhenOverLimit_KeepingOneOldFile()
    {
        var log = new AppLog(LogPath, maxBytes: 200);
        for (int i = 0; i < 20; i++) log.Write("INFO", new string('x', 50));

        Assert.True(File.Exists(LogPath + ".old"));
        Assert.True(new FileInfo(LogPath).Length <= 400);
    }

    [Fact]
    public void Write_NeverThrows_WhenPathIsUnwritable()
    {
        var log = new AppLog(Path.Combine(_dir, "a\0b", "x.log"));
        var ex = Record.Exception(() => log.Write("INFO", "x"));
        Assert.Null(ex);
    }
}
