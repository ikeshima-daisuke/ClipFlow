using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace ClipFlow.Services;

/// <summary>
/// 修飾キー単体の素早い2回押し（例: Ctrl連打）を検出するグローバルトリガー。
/// RegisterHotKey は修飾キー単体を扱えないため、WH_KEYBOARD_LL の低レベルキーボードフックで
/// 押下/離上イベントを拾い、実際の判定は <see cref="ModifierTapDetector"/>（テスト可能な純粋クラス）
/// に委譲する。
///
/// フックは専用スレッド（独自メッセージループ）に張る。UIスレッドに張ると、ポップアップ表示や
/// クリップボード取り込みでUIスレッドが詰まった隙にコールバックが LowLevelHooksTimeout（既定300ms）
/// を超え、Windows がフックを無言で外してしまう（アプリ側には一切通知されず、以後ホットキーが
/// 永久に効かなくなる）。同じ理由で、検出時のハンドラ呼び出しもコールバック内では行わず
/// UIスレッドへ非同期に投函する。
/// </summary>
internal sealed class ModifierTapHotkey : IHotkeyTrigger
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(400);

    /// <summary>フックを張り直す間隔。何らかの理由で外されていた場合の自動復帰用。</summary>
    private const uint ReinstallIntervalMs = 60_000;

    /// <summary>検出をUIスレッドへ渡す手段（テストでは同期的に差し替える）。</summary>
    private readonly Action<Action> _dispatch;

    // フック解除までデリゲートをフィールドで保持し続けないと GC に回収され、
    // ネイティブ側からのコールバックが不正なアドレスを呼ぶことになる。
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly Thread? _hookThread;
    private readonly ManualResetEventSlim _ready = new(false);

    private volatile ModifierTapDetector _detector;
    private volatile bool _disposed;
    private volatile uint _hookThreadId;
    private IntPtr _hookHandle;
    private nuint _timerId;

    public event Action? Pressed;

    public ModifierTapHotkey(uint modifierBit, Dispatcher dispatcher)
        : this(modifierBit, action => dispatcher.BeginInvoke(action), installHook: true)
    {
    }

    /// <summary>
    /// テスト用。<paramref name="installHook"/> が false ならフックもスレッドも作らず、
    /// <see cref="ProcessKeyEvent"/> を直接叩いて判定と通知経路だけを検証できる。
    /// </summary>
    internal ModifierTapHotkey(uint modifierBit, Action<Action> dispatch, bool installHook)
    {
        _detector = new ModifierTapDetector(modifierBit, Threshold);
        _dispatch = dispatch;
        _proc = HookCallback;

        if (!installHook)
        {
            _ready.Set();
            return;
        }

        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "ClipFlow hotkey hook",
        };
        _hookThread.Start();

        // タイムアウトしても続行してよい。その場合 _hookHandle は 0 のままなので IsRegistered が
        // false になり、呼び出し側は登録失敗として扱える。
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// フックの設置に成功したか（通常はほぼ常に true）。
    /// これは「SetWindowsHookEx に成功し、以後こちらから外していない」という意味でしかない点に注意。
    /// Windows がフックを無言で外した状態は検知できず、次の張り直し（最大 <see cref="ReinstallIntervalMs"/>）
    /// までは true のままになる。
    /// </summary>
    public bool IsRegistered => Volatile.Read(ref _hookHandle) != IntPtr.Zero;

    /// <summary>監視対象の修飾キーを変更する（フックは張り直さず判定器だけ差し替える）。</summary>
    public void Rebind(uint modifierBit)
    {
        _detector = new ModifierTapDetector(modifierBit, Threshold);
    }

    /// <summary>
    /// キーイベント1件を判定し、連打が成立したらハンドラをUIスレッドへ投函する。
    /// フックのコールバックから呼ばれるため、ここで重い処理を絶対にしないこと。
    /// </summary>
    internal void ProcessKeyEvent(ushort virtualKeyCode, bool isDown, DateTime nowUtc)
    {
        // 破棄済みインスタンスのフックが万一残っていても、二重にポップアップを開かせない。
        if (_disposed) return;

        if (_detector.Feed(virtualKeyCode, isDown, nowUtc))
            _dispatch(() => Pressed?.Invoke());
    }

    private void HookThreadMain()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();

        // コンストラクタの _ready 待ちがタイムアウトした直後に Dispose された場合、ここで降りないと
        // 誰も参照していないフックを張ったままプロセス終了まで残ってしまう。
        if (_disposed)
        {
            _ready.Set();
            return;
        }

        try
        {
            Install();
            _ready.Set();

            // hWnd を渡さないタイマーなので WM_TIMER はこのスレッドのキューに直接届く。
            _timerId = NativeMethods.SetTimer(IntPtr.Zero, 0, ReinstallIntervalMs, IntPtr.Zero);

            MessageLoop();
        }
        catch
        {
            // このスレッドで例外を素通しするとプロセスごと落ちる（ネイティブ側からのコールバック中に
            // 巻き込まれるとなお悪い）。ホットキーだけ諦めて常駐は続ける。
        }
        finally
        {
            if (_timerId != 0)
                NativeMethods.KillTimer(IntPtr.Zero, _timerId);
            Uninstall();
            _ready.Set();
        }
    }

    private void MessageLoop()
    {
        while (true)
        {
            int result = NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0);

            if (result == 0) return;   // WM_QUIT（Dispose からの停止要求）

            if (result == -1)
            {
                // GetMessage のエラー。ここで抜けるとホットキーが無言で死ぬので、
                // 間を置いてループを続ける（張り直しのタイマーも生かしたままにする）。
                Thread.Sleep(100);
                continue;
            }

            if (msg.message == NativeMethods.WM_TIMER)
            {
                if (_disposed) return;
                Reinstall();
                continue;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }
    }

    private void Install()
    {
        var handle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        Volatile.Write(ref _hookHandle, handle);
    }

    private void Uninstall()
    {
        var handle = Volatile.Read(ref _hookHandle);
        if (handle == IntPtr.Zero) return;

        NativeMethods.UnhookWindowsHookEx(handle);
        Volatile.Write(ref _hookHandle, IntPtr.Zero);
    }

    /// <summary>張り直す。Windows に無言で外されていた場合はここで復活する。</summary>
    private void Reinstall()
    {
        Uninstall();
        Install();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var message = wParam.ToInt32();
                bool isDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
                bool isUp = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    ProcessKeyEvent((ushort)data.vkCode, isDown, DateTime.UtcNow);
                }
            }
        }
        catch
        {
            // 全キーストロークで通る経路。例外をネイティブ側へ伝播させるとプロセスごと落ちるので、
            // 連打の取りこぼしを許容してでも CallNextHookEx まで進める。
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        // 先に立てる。スレッドがまだ起動していなければ、スレッド側がこれを見てフックを張らずに降りる。
        _disposed = true;

        bool ended = true;
        if (_hookThread is not null)
        {
            // フックの解除はフックスレッド自身に任せる（メッセージループを畳めば後始末まで走る）。
            var threadId = _hookThreadId;
            if (threadId != 0)
                NativeMethods.PostThreadMessageW(threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            ended = _hookThread.Join(TimeSpan.FromSeconds(2));
        }

        // 終了しきれていない場合に破棄すると、スレッド側の _ready.Set() が例外になる。
        // 取り残すコストの方が小さいので、確実に終わったときだけ破棄する。
        if (ended)
            _ready.Dispose();
    }
}
