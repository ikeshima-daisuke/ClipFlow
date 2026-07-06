using System;
using System.Windows;
using System.Windows.Input;
using ClipFlow.Services;
using Wpf.Ui.Controls;

namespace ClipFlow;

/// <summary>
/// グローバルホットキーをキー入力でキャプチャして変更するダイアログ。
/// 実際の登録可否（他アプリとの競合）は保存時に <see cref="_tryApply"/> で判定する。
/// </summary>
public partial class HotkeyDialog : FluentWindow
{
    private readonly Func<uint, uint, bool> _tryApply;
    private ModifierKeys _pendingModifiers = ModifierKeys.None;
    private Key _pendingKey = Key.None;

    public uint ResultModifiers { get; private set; }
    public uint ResultVirtualKey { get; private set; }

    /// <param name="currentModifiers">現在のホットキーの修飾キー（表示用）。</param>
    /// <param name="currentVirtualKey">現在のホットキーの仮想キー（表示用）。</param>
    /// <param name="tryApply">実際に登録を試みるコールバック。成功したときだけダイアログを閉じる。</param>
    public HotkeyDialog(uint currentModifiers, uint currentVirtualKey, Func<uint, uint, bool> tryApply)
    {
        InitializeComponent();
        _tryApply = tryApply;
        ComboText.Text = HotkeyFormat.Format(currentModifiers, currentVirtualKey);
        Loaded += (_, _) => CaptureBox.Focus();
    }

    private void CaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnly(key))
            return; // 修飾キー単体では確定しない（Ctrlだけ押した瞬間など）

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            ShowError("Ctrl・Alt・Shift・Win のいずれかと組み合わせてください。");
            return;
        }

        _pendingModifiers = mods;
        _pendingKey = key;
        ComboText.Text = HotkeyFormat.Format(HotkeyFormat.ToWin32Modifiers(mods), HotkeyFormat.ToVirtualKey(key));
        HideError();
        SaveButton.IsEnabled = true;
    }

    private static bool IsModifierOnly(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin or
        Key.System;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    private void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        _pendingModifiers = ModifierKeys.Control | ModifierKeys.Shift;
        _pendingKey = Key.V;
        ComboText.Text = HotkeyFormat.Format(
            HotkeyFormat.ToWin32Modifiers(_pendingModifiers), HotkeyFormat.ToVirtualKey(_pendingKey));
        HideError();
        SaveButton.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var modifiers = HotkeyFormat.ToWin32Modifiers(_pendingModifiers);
        var virtualKey = HotkeyFormat.ToVirtualKey(_pendingKey);

        if (!_tryApply(modifiers, virtualKey))
        {
            ShowError("他のアプリが使用中のため登録できませんでした。別の組み合わせを試してください。");
            return;
        }

        ResultModifiers = modifiers;
        ResultVirtualKey = virtualKey;
        DialogResult = true;
        Close();
    }
}
