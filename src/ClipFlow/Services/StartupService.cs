using System;
using Microsoft.Win32;

namespace ClipFlow.Services;

/// <summary>
/// Windows ログイン時の自動起動を HKCU の Run キーで ON/OFF する。
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipFlow";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{exe}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }

    /// <summary>
    /// 登録済みパスがexeの現在地と食い違っていたら（フォルダ移動などで）現在のパスへ上書きする。
    /// 未登録（スタートアップ無効）の場合は何もしない。
    /// </summary>
    public static void SyncPathIfEnabled()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not string registered) return;

        if (registered != $"\"{exe}\"")
        {
            key.SetValue(ValueName, $"\"{exe}\"");
        }
    }
}
