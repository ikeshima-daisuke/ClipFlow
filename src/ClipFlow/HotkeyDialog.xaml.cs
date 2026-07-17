using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipFlow.Services;
using Wpf.Ui.Controls;

namespace ClipFlow;

/// <summary>
/// グローバルホットキーをキー入力でキャプチャして変更するダイアログ。
/// 「組み合わせ」（Ctrl+Shift+V 等）と「連打」（Ctrl連打 等）の2モードを切り替えられる。
/// 実際の登録可否（他アプリとの競合）は保存時に <see cref="_tryApply"/> で判定する。
/// </summary>
public partial class HotkeyDialog : FluentWindow
{
    private const string ComboModeDescription = "履歴ポップアップを呼び出すショートカットキーを入力してください";
    private const string DoubleTapModeDescription = "選んだキーを素早く2回押すとポップアップを呼び出します";

    private readonly Func<HotkeySpec, bool> _tryApply;
    private ModifierKeys _pendingModifiers = ModifierKeys.None;
    private Key _pendingKey = Key.None;
    private uint _pendingDoubleTapModifier = NativeMethods.MOD_CONTROL;

    public HotkeySpec Result { get; private set; }

    /// <param name="current">現在のホットキー設定（表示用）。</param>
    /// <param name="tryApply">実際に登録を試みるコールバック。成功したときだけダイアログを閉じる。</param>
    public HotkeyDialog(HotkeySpec current, Func<HotkeySpec, bool> tryApply)
    {
        InitializeComponent();
        _tryApply = tryApply;

        if (current.IsDoubleTap)
        {
            _pendingDoubleTapModifier = current.Modifiers;
            DoubleTapModeRadio.IsChecked = true;
            SelectDoubleTapCombo(current.Modifiers);
        }
        else
        {
            ComboText.Text = HotkeyFormat.Format(current.Modifiers, current.VirtualKey);
        }

        Loaded += (_, _) => CaptureBox.Focus();
    }

    private void ComboModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (CaptureBox == null) return; // InitializeComponent中の既定値設定による発火を無視
        CaptureBox.Visibility = Visibility.Visible;
        DoubleTapCombo.Visibility = Visibility.Collapsed;
        DescriptionText.Text = ComboModeDescription;
        HideError();
        SaveButton.IsEnabled = _pendingKey != Key.None;
    }

    private void DoubleTapModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (CaptureBox == null) return;
        CaptureBox.Visibility = Visibility.Collapsed;
        DoubleTapCombo.Visibility = Visibility.Visible;
        DescriptionText.Text = DoubleTapModeDescription;
        HideError();
        if (DoubleTapCombo.SelectedIndex < 0)
            DoubleTapCombo.SelectedIndex = 0;
        SaveButton.IsEnabled = true;
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

    private void DoubleTapCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DoubleTapCombo.SelectedItem is not ComboBoxItem item) return;

        _pendingDoubleTapModifier = (string)item.Tag switch
        {
            "Control" => NativeMethods.MOD_CONTROL,
            "Shift" => NativeMethods.MOD_SHIFT,
            "Alt" => NativeMethods.MOD_ALT,
            "Win" => NativeMethods.MOD_WIN,
            _ => NativeMethods.MOD_CONTROL,
        };
        HideError();
        SaveButton.IsEnabled = true;
    }

    private void SelectDoubleTapCombo(uint modifierBit)
    {
        var tag = modifierBit switch
        {
            NativeMethods.MOD_CONTROL => "Control",
            NativeMethods.MOD_SHIFT => "Shift",
            NativeMethods.MOD_ALT => "Alt",
            NativeMethods.MOD_WIN => "Win",
            _ => "Control",
        };
        foreach (ComboBoxItem item in DoubleTapCombo.Items)
        {
            if ((string)item.Tag == tag)
            {
                DoubleTapCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    private void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        ComboModeRadio.IsChecked = true;
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
        var spec = DoubleTapModeRadio.IsChecked == true
            ? HotkeySpec.DoubleTap(_pendingDoubleTapModifier)
            : HotkeySpec.Combo(HotkeyFormat.ToWin32Modifiers(_pendingModifiers), HotkeyFormat.ToVirtualKey(_pendingKey));

        if (!_tryApply(spec))
        {
            ShowError(spec.IsDoubleTap
                ? "この設定を登録できませんでした。"
                : "他のアプリが使用中のため登録できませんでした。別の組み合わせを試してください。");
            return;
        }

        Result = spec;
        DialogResult = true;
        Close();
    }
}
