using System;
using System.Diagnostics;
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

    /// <summary>
    /// カーソルがウィンドウ本体から出てから実際に隠すまでの猶予。プレビューポップアップは
    /// 本体の外（右側）に別ウィンドウとして出るため、その間の隙間を横切っただけの一瞬の
    /// MouseLeave で誤って隠れないよう、少し待ってから最終判定する。
    /// </summary>
    private static readonly TimeSpan LeaveHideDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// 取り残し（フォーカスを失ったのに残っている本体／本体が隠れたのに残っているプレビュー）を
    /// 見張る間隔。イベントの取りこぼしに備えた保険なので、間隔は粗くてよい。
    /// </summary>
    private static readonly TimeSpan DismissGuardInterval = TimeSpan.FromMilliseconds(300);

    private MainViewModel _vm = null!;
    private Action _hide = null!;

    private readonly PreviewTargetResolver<ClipItemViewModel> _preview = new();
    private readonly DispatcherTimer _hoverTimer = new() { Interval = HoverDelay };
    private readonly DispatcherTimer _leaveHideTimer = new() { Interval = LeaveHideDelay };
    private readonly DispatcherTimer _dismissGuard = new() { Interval = DismissGuardInterval };
    private readonly Stopwatch _shownWatch = new();
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

        _leaveHideTimer.Tick += (_, _) =>
        {
            if (!IsVisible)
            {
                _leaveHideTimer.Stop();
                return;
            }

            // Enter/Leaveのペアだけを信用しない：SearchBoxの既定コンテキストメニュー等、
            // 許可リストに無い一時ポップアップが開くと、PreviewPopupと同じ仕組み（ポップアップに
            // よるオクルージョン）でウィンドウのMouseLeaveが誤発火することがある。実際のカーソル
            // 座標で裏取りしてから隠す。
            // 空振り（カーソルは実は内側だった）ならタイマーは止めず見張り続ける。止めてしまうと、
            // 一時ポップアップにマウスキャプチャを奪われている間にカーソルが外へ出た場合、
            // 二度目のMouseLeaveが来ないまま判定の機会が永久に失われる。
            if (IsCursorInsideWindowOrPreview())
                return;

            _leaveHideTimer.Stop();
            _hide();
        };

        _dismissGuard.Tick += (_, _) => RunDismissGuard();

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                // 前面化の猶予は「実際に表示された時点」から測る
                _shownWatch.Restart();
            }
            else
            {
                _leaveHideTimer.Stop();
                ResetPreview();
            }
            UpdateDismissGuard();
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

    /// <summary>
    /// カーソルがウィンドウ本体（またはプレビューポップアップ）から完全に出たら、
    /// フォーカスを失っていなくても速やかに隠す。クリックせず視線を外しただけで
    /// Topmost のポップアップが画面に居座り続けるのを防ぐ。
    /// </summary>
    private void Window_MouseLeave(object sender, MouseEventArgs e) => ScheduleHide();

    private void Window_MouseEnter(object sender, MouseEventArgs e) => CancelScheduledHide();

    private void PreviewPopup_MouseEnter(object sender, MouseEventArgs e) => CancelScheduledHide();

    private void PreviewPopup_MouseLeave(object sender, MouseEventArgs e) => ScheduleHide();

    private void ScheduleHide()
    {
        _leaveHideTimer.Stop();
        _leaveHideTimer.Start();
    }

    private void CancelScheduledHide() => _leaveHideTimer.Stop();

    /// <summary>
    /// 本体を隠す直前に呼ぶ。プレビューは本体とは別の最上位ウィンドウなので、
    /// 本体の非表示に紛れて閉じ忘れると単独で画面に残る。<c>Hide()</c> より先に閉じる。
    /// </summary>
    public void PrepareForHide() => ResetPreview();

    /// <summary>取り残しの見張りは、見張る対象（本体かプレビュー）が出ている間だけ回す。</summary>
    private void UpdateDismissGuard()
    {
        if (IsVisible || PreviewPopup.IsOpen)
            _dismissGuard.Start();
        else
            _dismissGuard.Stop();
    }

    private void PreviewPopup_OpenedOrClosed(object sender, EventArgs e) => UpdateDismissGuard();

    /// <summary>
    /// イベントの取りこぼしで画面に取り残されたポップアップを回収する。
    /// 判定そのものは <see cref="PopupDismissPolicy"/>（純粋ロジック）に置いてテストで固定している。
    /// </summary>
    private void RunDismissGuard()
    {
        switch (PopupDismissPolicy.Decide(IsVisible, PreviewPopup.IsOpen, ForegroundIsOurs(), _shownWatch.Elapsed))
        {
            case DismissAction.HideWindow:
                _hide();
                break;

            case DismissAction.ClosePreview:
                ResetPreview();
                break;
        }

        UpdateDismissGuard();
    }

    /// <summary>
    /// OSレベルの前面ウィンドウが自プロセスのものか。トレイメニューやショートカット設定
    /// ダイアログも自プロセスなので、それらに前面を渡している間は「自分のまま」と見なす。
    /// 前面ウィンドウが取れない一瞬（デスクトップ切替中など）は誤爆を避けて true を返す。
    /// </summary>
    private static bool ForegroundIsOurs()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return true;

        NativeMethods.GetWindowThreadProcessId(foreground, out uint pid);
        return pid == 0 || pid == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// カーソルが実際にウィンドウ本体またはプレビューポップアップの矩形内にあるか。
    /// 矩形に入っていなくても、カーソル直下のウィンドウが自プロセスのものなら内側と見なす
    /// （SearchBoxの右クリックメニュー等、自前のメニューは本体の矩形の外へ張り出すことがあり、
    /// そこへカーソルを動かした瞬間に隠れてしまうのを防ぐ）。
    /// </summary>
    private bool IsCursorInsideWindowOrPreview()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
            return false;

        if (IsPointInWindow(new WindowInteropHelper(this).Handle, pt))
            return true;

        if (PreviewPopup.IsOpen
            && PresentationSource.FromVisual(PreviewPopup.Child) is HwndSource popupSource
            && IsPointInWindow(popupSource.Handle, pt))
        {
            return true;
        }

        return IsOwnWindow(NativeMethods.WindowFromPoint(pt));
    }

    /// <summary>指定ウィンドウが自プロセスのものか（自前のメニュー・ポップアップの判定用）。</summary>
    private static bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private static bool IsPointInWindow(IntPtr hwnd, NativeMethods.POINT pt)
        => hwnd != IntPtr.Zero
            && NativeMethods.GetWindowRect(hwnd, out var rect)
            && pt.X >= rect.left && pt.X < rect.right
            && pt.Y >= rect.top && pt.Y < rect.bottom;

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
        // 本体が隠れている間は絶対に開かない。プレビューは別の最上位ウィンドウなので、
        // ここで開くと本体なしのまま画面に居座る（ホバー待ちタイマーの遅延Tickや、
        // 履歴の再読み込みに伴う SelectionChanged が非表示中に届くことがある）。
        if (!IsVisible)
        {
            PreviewPopup.IsOpen = false;
            return;
        }

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
            // 待っている間に対象が変わった／閉じられていたら、そちらの処理に任せる
            if (IsVisible && ReferenceEquals(_preview.Target, vm) && TryGetRowOffset(vm, out double late))
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
