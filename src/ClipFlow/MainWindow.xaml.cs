using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ClipFlow.Services;
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
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                PreviewPopup.IsOpen = false;
        };
    }

    public void Initialize(MainViewModel vm, Action hide)
    {
        _vm = vm;
        _hide = hide;
        DataContext = vm;
    }

    /// <summary>最後にアクティブだった画面の中央に出して前面化、検索ボックスにフォーカス。</summary>
    public void ShowAndActivate(IntPtr activeWindow = default)
    {
        PositionOnActiveScreen(activeWindow);

        // 検索状態をリセットして毎回フレッシュに
        _vm.SearchText = string.Empty;

        Show();
        Activate();
        Topmost = true;

        SearchBox.Focus();
        if (HistoryList.Items.Count > 0)
            HistoryList.SelectedIndex = 0;
    }

    /// <summary>
    /// 最後にアクティブだったウィンドウのモニター（無ければカーソル位置のモニター、
    /// それも無ければプライマリ）の作業領域中央へウィンドウを配置する。
    /// </summary>
    private void PositionOnActiveScreen(IntPtr activeWindow)
    {
        IntPtr mon = activeWindow != IntPtr.Zero
            ? NativeMethods.MonitorFromWindow(activeWindow, NativeMethods.MONITOR_DEFAULTTONEAREST)
            : CursorMonitor();

        if (mon == IntPtr.Zero)
        {
            CenterOnPrimary();
            return;
        }

        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(mon, ref mi))
        {
            CenterOnPrimary();
            return;
        }

        // 物理ピクセル → DIP（モニターのDPIスケールで割る。全画面同一スケールなら正確）
        double scale = MonitorScale(mon);
        var work = mi.rcWork;
        double leftDip = work.left / scale;
        double topDip = work.top / scale;
        double widthDip = (work.right - work.left) / scale;
        double heightDip = (work.bottom - work.top) / scale;

        Left = leftDip + (widthDip - Width) / 2;
        Top = topDip + (heightDip - Height) / 2;
    }

    private void CenterOnPrimary()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    private static IntPtr CursorMonitor()
        => NativeMethods.GetCursorPos(out var p)
            ? NativeMethods.MonitorFromPoint(p, NativeMethods.MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;

    private static double MonitorScale(IntPtr mon)
        => NativeMethods.GetDpiForMonitor(mon, 0, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;

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

    /// <summary>選択した履歴の全文（テキスト）または原寸画像をポップアップに表示する。</summary>
    private void HistoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not ClipItemViewModel vm)
        {
            PreviewPopup.IsOpen = false;
            return;
        }

        if (vm.IsImage)
        {
            PreviewImage.Source = vm.FullImage;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewTextScroll.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewText.Text = vm.FullText;
            PreviewTextScroll.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
        }

        PreviewPopup.IsOpen = true;
    }
}
