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

- `Services/ClipboardMonitor` — 隠しウィンドウの HWND に `AddClipboardFormatListener` を張り、`WM_CLIPBOARDUPDATE` でテキスト/画像/ファイル（`CF_HDROP`）を取り込む。自前のクリップボード書込みは `SuppressNext` で無視。ファイルは中身をコピーせず**パスのみ**を `ClipItem.Text` に改行区切りで保存（`ClipItem.JoinFilePaths`/`SplitFilePaths`）。テキストは `CF_HTML`/`CF_RTF` があれば生の文字列のまま `ClipItem.Html`/`Rtf` にも保持（オフセット付きヘッダごと再貼付するので再構築不要）。`IsPaused` が true の間はキャプチャしない（トレイの「記録を一時停止」）。
- `Services/GlobalHotkey` — `RegisterHotKey`（既定 `Ctrl+Shift+V`）。
- `Services/HistoryStore` — SQLite。重複はハッシュで先頭へ繰り上げ、`MaxItems`（既定100、0以下で無制限）超過は古い順に削除。`MaxItems` は実行中に変更可能で、`ApplyMaxItems()` を呼べば即座に反映（減らした場合はその場でevict、増やしても消えた項目は戻らない）。`html`/`rtf` 列は `ALTER TABLE` での後方互換マイグレーション込み（`MigrateAddColumnIfMissing`）。テスト用に dbPath/imagesDir/maxItems を注入可能。
- `Services/PasteService` — **表示直前の前面ウィンドウを記憶 → `AttachThreadInput` で確実に前面復帰 → `SendInput` で Ctrl+V**。画像はビットマップ＋ファイル参照の両方を載せる（エクスプローラ貼付対応）。ファイルはコピー元が移動・削除されていたら貼り付けを中止する。`PasteAsync(item, plainTextOnly: true)` が既定（貼り先の見た目を予測可能にするため常にプレーンテキスト）。`plainTextOnly: false` のときだけ Html/Rtf を併せて書き込む。
- `Services/StartupService` — `HKCU\...\Run` でスタートアップ登録のON/OFF。
- `Services/AppSettings` — `%APPDATA%\ClipFlow\settings.json`。`CheckForUpdates` の既定は **false**（このアプリは元々「外部通信なし」を謳っているため、ネットワークに触る機能は必ずオプトインにする）。`MaxHistoryItems` の既定は `HistoryStore.DefaultMaxItems`（100）、`null`/0以下で無制限。トレイメニュー「保持件数の上限」から変更。
- `Services/UpdateChecker` — GitHub Releases API (`api.github.com`) を叩いて最新タグと現在バージョンを比較。呼ばれない限り通信しない。失敗時は全部 `null` を返して黙る（起動を妨げない）。
- `Services/NativeMethods` — P/Invoke（`LibraryImport`）。`<AllowUnsafeBlocks>` 必須。
- `ViewModels` — CommunityToolkit.Mvvm（`[ObservableProperty]` / `[RelayCommand]`）。`MainViewModel.FilterKind`（`ClipKind?`、null=すべて）と `SetFilterCommand`（XAMLからは文字列 `"Text"`/`"Image"`/`"Files"`/null で呼ぶ）で種別フィルターを実装。`IsFilterAll`/`IsFilterText`/`IsFilterImage`/`IsFilterFiles` はXAML側のタブ強調表示用の派生bool。ポップアップを開くたびに検索文字列と一緒にリセットされる。`PasteCommand`＝既定（プレーンテキスト）、`PasteWithFormattingCommand`＝書式保持。書式（Html/Rtfのどちらか）を持つ項目だけ `ClipItemViewModel.HasFormatting` が true になり、一覧に「Aa」ボタンを表示（キーボードショートカットだけに頼らない発見しやすさのため）。

## 落とし穴（テストで固定済み）

- `SendInput` の `INPUT` 構造体の共用体は **MOUSEINPUT を含めて x64 で 40 バイト** にすること。KEYBDINPUT だけだと 32 バイトになり SendInput が無言で失敗して貼り付かない。→ `tests/ClipFlow.Tests/NativeInputTests.cs` で固定。
- 自前書き込み（コピー/ペースト）の無視は **bool フラグではなく `GetClipboardSequenceNumber` で識別** すること。「次の通知を1回無視」する bool 方式は、こだまが届かないとフラグが立ちっぱなしになり次の本物コピーを取りこぼす。→ `Services/SelfCopyGate` と `tests/ClipFlow.Tests/SelfCopyGateTests.cs` で固定。

## 方針

- ネイティブ/OS連携は副作用が再現しにくいので、**サイズ・ロジックをテストで固定**し、UI実機確認と併用する。
- 変更後は対象プロジェクトをビルドし、テストを通すこと。
