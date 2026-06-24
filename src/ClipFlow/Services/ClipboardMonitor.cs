using System;
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

    /// <summary>自前でクリップボードを書き換えるときに立てると、その変更を無視する。</summary>
    public bool SuppressNext { get; set; }

    public event Action<ClipItem>? Captured;

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

    private void OnClipboardChanged()
    {
        if (SuppressNext)
        {
            SuppressNext = false;
            return;
        }

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
                // 画像を優先（画像コピー時に空テキストも入る場合があるため）
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
                        return BuildTextItem(text);
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

    private static ClipItem BuildTextItem(string text)
    {
        var preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (preview.Length > 200)
            preview = preview[..200];

        return new ClipItem
        {
            Kind = ClipKind.Text,
            Text = text,
            Preview = preview,
            Hash = "t:" + ImageHelper.Sha256(System.Text.Encoding.UTF8.GetBytes(text)),
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
