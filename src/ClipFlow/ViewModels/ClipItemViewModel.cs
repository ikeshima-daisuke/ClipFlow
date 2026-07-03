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
    private BitmapImage? _fullImage;
    private bool _fullImageLoaded;

    public ClipItemViewModel(ClipItem item)
    {
        Item = item;
        _pinned = item.Pinned;
    }

    public bool IsImage => Item.Kind == ClipKind.Image;
    public bool IsText => Item.Kind == ClipKind.Text;
    public string Preview => Item.Preview;

    /// <summary>全文（テキストのとき）。プレビューで選択された際にポップアップへ表示する。</summary>
    public string FullText => Item.Text ?? string.Empty;

    /// <summary>サムネイル（画像のとき）。初回アクセス時に遅延読み込み。</summary>
    public BitmapImage? Thumbnail => _thumbnail ??= IsImage ? ImageHelper.LoadFromPath(Item.ThumbPath) : null;

    /// <summary>原寸画像（画像のとき）。選択時のポップアップ表示用に遅延読み込み。</summary>
    public BitmapImage? FullImage
    {
        get
        {
            if (!_fullImageLoaded)
            {
                _fullImage = IsImage ? ImageHelper.LoadFromPath(Item.ImagePath) : null;
                _fullImageLoaded = true;
            }
            return _fullImage;
        }
    }

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
