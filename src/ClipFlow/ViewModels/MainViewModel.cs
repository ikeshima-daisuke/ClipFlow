using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipFlow.Models;
using ClipFlow.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipFlow.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HistoryStore _store;
    private readonly PasteService _paste;
    private readonly Action _hideWindow;

    private readonly List<ClipItemViewModel> _all = new();

    public ObservableCollection<ClipItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>種別フィルター。null = すべて表示。</summary>
    [ObservableProperty]
    private ClipKind? _filterKind;

    public bool IsFilterAll => FilterKind == null;
    public bool IsFilterText => FilterKind == ClipKind.Text;
    public bool IsFilterImage => FilterKind == ClipKind.Image;
    public bool IsFilterFiles => FilterKind == ClipKind.Files;

    public MainViewModel(HistoryStore store, PasteService paste, Action hideWindow)
    {
        _store = store;
        _paste = paste;
        _hideWindow = hideWindow;
        Reload();
    }

    public void Reload()
    {
        _all.Clear();
        foreach (var item in _store.GetAll())
            _all.Add(new ClipItemViewModel(item));
        ApplyFilter();
    }

    /// <summary>新規キャプチャを永続化して一覧へ反映する。</summary>
    public void OnCaptured(ClipItem item)
    {
        // DBへ保存（重複は先頭へ繰り上げ、上限100超過は古い順に自動削除）
        _store.Add(item);
        // 保存後の状態（並び順・上限・ピン留め）をそのまま反映
        Reload();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnFilterKindChanged(ClipKind? value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterText));
        OnPropertyChanged(nameof(IsFilterImage));
        OnPropertyChanged(nameof(IsFilterFiles));
        ApplyFilter();
    }

    /// <summary>種別フィルターを設定する。"Text"/"Image"/"Files"、それ以外（null含む）は「すべて」。</summary>
    [RelayCommand]
    private void SetFilter(string? kind)
    {
        FilterKind = kind switch
        {
            "Text" => ClipKind.Text,
            "Image" => ClipKind.Image,
            "Files" => ClipKind.Files,
            _ => null,
        };
    }

    private void ApplyFilter()
    {
        IEnumerable<ClipItemViewModel> query = _all;

        if (FilterKind is ClipKind kind)
            query = query.Where(v => v.Item.Kind == kind);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            query = query.Where(v => v.Preview.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // ピン留め優先で並べ替え
        var ordered = query
            .OrderByDescending(v => v.Pinned)
            .ThenByDescending(v => v.Item.CreatedAt)
            .ToList();

        Items.Clear();
        foreach (var v in ordered)
            Items.Add(v);

        IsEmpty = Items.Count == 0;
    }

    /// <summary>既定の貼り付け。書式があっても常にプレーンテキストで貼り付ける（貼り先の見た目が予測できるように）。</summary>
    [RelayCommand]
    private async Task PasteAsync(ClipItemViewModel? vm)
    {
        if (vm == null) return;
        _hideWindow();
        await _paste.PasteAsync(vm.Item, plainTextOnly: true);
    }

    /// <summary>保存されている書式（HTML/RTF）を保持したまま貼り付ける。一覧の「Aa」ボタン、または Ctrl+Shift+Enter から。</summary>
    [RelayCommand]
    private async Task PasteWithFormattingAsync(ClipItemViewModel? vm)
    {
        if (vm == null) return;
        _hideWindow();
        await _paste.PasteAsync(vm.Item, plainTextOnly: false);
    }

    [RelayCommand]
    private void Copy(ClipItemViewModel? vm)
    {
        if (vm == null) return;
        _paste.CopyToClipboard(vm.Item);
        _hideWindow();
    }

    [RelayCommand]
    private void Delete(ClipItemViewModel? vm)
    {
        if (vm == null) return;
        _store.Delete(vm.Item.Id);
        _all.Remove(vm);
        ApplyFilter();
    }

    [RelayCommand]
    private void TogglePin(ClipItemViewModel? vm)
    {
        if (vm == null) return;
        vm.Pinned = !vm.Pinned;
        _store.SetPinned(vm.Item.Id, vm.Pinned);
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _store.Clear(includePinned: false);
        Reload();
    }
}
