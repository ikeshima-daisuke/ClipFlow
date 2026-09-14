using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// ImageHelper の現状の振る舞いを固定する特性化テスト。
/// Save は保存先が AppPaths.ImagesDir(%APPDATA%\ClipFlow\images)に固定で一時ディレクトリを注入できないため対象外。
/// </summary>
public class ImageHelperTests
{
    [Fact]
    public void Sha256_returns_lowercase_hex_of_empty_input()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ImageHelper.Sha256([]));
    }

    [Fact]
    public void Sha256_returns_lowercase_hex_of_abc()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ImageHelper.Sha256(Encoding.ASCII.GetBytes("abc")));
    }

    [Fact]
    public void EncodePng_output_loads_back_with_same_size_and_without_file_lock()
    {
        var png = ImageHelper.EncodePng(CreateBitmap(width: 2, height: 3));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);

        var path = Path.Combine(Path.GetTempPath(), $"clipflow-test-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, png);

            var loaded = ImageHelper.LoadFromPath(path);

            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.PixelWidth);
            Assert.Equal(3, loaded.PixelHeight);
            Assert.True(loaded.IsFrozen);
        }
        finally
        {
            // OnLoad キャッシュで読み込むのでファイルはロックされておらず、ここで消せる
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LoadFromPath_returns_null_for_empty_path(string? path)
    {
        Assert.Null(ImageHelper.LoadFromPath(path));
    }

    [Fact]
    public void LoadFromPath_returns_null_for_missing_file()
    {
        Assert.Null(ImageHelper.LoadFromPath(MissingPath()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetPasteableFile_returns_null_for_empty_path(string? path)
    {
        Assert.Null(ImageHelper.GetPasteableFile(path));
    }

    [Fact]
    public void GetPasteableFile_returns_null_for_missing_file()
    {
        Assert.Null(ImageHelper.GetPasteableFile(MissingPath()));
    }

    [Fact]
    public void GetPasteableFile_copies_into_temp_ClipFlow_dir_with_readable_name()
    {
        var source = Path.Combine(Path.GetTempPath(), $"clipflow-test-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        string? dest = null;
        try
        {
            dest = ImageHelper.GetPasteableFile(source);

            Assert.NotNull(dest);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "ClipFlow"), Path.GetDirectoryName(dest));
            Assert.StartsWith("clipflow_", Path.GetFileName(dest));
            Assert.EndsWith(".png", dest);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dest!));
        }
        finally
        {
            File.Delete(source);
            if (dest != null) File.Delete(dest);
        }
    }

    private static BitmapSource CreateBitmap(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static string MissingPath() => Path.Combine(Path.GetTempPath(), $"clipflow-missing-{Guid.NewGuid():N}.png");
}
