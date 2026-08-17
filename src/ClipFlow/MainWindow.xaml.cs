using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipFlow.Services;
using ClipFlow.ViewModels;
using Wpf.Ui.Controls;

namespace ClipFlow;

public partial class MainWindow : FluentWindow
{
    /// <summary>一覧を横切っただけでプレビューが点滅しないよう、ホバー表示に挟む待ち時間。</summary>
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(250);

    private MainViewModel _vm = null!;
    private Action _hide = null!;

    private readonly PreviewTargetResolver<ClipItemViewModel> _preview = new();
    private readonly DispatcherTimer _hoverTimer = new() { Interval = HoverDelay };
    private Point _lastMousePosition = new(double.NaN, double.NaN);

    public MainWindow()
    {
        InitializeComponent();

        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_preview.CommitPendingHover())
                RefreshPreview();
        };

        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                ResetPreview();
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

        // 検索・種別フィルターをリセットして毎回フレッシュに
        _vm.SearchText = string.Empty;
        _vm.FilterKind = null;
        ResetPreview();

        // 次のフェードインが必ずゼロから始まるよう、表示前にリセットしておく
        RootGrid.BeginAnimation(OpacityProperty, null);
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RootGrid.Opacity = 0;
        RootScale.ScaleX = RootScale.ScaleY = 0.97;

        Show();

        // Activate() だけでは前面化に失敗することがある（低レベルフック経由の起動は
        // WM_HOTKEY と違い OS の SetForegroundWindow 特例を受けられないため）。
        // 見た目は出ていてもキー入力が直前の前面アプリへ流れたままになり、矢印キー等が効かなくなる。
        ForegroundActivator.Force(new WindowInteropHelper(this).Handle);
        Activate();
        Topmost = true;

        // フォーカス・選択はアニメーションを待たず即座に確定させ、矢印キー操作をすぐ受け付ける
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        if (HistoryList.Items.Count > 0)
            HistoryList.SelectedIndex = 0;

        PlayShowAnimation();
    }

    /// <summary>表示直後に重ねる軽いフェード＋拡大アニメーション（入力はブロックしない）。</summary>
    private void PlayShowAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(120);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        RootGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });
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
                // Enter = 元の場所へ貼り付け（既定はプレーンテキスト。クリップボードにも残る）
                // Ctrl+Shift+Enter = 保存されている書式（HTML/RTF）を保持して貼り付け（一覧の「Aa」ボタンと同じ）
                var pasteTarget = SelectedOrFirst();
                if (pasteTarget != null)
                {
                    if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                        _vm.PasteWithFormattingCommand.Execute(pasteTarget);
                    else
                        _vm.PasteCommand.Execute(pasteTarget);
                }
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
        // 選択（＝プレビュー位置の再計算）より先にスクロールしておく。
        // SelectionChanged は同期発火するので、逆順だとコンテナ未生成のまま位置を測ることになる。
        HistoryList.ScrollIntoView(HistoryList.Items[next]);
        HistoryList.SelectedIndex = next;
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

    private void HistoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _hoverTimer.Stop();
        if (_preview.Select(HistoryList.SelectedItem as ClipItemViewModel))
            RefreshPreview();
    }

    /// <summary>
    /// マウスが乗っている項目をプレビューする。MouseEnter ではなく MouseMove で拾うのは、
    /// 矢印キーでのスクロールで項目がカーソル下へ流れてきただけの場合を座標で弾くため
    /// （カーソルが実際に動いていなければキーボード側の選択を優先する）。
    /// </summary>
    private void HistoryList_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(HistoryList);
        if (pos == _lastMousePosition) return;
        _lastMousePosition = pos;

        var container = HistoryList.ContainerFromElement(e.OriginalSource as DependencyObject)
            as System.Windows.Controls.ListBoxItem;

        if (container?.DataContext is not ClipItemViewModel vm)
        {
            CancelHover(); // 項目のない余白の上
            return;
        }

        if (_preview.HoverEnter(vm))
            RefreshPreview();

        if (_preview.HasPendingHover)
        {
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }
    }

    private void HistoryList_MouseLeave(object sender, MouseEventArgs e) => CancelHover();

    /// <summary>ホバーを取り消して、選択中の項目のプレビューへ戻す。</summary>
    private void CancelHover()
    {
        _hoverTimer.Stop();
        if (_preview.HoverLeave())
            RefreshPreview();
    }

    private void ResetPreview()
    {
        _hoverTimer.Stop();
        _preview.Reset();
        _lastMousePosition = new Point(double.NaN, double.NaN);
        PreviewPopup.IsOpen = false;
    }

    /// <summary>プレビュー対象の全文（テキスト）または原寸画像をポップアップに表示する。</summary>
    private void RefreshPreview()
    {
        if (_preview.Target is not { } vm)
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
            PreviewTextScroll.ScrollToTop(); // 前の項目のスクロール位置を持ち越さない
        }

        UpdatePreviewOffset(vm);
        PreviewPopup.IsOpen = true;
    }

    /// <summary>
    /// ポップアップの縦位置を対象の行に合わせる（見ている行の真横に出す）。
    /// スクロール直後はコンテナがまだ生成されていないことがあるので、
    /// その場合はレイアウト確定後にもう一度合わせ直す（先頭に飛んだままにしない）。
    /// </summary>
    private void UpdatePreviewOffset(ClipItemViewModel vm)
    {
        if (TryGetRowOffset(vm, out double offset))
        {
            PreviewPopup.VerticalOffset = offset;
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            // 待っている間に対象が変わっていたら、そちらの処理に任せる
            if (ReferenceEquals(_preview.Target, vm) && TryGetRowOffset(vm, out double late))
                PreviewPopup.VerticalOffset = late;
        });
    }

    /// <summary>対象の行の、一覧内での上端位置。コンテナが未生成／非表示なら false。</summary>
    private bool TryGetRowOffset(ClipItemViewModel vm, out double offset)
    {
        offset = 0;

        if (HistoryList.ItemContainerGenerator.ContainerFromItem(vm) is not FrameworkElement container
            || !container.IsVisible
            || !container.IsDescendantOf(HistoryList))
        {
            return false;
        }

        offset = Math.Max(0, container.TranslatePoint(new Point(0, 0), HistoryList).Y);
        return true;
    }
}
