using System;
using System.IO;
using System.Text.Json;

namespace ClipFlow.Services;

/// <summary>ユーザー設定（%APPDATA%\ClipFlow\settings.json）。ClipFlowは外部への通信を一切行わない。</summary>
public sealed class AppSettings
{
    /// <summary>保持件数の上限（ピン留め以外）。0以下または null で無制限。既定は100件。</summary>
    public int? MaxHistoryItems { get; set; } = HistoryStore.DefaultMaxItems;

    /// <summary>履歴ポップアップを呼び出すグローバルホットキーの修飾キー（Win32 MOD_* のビットOR）。既定は Ctrl+Shift。</summary>
    public uint HotkeyModifiers { get; set; } = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT;

    /// <summary>グローバルホットキーの仮想キーコード。既定は 'V'。</summary>
    public uint HotkeyVirtualKey { get; set; } = NativeMethods.VK_V;

    /// <summary>true なら組み合わせではなく修飾キー単体の連打（<see cref="HotkeyDoubleTapModifier"/>）で呼び出す。既定は false。</summary>
    public bool HotkeyIsDoubleTap { get; set; } = false;

    /// <summary>連打モードで監視する修飾キー（単一の MOD_* ビット）。<see cref="HotkeyIsDoubleTap"/> が true のときのみ使う。</summary>
    public uint HotkeyDoubleTapModifier { get; set; } = NativeMethods.MOD_CONTROL;

    /// <summary>履歴ポップアップの最後のサイズ（DIP）。null なら XAML の既定サイズを使う。</summary>
    public double? WindowWidth { get; set; }

    /// <summary>履歴ポップアップの最後のサイズ（DIP）。null なら XAML の既定サイズを使う。</summary>
    public double? WindowHeight { get; set; }

    /// <summary>アクセントカラー。トレイメニュー「アクセントカラー」から変更。既定はティール。</summary>
    public AccentPalette Accent { get; set; } = AccentPalette.Teal;

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
