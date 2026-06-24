using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ClipFlow.ViewModels;
using Wpf.Ui.Controls;

namespace ClipFlow;

public partial class MainWindow : FluentWindow
{
    private MainViewModel _vm = null!;
    private Action _hide = null!;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void Initialize(MainViewModel vm, Action hide)
    {
        _vm = vm;
        _hide = hide;
        DataContext = vm;
    }

    /// <summary>作業領域の中央に出して前面化、検索ボックスにフォーカス。</summary>
    public void ShowAndActivate()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;

        // 検索状態をリセットして毎回フレッシュに
        _vm.SearchText = string.Empty;

        Show();
        Activate();
        Topmost = true;

        SearchBox.Focus();
        if (HistoryList.Items.Count > 0)
            HistoryList.SelectedIndex = 0;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // フォーカスが外れたら隠す（ペースト時は先に Hide 済み）
        if (IsVisible)
            _hide();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        switch (e.Key)
        {
            case Key.Escape:
                _hide();
                e.Handled = true;
                break;

            case Key.Enter:
                // Enter = 元の場所へ貼り付け（クリップボードにも残る）
                var pasteTarget = SelectedOrFirst();
                if (pasteTarget != null)
                    _vm.PasteCommand.Execute(pasteTarget);
                e.Handled = true;
                break;

            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                // Ctrl+C = 貼り付けずにクリップボードへコピーするだけ
                var copyTarget = SelectedOrFirst();
                if (copyTarget != null)
                    _vm.CopyCommand.Execute(copyTarget);
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private ClipItemViewModel? SelectedOrFirst()
        => HistoryList.SelectedItem as ClipItemViewModel ?? _vm.Items.FirstOrDefault();

    private void MoveSelection(int delta)
    {
        if (HistoryList.Items.Count == 0) return;
        // 未選択なら先頭から
        int current = HistoryList.SelectedIndex < 0 ? -1 : HistoryList.SelectedIndex;
        int next = Math.Clamp(current + delta, 0, HistoryList.Items.Count - 1);
        HistoryList.SelectedIndex = next;
        HistoryList.ScrollIntoView(HistoryList.SelectedItem);
    }

    /// <summary>検索で絞り込んだら先頭を選択状態にして、Enter ですぐ貼れるようにする。</summary>
    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (HistoryList.Items.Count > 0)
        {
            HistoryList.SelectedIndex = 0;
            HistoryList.ScrollIntoView(HistoryList.SelectedItem);
        }
    }
}
