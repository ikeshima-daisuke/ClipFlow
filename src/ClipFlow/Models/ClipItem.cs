using System;

namespace ClipFlow.Models;

public enum ClipKind
{
    Text,
    Image
}

/// <summary>
/// 履歴1件分のクリップボードデータ。テキストか画像のどちらか。
/// </summary>
public class ClipItem
{
    public long Id { get; set; }
    public ClipKind Kind { get; set; }

    /// <summary>テキスト本文（Kind==Text のとき）。</summary>
    public string? Text { get; set; }

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
}
