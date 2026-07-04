using System;
using System.Collections.Generic;

namespace ClipFlow.Models;

public enum ClipKind
{
    Text,
    Image,
    Files
}

/// <summary>
/// 履歴1件分のクリップボードデータ。テキストか画像のどちらか。
/// </summary>
public class ClipItem
{
    public long Id { get; set; }
    public ClipKind Kind { get; set; }

    /// <summary>テキスト本文（Kind==Text のとき）。Kind==Files のときはファイルパス一覧（改行区切り）。</summary>
    public string? Text { get; set; }

    /// <summary>コピー元が持っていた CF_HTML の生データ（あれば）。書式付き貼り付けに使う。</summary>
    public string? Html { get; set; }

    /// <summary>コピー元が持っていた CF_RTF の生データ（あれば）。書式付き貼り付けに使う。</summary>
    public string? Rtf { get; set; }

    /// <summary>画像本体PNGのパス（Kind==Image のとき）。</summary>
    public string? ImagePath { get; set; }

    /// <summary>サムネイルPNGのパス（Kind==Image のとき）。</summary>
    public string? ThumbPath { get; set; }

    /// <summary>一覧に出す短いプレビュー文字列。</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>重複判定用ハッシュ。</summary>
    public string Hash { get; set; } = string.Empty;

    public bool Pinned { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>ファイルパス一覧を Text 欄の保存形式（改行区切り）に結合する。</summary>
    public static string JoinFilePaths(IEnumerable<string> paths) => string.Join('\n', paths);

    /// <summary>Text 欄の保存形式（改行区切り）からファイルパス一覧を復元する。</summary>
    public static string[] SplitFilePaths(string? raw)
        => string.IsNullOrEmpty(raw) ? Array.Empty<string>() : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
