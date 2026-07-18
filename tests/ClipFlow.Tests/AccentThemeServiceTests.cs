using System.Windows.Media;
using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>アクセントパレット（列挙値）→実際の色（WCAGコントラスト比を検証済みのHex）の対応を固定する。</summary>
public class AccentThemeServiceTests
{
    [Fact]
    public void GetColor_blue_is_hex_2563EB()
    {
        Assert.Equal(Color.FromRgb(0x25, 0x63, 0xEB), AccentThemeService.GetColor(AccentPalette.Blue));
    }

    [Fact]
    public void GetColor_indigo_is_hex_4F46E5()
    {
        Assert.Equal(Color.FromRgb(0x4F, 0x46, 0xE5), AccentThemeService.GetColor(AccentPalette.Indigo));
    }

    [Fact]
    public void GetColor_teal_is_hex_0A8074()
    {
        Assert.Equal(Color.FromRgb(0x0A, 0x80, 0x74), AccentThemeService.GetColor(AccentPalette.Teal));
    }
}
