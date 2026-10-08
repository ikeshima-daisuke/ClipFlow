using System;
using System.IO;

namespace ClipFlow.Services;

/// <summary>
/// 診断用のファイルログ（%APPDATA%\ClipFlow\clipflow.log）。外部には送らない。
/// ログ書込みの失敗でアプリ本体を落とさないよう、例外はすべて握りつぶす。
/// 上限を超えたら clipflow.log.old へ退避して1世代だけ残す。
/// </summary>
internal sealed class AppLog
{
    public const long DefaultMaxBytes = 1024 * 1024;

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _lock = new();

    public AppLog(string path, long maxBytes = DefaultMaxBytes)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public static AppLog Default { get; } = new(AppPaths.LogPath);

    public void Write(string level, string message, Exception? ex = null)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                Rotate();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null) line += Environment.NewLine + ex;
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // ログ失敗は無視する
        }
    }

    private void Rotate()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _maxBytes) return;
        File.Move(_path, _path + ".old", overwrite: true);
    }
}
