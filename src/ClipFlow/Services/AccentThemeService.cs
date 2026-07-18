using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace ClipFlow.Services;

/// <summary>
/// アクセントカラーの色定義と、WPF-UI公式の <see cref="ApplicationAccentColorManager"/> への反映。
/// systemAccent/primary/secondary/tertiaryに同じ色を渡す4引数オーバーロードを使うことで、
/// システムのOSアクセントカラーやテーマ別の自動調光（Light/Dark引数を取る2引数版は入力色を
/// 明るく変換してしまい、WCAGで検証した色と一致しなくなる）を避け、指定した色をそのまま
/// AccentFillColorDefaultBrush等へ反映する。
/// </summary>
public static class AccentThemeService
{
    public static Color GetColor(AccentPalette palette) => palette switch
    {
        AccentPalette.Blue => Color.FromRgb(0x25, 0x63, 0xEB),
        AccentPalette.Indigo => Color.FromRgb(0x4F, 0x46, 0xE5),
        AccentPalette.Teal => Color.FromRgb(0x0A, 0x80, 0x74),
        _ => Color.FromRgb(0x0A, 0x80, 0x74),
    };

    public static void Apply(AccentPalette palette)
    {
        var color = GetColor(palette);
        ApplicationAccentColorManager.Apply(color, color, color, color);
    }
}
