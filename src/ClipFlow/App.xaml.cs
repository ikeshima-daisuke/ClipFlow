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
    private AppSettings _settings = null!;
    private System.Windows.Controls.ContextMenu _trayMenu = null!;
    private System.Windows.Controls.MenuItem _openItem = null!;
    private System.Windows.Controls.MenuItem _pauseItem = null!;

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

        // フォルダ移動などでスタートアップ登録パスが古くなっていたら現在地に合わせて修復
        StartupService.SyncPathIfEnabled();

        // ウィンドウは作るが表示しない。HWND だけ確保してリスナーを張る。
        _window = new MainWindow();

        // 最後に手動リサイズしたサイズを復元（作業領域を超える値は画面内に収まるよう抑える）
        if (_settings.WindowWidth is double savedWidth && savedWidth > 0)
            _window.Width = Math.Min(savedWidth, SystemParameters.WorkArea.Width);
        if (_settings.WindowHeight is double savedHeight && savedHeight > 0)
            _window.Height = Math.Min(savedHeight, SystemParameters.WorkArea.Height);

        var handle = new WindowInteropHelper(_window).EnsureHandle();

        _monitor = new ClipboardMonitor(handle);
        _paste = new PasteService(_monitor);
        _vm = new MainViewModel(_store, _paste, HideWindow);
        _window.Initialize(_vm, HideWindow);

        _monitor.Captured += item => _vm.OnCaptured(item);

        // ユーザー設定のホットキー（既定 Ctrl+Shift+V）
        _hotkey = new GlobalHotkey(handle, _settings.HotkeyModifiers, _settings.HotkeyVirtualKey);
        _hotkey.Pressed += ToggleWindow;

        SetupTray();
        RefreshHotkeyDisplays();

        if (!_hotkey.IsRegistered)
            _tray.ShowBalloonTip("ClipFlow",
                $"ホットキー {HotkeyFormat.Format(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey)} を登録できませんでした" +
                "（他アプリが使用中）。トレイアイコンから開けます。",
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

        // StaysOpen=true: メニュー項目のクリックでは閉じず、メニュー外をクリックしたときだけ閉じる
        // （WPFのContextMenuが持つ標準機能。Popupの再オープンのような自前実装は不要）。
        var menu = new System.Windows.Controls.ContextMenu { StaysOpen = true };
        _trayMenu = menu;

        _openItem = new System.Windows.Controls.MenuItem();
        _openItem.Click += (_, _) => ToggleWindow();
        menu.Items.Add(_openItem);

        _pauseItem = new System.Windows.Controls.MenuItem
        {
            Header = "記録を一時停止",
            IsCheckable = true,
            IsChecked = false,
        };
        _pauseItem.Click += (_, _) =>
        {
            _monitor.IsPaused = _pauseItem.IsChecked;
            RefreshHotkeyDisplays();
        };
        menu.Items.Add(_pauseItem);

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

        var hotkeyItem = new System.Windows.Controls.MenuItem { Header = "ショートカットを変更..." };
        hotkeyItem.Click += (_, _) => ShowHotkeyDialog();
        menu.Items.Add(hotkeyItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        _tray.ContextMenu = menu;
    }

    /// <summary>トレイのツールチップ・「履歴を開く」項目のラベルを、現在のホットキー表示に合わせて更新する。</summary>
    private void RefreshHotkeyDisplays()
    {
        var label = HotkeyFormat.Format(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey);
        _openItem.Header = $"履歴を開く ({label})";
        _tray.ToolTipText = _monitor.IsPaused
            ? "ClipFlow — 記録一時停止中"
            : $"ClipFlow — クリップボード履歴 ({label})";
    }

    /// <summary>ショートカットキーのキャプチャダイアログを表示し、確定すれば設定を保存する。</summary>
    private void ShowHotkeyDialog()
    {
        var dialog = new HotkeyDialog(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey,
            (mods, vk) => _hotkey.Rebind(mods, vk));
        if (dialog.ShowDialog() == true)
        {
            _settings.HotkeyModifiers = dialog.ResultModifiers;
            _settings.HotkeyVirtualKey = dialog.ResultVirtualKey;
            _settings.Save();
            RefreshHotkeyDisplays();
        }
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

    private void HideWindow()
    {
        // ユーザーが手動でリサイズしていれば、次回もそのサイズで開けるように覚えておく
        _settings.WindowWidth = _window.Width;
        _settings.WindowHeight = _window.Height;
        _settings.Save();
        _window.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _monitor?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
