using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipFlow.Services;

/// <summary>画像の保存・サムネイル生成・ハッシュ計算。</summary>
internal static class ImageHelper
{
    private const int ThumbMaxWidth = 240;
    private const int ThumbMaxHeight = 160;

    /// <summary>BitmapSource を凍結可能な PNG バイト列に変換。</summary>
    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static string Sha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>原寸PNGとサムネイルPNGをディスクへ保存し、両パスを返す。</summary>
    public static (string imagePath, string thumbPath) Save(BitmapSource source, string hash)
    {
        var imagePath = Path.Combine(AppPaths.ImagesDir, hash + ".png");
        var thumbPath = Path.Combine(AppPaths.ImagesDir, hash + ".thumb.png");

        if (!File.Exists(imagePath))
            File.WriteAllBytes(imagePath, EncodePng(source));

        if (!File.Exists(thumbPath))
        {
            var thumb = CreateThumbnail(source);
            File.WriteAllBytes(thumbPath, EncodePng(thumb));
        }

        return (imagePath, thumbPath);
    }

    private static BitmapSource CreateThumbnail(BitmapSource source)
    {
        double scale = Math.Min(
            (double)ThumbMaxWidth / source.PixelWidth,
            (double)ThumbMaxHeight / source.PixelHeight);
        if (scale >= 1.0)
            return source;

        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    /// <summary>
    /// エクスプローラ等へ「ファイルとして」貼り付けるための一時コピーを作る。
    /// 内部のハッシュ名ではなく分かりやすい名前を付ける。
    /// </summary>
    public static string? GetPasteableFile(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return null;

        var dir = Path.Combine(Path.GetTempPath(), "ClipFlow");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"clipflow_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.Copy(imagePath, dest, overwrite: true);
        return dest;
    }

    /// <summary>ファイルロックを避けてパスから画像を読み込む（OnLoad キャッシュ）。</summary>
    public static BitmapImage? LoadFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
