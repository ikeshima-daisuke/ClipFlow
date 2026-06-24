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

    private void ApplyFilter()
    {
        IEnumerable<ClipItemViewModel> query = _all;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            query = _all.Where(v => v.Preview.Contains(q, StringComparison.OrdinalIgnoreCase));
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

    [RelayCommand]
    private async Task PasteAsync(ClipItemViewModel? vm)
    {
        if (vm == null) return;
        _hideWindow();
        await _paste.PasteAsync(vm.Item);
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
