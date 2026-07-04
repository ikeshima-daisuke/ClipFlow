using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ClipFlow.Models;

namespace ClipFlow.Services;

/// <summary>
/// クリップボード変更を監視し、テキスト/画像を ClipItem 化して通知する。
/// 指定ウィンドウの HWND にフォーマットリスナーを登録して使う。
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly SelfCopyGate _gate = new();

    public event Action<ClipItem>? Captured;

    /// <summary>
    /// 自前でクリップボードへ書き込んだ直後に呼ぶ。書き込み後のシーケンス番号を控え、
    /// そのこだま（同番号の変更通知）だけを無視する。本物の後続コピーは取りこぼさない。
    /// </summary>
    public void SuppressSelfWrite()
        => _gate.ExpectSelfWrite(NativeMethods.GetClipboardSequenceNumber());

    public ClipboardMonitor(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("HWND からソースを取得できません。");
        _source.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(_hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            OnClipboardChanged();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>true の間は変更を検知しても履歴へ取り込まない（トレイメニューの「記録を一時停止」用）。</summary>
    public bool IsPaused { get; set; }

    private void OnClipboardChanged()
    {
        // 自前書き込みのこだまはシーケンス番号で厳密に判定して無視する。
        if (_gate.ShouldSuppress(NativeMethods.GetClipboardSequenceNumber()))
            return;

        if (IsPaused)
            return;

        var item = TryCapture();
        if (item != null)
            Captured?.Invoke(item);
    }

    /// <summary>クリップボードは一時的にロックされるためリトライしつつ読む。</summary>
    private ClipItem? TryCapture()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                // ファイル（エクスプローラのコピー）を最優先。画像は画像コピー時に空テキストも
                // 入る場合があるため、テキストより先に見る。
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    var paths = files?.Cast<string>().Where(p => !string.IsNullOrEmpty(p)).ToArray();
                    if (paths is { Length: > 0 })
                        return BuildFilesItem(paths);
                }

                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                        return BuildImageItem(img);
                }

                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        // 書式（CF_HTML/CF_RTF）があれば併せて保持し、書式付き貼り付けに使う。
                        // どちらも生の文字列のまま保存し、貼り付け時にそのまま書き戻す。
                        string? html = Clipboard.ContainsData(DataFormats.Html)
                            ? Clipboard.GetData(DataFormats.Html) as string
                            : null;
                        string? rtf = Clipboard.ContainsData(DataFormats.Rtf)
                            ? Clipboard.GetData(DataFormats.Rtf) as string
                            : null;
                        return BuildTextItem(text, html, rtf);
                    }
                }

                return null;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
        return null;
    }

    private static ClipItem BuildTextItem(string text, string? html, string? rtf)
    {
        var preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (preview.Length > 200)
            preview = preview[..200];

        return new ClipItem
        {
            Kind = ClipKind.Text,
            Text = text,
            Html = html,
            Rtf = rtf,
            Preview = preview,
            // 重複判定はプレーンテキストのみで行う（書式違いは同一内容として扱う）。
            Hash = "t:" + ImageHelper.Sha256(System.Text.Encoding.UTF8.GetBytes(text)),
            CreatedAt = DateTime.Now,
        };
    }

    private static ClipItem BuildFilesItem(string[] paths)
    {
        var joined = ClipItem.JoinFilePaths(paths);
        var firstName = System.IO.Path.GetFileName(paths[0]);
        var preview = paths.Length == 1 ? firstName : $"{firstName} 他{paths.Length - 1}件";

        return new ClipItem
        {
            Kind = ClipKind.Files,
            Text = joined,
            Preview = preview,
            Hash = "f:" + ImageHelper.Sha256(System.Text.Encoding.UTF8.GetBytes(joined)),
            CreatedAt = DateTime.Now,
        };
    }

    private static ClipItem BuildImageItem(BitmapSource source)
    {
        if (!source.IsFrozen && source.CanFreeze)
            source.Freeze();

        var png = ImageHelper.EncodePng(source);
        var hash = ImageHelper.Sha256(png);
        var (imagePath, thumbPath) = ImageHelper.Save(source, hash);

        return new ClipItem
        {
            Kind = ClipKind.Image,
            ImagePath = imagePath,
            ThumbPath = thumbPath,
            Preview = $"画像  {source.PixelWidth}×{source.PixelHeight}",
            Hash = "i:" + hash,
            CreatedAt = DateTime.Now,
        };
    }

    public void Dispose()
    {
        NativeMethods.RemoveClipboardFormatListener(_hwnd);
        _source.RemoveHook(WndProc);
    }
}
