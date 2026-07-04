using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
    private AppSettings _settings = null!;
    private string? _pendingUpdateUrl;

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
        _settings = AppSettings.Load();

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

        // オプトイン設定。既定はOFFで、有効化した場合のみ起動時にGitHubへ問い合わせる。
        if (_settings.CheckForUpdates)
            _ = CheckForUpdatesAsync(silent: true);
    }

    private void SetupTray()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "ClipFlow — クリップボード履歴 (Ctrl+Shift+V)",
            Icon = LoadTrayIcon(),
        };
        _tray.TrayMouseDoubleClick += (_, _) => ToggleWindow();
        _tray.TrayBalloonTipClicked += (_, _) =>
        {
            if (_pendingUpdateUrl != null)
                OpenUrl(_pendingUpdateUrl);
        };

        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "履歴を開く (Ctrl+Shift+V)" };
        openItem.Click += (_, _) => ToggleWindow();
        menu.Items.Add(openItem);

        var pauseItem = new System.Windows.Controls.MenuItem
        {
            Header = "記録を一時停止",
            IsCheckable = true,
            IsChecked = false,
        };
        pauseItem.Click += (_, _) =>
        {
            _monitor.IsPaused = pauseItem.IsChecked;
            _tray.ToolTipText = pauseItem.IsChecked
                ? "ClipFlow — 記録一時停止中"
                : "ClipFlow — クリップボード履歴 (Ctrl+Shift+V)";
        };
        menu.Items.Add(pauseItem);

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

        menu.Items.Add(BuildMaxItemsMenu());

        menu.Items.Add(new System.Windows.Controls.Separator());

        var autoCheckItem = new System.Windows.Controls.MenuItem
        {
            Header = "起動時に更新を自動確認する（GitHubへ接続）",
            IsCheckable = true,
            IsChecked = _settings.CheckForUpdates,
        };
        autoCheckItem.Click += (_, _) =>
        {
            _settings.CheckForUpdates = autoCheckItem.IsChecked;
            _settings.Save();
        };
        menu.Items.Add(autoCheckItem);

        var checkNowItem = new System.Windows.Controls.MenuItem { Header = "今すぐ更新を確認" };
        checkNowItem.Click += (_, _) => _ = CheckForUpdatesAsync(silent: false);
        menu.Items.Add(checkNowItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        _tray.ContextMenu = menu;
    }

    /// <summary>保持件数（ピン留め以外）の上限を選ぶサブメニュー。無制限も選べる。</summary>
    private System.Windows.Controls.MenuItem BuildMaxItemsMenu()
    {
        var root = new System.Windows.Controls.MenuItem { Header = "保持件数の上限" };
        var options = new (string Label, int? Value)[]
        {
            ("100件", 100),
            ("500件", 500),
            ("1000件", 1000),
            ("無制限", null),
        };

        var items = new System.Windows.Controls.MenuItem[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            var (label, value) = options[i];
            var mi = new System.Windows.Controls.MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.MaxHistoryItems == value,
            };
            mi.Click += (_, _) =>
            {
                _settings.MaxHistoryItems = value;
                _settings.Save();
                _store.MaxItems = value ?? 0;
                _store.ApplyMaxItems();
                _vm.Reload();
                foreach (var sibling in items)
                    sibling.IsChecked = sibling == mi;
            };
            items[i] = mi;
            root.Items.Add(mi);
        }
        return root;
    }

    /// <summary>
    /// GitHub Releases の最新版を確認する。silent=true（起動時の自動確認）では
    /// 「最新版です」の通知は出さず、更新が見つかったときだけバルーンを出す。
    /// </summary>
    private async Task CheckForUpdatesAsync(bool silent)
    {
        var info = await UpdateChecker.CheckAsync();
        if (info != null)
        {
            _pendingUpdateUrl = info.ReleaseUrl;
            _tray.ShowBalloonTip("ClipFlowの更新があります",
                $"v{info.Version} が公開されています。このバルーンをクリックすると配布ページを開きます。",
                BalloonIcon.Info);
        }
        else if (!silent)
        {
            _tray.ShowBalloonTip("ClipFlow", "現在お使いのバージョンが最新です。", BalloonIcon.Info);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ブラウザ起動失敗は無視 */ }
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
