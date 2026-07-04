using System;
using System.IO;
using System.Text.Json;

namespace ClipFlow.Services;

/// <summary>
/// ユーザー設定（%APPDATA%\ClipFlow\settings.json）。
/// 既定はすべて無効・無通信側（オプトイン）にしておく。
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 起動時にGitHub Releasesへ最新バージョンを問い合わせるか。
    /// 既定は false（ClipFlowは既定でネットワーク通信を一切行わない方針のため）。
    /// トレイメニューからいつでもON/OFFでき、OFFのときも手動の「更新を確認」は都度の明示操作として利用可。
    /// </summary>
    public bool CheckForUpdates { get; set; }

    /// <summary>保持件数の上限（ピン留め以外）。0以下または null で無制限。既定は100件。</summary>
    public int? MaxHistoryItems { get; set; } = HistoryStore.DefaultMaxItems;

    private static string FilePath => Path.Combine(AppPaths.Root, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* 壊れていれば既定値で続行 */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* 保存失敗は無視（次回起動時は既定値に戻るだけ） */ }
    }
}
