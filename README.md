# ClipFlow

Windows 向けの軽量クリップボード履歴マネージャ。コピーしたテキスト・画像を自動で履歴に溜め、ホットキーでいつでも呼び出して貼り付けられます。

![tray](src/ClipFlow/Assets/clipflow.ico)

## 特長

- 📋 **テキスト / 画像** を自動でキャプチャして履歴化
- ⌨️ **`Ctrl+Shift+V`** で履歴ポップアップを表示
- 🔎 **検索** で素早く絞り込み
- 🖼️ 画像は **サムネイル表示**、エクスプローラーにも貼り付け可（ファイル化）
- 📌 **ピン留め**でよく使う項目を常に上部へ
- 🎯 **元のカーソル位置へ自動ペースト**（クリック / Enter）
- 🪶 **最大100件**保持・SQLite永続化・軽快動作
- 🎨 Fluent Design（Acrylic）のスマートなUI、タスクトレイ常駐

## 操作

| 操作 | 動作 |
|---|---|
| `Ctrl+Shift+V` | 履歴ポップアップ表示 / 非表示 |
| `↑` `↓` | 履歴セルを選択 |
| `Enter` | 選択を元の場所へ貼り付け |
| `Ctrl+C` | 貼り付けず、クリップボードへコピーのみ |
| `Esc` | 閉じる |
| クリック | その項目を貼り付け |
| トレイ右クリック | 履歴クリア / 起動設定 / 終了 |

## 動作環境

- Windows 10 / 11
- .NET 10 ランタイム

## ビルドと実行

```sh
# ビルド
dotnet build src/ClipFlow/ClipFlow.csproj

# 実行
dotnet run --project src/ClipFlow/ClipFlow.csproj

# テスト
dotnet test tests/ClipFlow.Tests/ClipFlow.Tests.csproj
```

> 実行中はアプリが exe をロックするため、再ビルド前に終了が必要です（csproj のビルド前ターゲットで自動終了します）。

## スタートアップ登録

タスクトレイアイコンを右クリック → **「Windows起動時に実行」** で切替できます（レジストリ `HKCU\...\Run`）。

## データ保存先

`%APPDATA%\ClipFlow\`（`clipflow.db` と `images/`）

## 構成

```
src/ClipFlow/
  Models/        ClipItem
  Services/      ClipboardMonitor / GlobalHotkey / HistoryStore / PasteService / ImageHelper / StartupService / NativeMethods
  ViewModels/    MainViewModel / ClipItemViewModel
  MainWindow.*   履歴ポップアップ(Fluent)
  App.*          常駐・トレイ・ホットキー配線
tests/ClipFlow.Tests/   xUnit テスト
```

## 技術スタック

C# / WPF / .NET 10 / [WPF-UI](https://github.com/lepoco/wpfui) / Microsoft.Data.Sqlite / CommunityToolkit.Mvvm / Hardcodet.NotifyIcon.Wpf
