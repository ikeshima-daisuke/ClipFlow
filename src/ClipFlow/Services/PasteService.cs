using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClipFlow.Models;

namespace ClipFlow.Services;

/// <summary>
/// 「クリックでペースト」を実現する。ポップアップ表示でフォーカスが移るため、
/// 表示直前の前面ウィンドウを記憶 → ペースト時にそれを前面へ戻して Ctrl+V を送る。
/// </summary>
public sealed class PasteService
{
    private readonly ClipboardMonitor _monitor;
    private IntPtr _previousWindow;

    public PasteService(ClipboardMonitor monitor) => _monitor = monitor;

    /// <summary>表示直前に記憶した、元のアクティブウィンドウ。</summary>
    public IntPtr PreviousWindow => _previousWindow;

    /// <summary>ポップアップを出す直前に呼ぶ。元のアクティブウィンドウを覚える。</summary>
    public void CaptureForeground() => _previousWindow = NativeMethods.GetForegroundWindow();

    /// <summary>クリップボードへ書き込むだけ（ペーストはしない）。</summary>
    public bool CopyToClipboard(ClipItem item) => WriteClipboard(item, plainTextOnly: false);

    /// <summary>
    /// クリップボードへ書き込み → 元ウィンドウへ Ctrl+V を送る。
    /// plainTextOnly=false のときだけ、保存されている書式（HTML/RTF）を保持して貼り付ける（既定はプレーンテキストのみ）。
    /// </summary>
    public async Task PasteAsync(ClipItem item, bool plainTextOnly = true)
    {
        if (!WriteClipboard(item, plainTextOnly))
            return;

        if (_previousWindow == IntPtr.Zero)
            return;

        // 元のウィンドウを確実に前面へ戻す（キャレット位置はアプリ側が復元する）
        RestoreForeground(_previousWindow);
        await Task.Delay(90); // フォーカス遷移とキャレット復帰待ち
        SendCtrlV();
    }

    /// <summary>
    /// Windows のフォアグラウンド奪取制限を AttachThreadInput で回避し、
    /// 元ウィンドウを確実にアクティブへ戻す（Win+V / Ditto と同方式）。
    /// </summary>
    private static void RestoreForeground(IntPtr target)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        uint targetThread = NativeMethods.GetWindowThreadProcessId(target, out _);
        uint foreThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();

        if (foreThread != targetThread)
            NativeMethods.AttachThreadInput(foreThread, targetThread, true);
        NativeMethods.AttachThreadInput(thisThread, targetThread, true);

        NativeMethods.SetForegroundWindow(target);
        NativeMethods.BringWindowToTop(target);

        NativeMethods.AttachThreadInput(thisThread, targetThread, false);
        if (foreThread != targetThread)
            NativeMethods.AttachThreadInput(foreThread, targetThread, false);
    }

    private bool WriteClipboard(ClipItem item, bool plainTextOnly)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                switch (item.Kind)
                {
                    case ClipKind.Text:
                        if (plainTextOnly || (item.Html == null && item.Rtf == null))
                        {
                            Clipboard.SetText(item.Text ?? string.Empty);
                        }
                        else
                        {
                            // プレーンテキストと合わせて書式も載せる。貼り付け先が対応する形式を選ぶ
                            // （Word は RTF、ブラウザ/note.com 等は HTML を優先して読む）。
                            var textData = new DataObject();
                            textData.SetText(item.Text ?? string.Empty);
                            if (item.Html != null)
                                textData.SetData(DataFormats.Html, item.Html);
                            if (item.Rtf != null)
                                textData.SetData(DataFormats.Rtf, item.Rtf);
                            Clipboard.SetDataObject(textData, true);
                        }
                        break;

                    case ClipKind.Files:
                        var paths = ClipItem.SplitFilePaths(item.Text);
                        // コピー元がその後に移動・削除されていたら貼り付けられない（Windows標準の挙動と同じ）
                        if (paths.Length == 0 || paths.Any(p => !File.Exists(p) && !Directory.Exists(p)))
                            return false;

                        var fileList = new System.Collections.Specialized.StringCollection();
                        fileList.AddRange(paths);
                        var filesData = new DataObject();
                        filesData.SetFileDropList(fileList);
                        Clipboard.SetDataObject(filesData, true);
                        break;

                    default: // Image
                        var img = ImageHelper.LoadFromPath(item.ImagePath);
                        if (img == null) return false;

                        // ビットマップ（ペイント/Word/チャット向け）と
                        // ファイル参照（エクスプローラ向け）の両方を載せる
                        var data = new DataObject();
                        data.SetImage(img);

                        var file = ImageHelper.GetPasteableFile(item.ImagePath);
                        if (file != null)
                        {
                            var imgFiles = new System.Collections.Specialized.StringCollection { file };
                            data.SetFileDropList(imgFiles);
                        }

                        Clipboard.SetDataObject(data, true);
                        break;
                }
                // 書き込み完了後のシーケンス番号を控え、このこだまだけを無視する。
                _monitor.SuppressSelfWrite();
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    private static void SendCtrlV()
    {
        var inputs = new NativeMethods.INPUT[4];

        inputs[0] = KeyDown(NativeMethods.VK_CONTROL);
        inputs[1] = KeyDown(NativeMethods.VK_V);
        inputs[2] = KeyUp(NativeMethods.VK_V);
        inputs[3] = KeyUp(NativeMethods.VK_CONTROL);

        NativeMethods.SendInput((uint)inputs.Length, inputs,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT KeyDown(ushort vk) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = vk } }
    };

    private static NativeMethods.INPUT KeyUp(ushort vk) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = NativeMethods.KEYEVENTF_KEYUP }
        }
    };
}
