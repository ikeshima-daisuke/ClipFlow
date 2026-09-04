using System;

namespace ClipFlow.Services;

/// <summary>
/// Windows のフォアグラウンド奪取制限を <c>AttachThreadInput</c> で回避し、指定ウィンドウを
/// 確実に前面へ持っていく（Win+V / Ditto と同方式）。<see cref="PasteService"/> の貼り付け先復帰と
/// <c>MainWindow</c> の表示の両方で使う。
///
/// 特に <c>WM_HOTKEY</c>（<see cref="GlobalHotkey"/>）経由の起動は OS が SetForegroundWindow を
/// 特例で許可してくれるが、<see cref="ModifierTapHotkey"/> の低レベルキーボードフック経由の起動は
/// この特例を受けられず、素の <c>Activate()</c> だけでは前面化に失敗することがある
/// （ウィンドウは見えていてもキー入力が直前の前面アプリへ流れたままになる）。
/// </summary>
internal static class ForegroundActivator
{
    public static void Force(IntPtr target)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == target) return;

        uint targetThread = NativeMethods.GetWindowThreadProcessId(target, out _);
        uint foreThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();

        // 自分自身への接続は必ず失敗する（MainWindow を前面化する経路は呼び出し元＝対象が同じ
        // UIスレッドなので、条件を付けずに呼ぶと無意味な失敗になる）。
        bool attachSelf = thisThread != targetThread;

        if (foreThread != targetThread)
            NativeMethods.AttachThreadInput(foreThread, targetThread, true);
        if (attachSelf)
            NativeMethods.AttachThreadInput(thisThread, targetThread, true);

        NativeMethods.SetForegroundWindow(target);
        NativeMethods.BringWindowToTop(target);

        if (attachSelf)
            NativeMethods.AttachThreadInput(thisThread, targetThread, false);
        if (foreThread != targetThread)
            NativeMethods.AttachThreadInput(foreThread, targetThread, false);
    }
}
