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
- `Services/IHotkeyTrigger` — 「履歴ポップアップを呼び出すグローバルトリガー」の共通インターフェース（`Pressed`イベント + `IsRegistered`）。`GlobalHotkey`（組み合わせ）と`ModifierTapHotkey`（連打）の2実装を`App`から同じ扱いで持てるようにするためだけの薄い抽象。
- `Services/GlobalHotkey` — `RegisterHotKey`（既定 `Ctrl+Shift+V`）。`Rebind(modifiers, virtualKey)` で別の組み合わせへ登録し直せる。同じホットキーID(`HotkeyId`)を使い回すため、登録済みなら一度 `UnregisterHotKey` してから登録し直す必要がある点に注意。新しい組み合わせの登録に失敗（他アプリが使用中）した場合は、直前まで有効だった組み合わせへ自動で復帰を試みる（`Rebind`失敗時に無音でホットキーが一切効かなくなる事故を避けるため）。
- `Services/ModifierTapHotkey` + `Services/ModifierTapDetector` — 修飾キー単体の連打（例: Ctrl連打）でポップアップを呼び出すモード。`RegisterHotKey`は修飾キー単体を扱えないため、`WH_KEYBOARD_LL`の低レベルキーボードフックで押下/離上を拾う。判定ロジック本体は`ModifierTapDetector`（OS非依存の純粋クラス）に切り出してあり、`tests/ClipFlow.Tests/ModifierTapDetectorTests.cs`でテスト可能。「対象キーを押しっぱなしの間に他キー（別の修飾キー含む）が押されたら組み合わせ扱いにして連打判定から除外する」ロジックが肝（Ctrl+Cの直後に単発Ctrlを押しても誤爆しないため）。フックのコールバックデリゲートはインスタンスフィールドで保持し続ける必要がある（GCされるとネイティブ側からのコールバックが不正アドレスを呼ぶ）。`SetWindowsHookEx`/`UnhookWindowsHookEx`/`CallNextHookEx`の3つだけは、他と違い`LibraryImport`ではなく従来の`DllImport`を使う（ソースジェネレータはデリゲート引数の自動マーシャリングに未対応のため）。
- `Services/HotkeySpec` — 「組み合わせ」と「連打」のどちらのモードも表せる値。`AppSettings`/`HotkeyDialog`/`App`の間でモードを問わず1つの値として受け渡すための構造体（`HotkeyIsDoubleTap`がfalseなら`Modifiers`+`VirtualKey`が組み合わせ、trueなら`Modifiers`が単一の対象修飾キー）。
- `Services/HotkeyFormat` — WPFの`ModifierKeys`/`Key`とWin32の`MOD_*`ビットマスク/仮想キーコードの相互変換、"Ctrl+Shift+V"や"Ctrl連打"のような表示文字列の組み立て（`Format(HotkeySpec)`がモードを見て振り分ける）。`HotkeyDialog`（キー入力キャプチャUI）とトレイのラベル表示の両方から使う共通ロジックなので、ここに集約している。
- `Services/HistoryStore` — SQLite。重複はハッシュで先頭へ繰り上げ、`MaxItems`（既定100、0以下で無制限）超過は古い順に削除。`MaxItems` は実行中に変更可能で、`ApplyMaxItems()` を呼べば即座に反映（減らした場合はその場でevict、増やしても消えた項目は戻らない）。`html`/`rtf` 列は `ALTER TABLE` での後方互換マイグレーション込み（`MigrateAddColumnIfMissing`）。テスト用に dbPath/imagesDir/maxItems を注入可能。
- `Services/PasteService` — **表示直前の前面ウィンドウを記憶 → `AttachThreadInput` で確実に前面復帰 → `SendInput` で Ctrl+V**。画像はビットマップ＋ファイル参照の両方を載せる（エクスプローラ貼付対応）。ファイルはコピー元が移動・削除されていたら貼り付けを中止する。`PasteAsync(item, plainTextOnly: true)` が既定（貼り先の見た目を予測可能にするため常にプレーンテキスト）。`plainTextOnly: false` のときだけ Html/Rtf を併せて書き込む。
- `Services/StartupService` — `HKCU\...\Run` でスタートアップ登録のON/OFF。
- `Services/AppSettings` — `%APPDATA%\ClipFlow\settings.json`。`MaxHistoryItems` の既定は `HistoryStore.DefaultMaxItems`（100）、`null`/0以下で無制限。トレイメニュー「保持件数の上限」から変更。`HotkeyModifiers`/`HotkeyVirtualKey`（Win32のMOD_*/VKをそのまま数値保存、既定はCtrl+Shift+V）と`HotkeyIsDoubleTap`/`HotkeyDoubleTapModifier`（連打モード用、既定false/MOD_CONTROL）はトレイメニュー「ショートカットを変更...」から変更。`WindowWidth`/`WindowHeight`（`double?`、未設定ならXAMLの既定サイズ）はポップアップを隠すたび（`App.HideWindow()`）に現在サイズを保存する。**ClipFlowは外部通信を一切行わない方針**なので、今後ここに設定を追加する際もネットワークに触るものは入れない。
- `Services/NativeMethods` — P/Invoke（`LibraryImport`）。`<AllowUnsafeBlocks>` 必須。
- `ViewModels` — CommunityToolkit.Mvvm（`[ObservableProperty]` / `[RelayCommand]`）。`MainViewModel.FilterKind`（`ClipKind?`、null=すべて）と `SetFilterCommand`（XAMLからは文字列 `"Text"`/`"Image"`/`"Files"`/null で呼ぶ）で種別フィルターを実装。`IsFilterAll`/`IsFilterText`/`IsFilterImage`/`IsFilterFiles` はXAML側のタブ強調表示用の派生bool。ポップアップを開くたびに検索文字列と一緒にリセットされる。`PasteCommand`＝既定（プレーンテキスト）、`PasteWithFormattingCommand`＝書式保持。書式（Html/Rtfのどちらか）を持つ項目だけ `ClipItemViewModel.HasFormatting` が true になり、一覧に「Aa」ボタンを表示（キーボードショートカットだけに頼らない発見しやすさのため）。
- `HotkeyDialog`（ViewModelなし、コードビハインド直書き） — 「組み合わせ」/「連打」のモードをラジオボタンで切り替える。組み合わせモードは`PreviewKeyDown`でキー入力をキャプチャし修飾キー単体では確定しない、連打モードはコンボボックスでCtrl/Shift/Alt/Winのどれを監視するか選ぶ。「保存」押下時に呼び出し元から渡された `tryApply` コールバック（実体は `App.TryApplyHotkey`。モードが変わらなければ既存トリガーへ`Rebind`、モードが変わる（組み合わせ⇔連打）ときは作り直す）を呼び、実際に登録できた場合だけダイアログを閉じる。失敗時はダイアログを開いたままエラーメッセージを表示し、別の設定を試せるようにする。
- `MainWindow` は `ResizeMode="CanResize"`（`MinWidth`/`MinHeight` あり）。リサイズ後のサイズは `App.HideWindow()` で `AppSettings.WindowWidth`/`WindowHeight` に保存し、次回起動時（`App.xaml.cs` の `_window` 生成直後）に復元する。作業領域を超える保存値は `SystemParameters.WorkArea` で頭打ちにする。
- `Themes/ClipFlowPalette.xaml` — MainWindow専用（`MainWindow.xaml`の`Window.Resources`にのみマージ、Application全体には広げない）の背景・文字色の固定パレット。壁紙やAcrylicの半透明合成に依存せず、WCAGコントラスト比を実測して決めた値（text-primary 15.2:1 / secondary 8.7:1 / tertiary 6.1:1、いずれもAA以上）。WPF-UIの既定Darkテーマと同名のキー（`ApplicationBackgroundBrush`等）を後からマージして上書きする。**Applicationスコープでマージしないこと** — `HotkeyDialog`は`WindowBackdropType="Mica"`のままなので、この不透明な背景色が全体に広がるとMica効果が透けなくなる。`MainWindow`側は逆に、背景が不透明になったことで`WindowBackdropType="Acrylic"`の意味がなくなるため`None`に変更済み。
- `Services/AccentPalette` + `Services/AccentThemeService` — アクセントカラー（ブルー/インディゴ/ティールの3択、既定ティール）。`AccentThemeService.Apply`はWPF-UI公式の`Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(color, color, color, color)`（4引数オーバーロード）を呼ぶ。**2引数オーバーロード（`Apply(color, theme)`）は使わないこと** — システムのOSアクセントカラーやテーマに応じて入力色を自動的に明るく変換してしまい、指定した色そのものにならない（実際に確認済み: teal `#0A8074`指定で`#5AABA3`になった）。4引数版はsystemAccent/primary/secondary/tertiaryに同じ色を渡すことで、指定した色をそのまま`AccentFillColorDefaultBrush`等へ反映できる。`AppSettings.Accent`に永続化し、トレイメニュー「アクセントカラー」（`App.BuildAccentMenu()`、`BuildMaxItemsMenu()`と同じチェック式サブメニューのパターン）から切替。

## 落とし穴（テストで固定済み）

- `SendInput` の `INPUT` 構造体の共用体は **MOUSEINPUT を含めて x64 で 40 バイト** にすること。KEYBDINPUT だけだと 32 バイトになり SendInput が無言で失敗して貼り付かない。→ `tests/ClipFlow.Tests/NativeInputTests.cs` で固定。
- 自前書き込み（コピー/ペースト）の無視は **bool フラグではなく `GetClipboardSequenceNumber` で識別** すること。「次の通知を1回無視」する bool 方式は、こだまが届かないとフラグが立ちっぱなしになり次の本物コピーを取りこぼす。→ `Services/SelfCopyGate` と `tests/ClipFlow.Tests/SelfCopyGateTests.cs` で固定。
- 修飾キー連打（`ModifierTapDetector`）の判定は、対象キーが押しっぱなしの間に**他キー（別の修飾キー含む）が押されたら「組み合わせ」扱いにして連打から除外する**こと。これがないと Ctrl+C の直後に単発で Ctrl を押しただけで誤爆する。→ `tests/ClipFlow.Tests/ModifierTapDetectorTests.cs` で固定。
- XAMLの`ResourceDictionary`で定義した`SolidColorBrush`はWPFが自動でフリーズ（読み取り専用化）するため、`Application.Resources["Key"]`から取得したブラシの`.Color`をその場で書き換えようとすると`InvalidOperationException`（実行時にしか気づけない）。切り替え可能な色は、キーへ新しいブラシ/値を代入するか（`Application.Resources["Key"] = ...`）、専用のマネージャAPI（`ApplicationAccentColorManager`等）を使うこと。

## 方針

- ネイティブ/OS連携は副作用が再現しにくいので、**サイズ・ロジックをテストで固定**し、UI実機確認と併用する。
- 変更後は対象プロジェクトをビルドし、テストを通すこと。
