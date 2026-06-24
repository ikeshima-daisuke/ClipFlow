using System;
using System.Windows.Media.Imaging;
using ClipFlow.Models;
using ClipFlow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipFlow.ViewModels;

/// <summary>一覧表示用に ClipItem をラップ。</summary>
public partial class ClipItemViewModel : ObservableObject
{
    public ClipItem Item { get; }

    [ObservableProperty]
    private bool _pinned;

    private BitmapImage? _thumbnail;

    public ClipItemViewModel(ClipItem item)
    {
        Item = item;
        _pinned = item.Pinned;
    }

    public bool IsImage => Item.Kind == ClipKind.Image;
    public bool IsText => Item.Kind == ClipKind.Text;
    public string Preview => Item.Preview;

    /// <summary>サムネイル（画像のとき）。初回アクセス時に遅延読み込み。</summary>
    public BitmapImage? Thumbnail => _thumbnail ??= IsImage ? ImageHelper.LoadFromPath(Item.ThumbPath) : null;

    public string TimeAgo
    {
        get
        {
            var span = DateTime.Now - Item.CreatedAt;
            if (span.TotalMinutes < 1) return "たった今";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}分前";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}時間前";
            return $"{(int)span.TotalDays}日前";
        }
    }

    partial void OnPinnedChanged(bool value) => Item.Pinned = value;
}
