# CLAUDE.md

ClipFlow — Windows 向けクリップボード履歴マネージャ（C# / WPF / .NET 10）。

## ビルド・実行・テスト

```sh
dotnet build src/ClipFlow/ClipFlow.csproj
dotnet run   --project src/ClipFlow/ClipFlow.csproj
dotnet test  tests/ClipFlow.Tests/ClipFlow.Tests.csproj
```

ソリューション: `ClipFlow.slnx`（`src/ClipFlow` と `tests/ClipFlow.Tests`）。

## 重要な開発上の制約（このマシン）

- **実行中アプリが exe/dll をロックする** → 再ビルド前に終了が必要。csproj に `taskkill` するビルド前ターゲットを入れてあるが、別権限で起動された実体は落とせないことがある（その場合はトレイの「終了」で閉じてもらう）。
- **PowerShell を Bash 経由で呼ぶのは拒否される**。`tasklist` / `taskkill` / `dotnet` / `reg` など素のコマンドを使う。
- **自作 exe は computer-use で操作許可を付与できない**（スタートメニュー未登録のため）。GUI の検証は DB(`%APPDATA%\ClipFlow\clipflow.db`) やログなど副作用で間接的に行うか、ユーザーに操作してもらう。

## アーキテクチャ

- `Services/ClipboardMonitor` — 隠しウィンドウの HWND に `AddClipboardFormatListener` を張り、`WM_CLIPBOARDUPDATE` でテキスト/画像を取り込む。自前のクリップボード書込みは `SuppressNext` で無視。
- `Services/GlobalHotkey` — `RegisterHotKey`（既定 `Ctrl+Shift+V`）。
- `Services/HistoryStore` — SQLite。重複はハッシュで先頭へ繰り上げ、上限100超過は古い順に削除。テスト用に dbPath/imagesDir を注入可能。
- `Services/PasteService` — **表示直前の前面ウィンドウを記憶 → `AttachThreadInput` で確実に前面復帰 → `SendInput` で Ctrl+V**。テキストはビットマップ＋ファイル参照の両方を載せる（エクスプローラ貼付対応）。
- `Services/StartupService` — `HKCU\...\Run` でスタートアップ登録のON/OFF。
- `Services/NativeMethods` — P/Invoke（`LibraryImport`）。`<AllowUnsafeBlocks>` 必須。
- `ViewModels` — CommunityToolkit.Mvvm（`[ObservableProperty]` / `[RelayCommand]`）。

## 落とし穴（テストで固定済み）

- `SendInput` の `INPUT` 構造体の共用体は **MOUSEINPUT を含めて x64 で 40 バイト** にすること。KEYBDINPUT だけだと 32 バイトになり SendInput が無言で失敗して貼り付かない。→ `tests/ClipFlow.Tests/NativeInputTests.cs` で固定。

## 方針

- ネイティブ/OS連携は副作用が再現しにくいので、**サイズ・ロジックをテストで固定**し、UI実機確認と併用する。
- 変更後は対象プロジェクトをビルドし、テストを通すこと。
