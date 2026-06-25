using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using ClipFlow.Services;
using ClipFlow.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;

namespace ClipFlow;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private HistoryStore _store = null!;
    private ClipboardMonitor _monitor = null!;
    private GlobalHotkey _hotkey = null!;
    private PasteService _paste = null!;
    private MainViewModel _vm = null!;
    private MainWindow _window = null!;
    private TaskbarIcon _tray = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 二重起動防止
        _singleInstance = new Mutex(true, "ClipFlow_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppPaths.EnsureCreated();
        _store = new HistoryStore();

        // ウィンドウは作るが表示しない。HWND だけ確保してリスナーを張る。
        _window = new MainWindow();
        var handle = new WindowInteropHelper(_window).EnsureHandle();

        _monitor = new ClipboardMonitor(handle);
        _paste = new PasteService(_monitor);
        _vm = new MainViewModel(_store, _paste, HideWindow);
        _window.Initialize(_vm, HideWindow);

        _monitor.Captured += item => _vm.OnCaptured(item);

        // 既定ホットキー: Ctrl+Shift+V
        _hotkey = new GlobalHotkey(handle,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, NativeMethods.VK_V);
        _hotkey.Pressed += ToggleWindow;

        SetupTray();

        if (!_hotkey.IsRegistered)
            _tray.ShowBalloonTip("ClipFlow",
                "ホットキー Ctrl+Shift+V を登録できませんでした（他アプリが使用中）。トレイアイコンから開けます。",
                BalloonIcon.Warning);
    }

    private void SetupTray()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "ClipFlow — クリップボード履歴 (Ctrl+Shift+V)",
            Icon = LoadTrayIcon(),
        };
        _tray.TrayMouseDoubleClick += (_, _) => ToggleWindow();

        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "履歴を開く (Ctrl+Shift+V)" };
        openItem.Click += (_, _) => ToggleWindow();
        menu.Items.Add(openItem);

        var clearItem = new System.Windows.Controls.MenuItem { Header = "履歴をクリア（ピン留め以外）" };
        clearItem.Click += (_, _) => _vm.ClearAllCommand.Execute(null);
        menu.Items.Add(clearItem);

        var startupItem = new System.Windows.Controls.MenuItem
        {
            Header = "Windows起動時に実行",
            IsCheckable = true,
            IsChecked = StartupService.IsEnabled(),
        };
        startupItem.Click += (_, _) => StartupService.SetEnabled(startupItem.IsChecked);
        menu.Items.Add(startupItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        _tray.ContextMenu = menu;
    }

    /// <summary>埋め込みリソースの .ico からトレイ用アイコンを読み込む。</summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/clipflow.ico", UriKind.Absolute);
            var info = GetResourceStream(uri);
            if (info != null)
                return new System.Drawing.Icon(info.Stream);
        }
        catch { /* 失敗時は既定アイコンへフォールバック */ }
        return System.Drawing.SystemIcons.Application;
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible)
            HideWindow();
        else
            ShowWindow();
    }

    private void ShowWindow()
    {
        _paste.CaptureForeground();   // 表示前に元の前面ウィンドウを記憶
        _window.ShowAndActivate(_paste.PreviousWindow);
    }

    private void HideWindow() => _window.Hide();

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _monitor?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
