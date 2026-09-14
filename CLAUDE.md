# CLAUDE.md

ClipFlow — Windows 向けクリップボード履歴マネージャ（C# / WPF / .NET 10）。

## 開発フロー(必須)

新機能・修正・バグ修正は必ず `/dev` スキル経由で進めること:
**要件確認 → テスト先行 → 実装 → 動作確認 → コミット** を基本線に、変更の規模・リスクに応じて設計(ARCHITECTURE.md更新)・コードレビュー・codexレビューが加わる。分類基準(S/M/L)は `/dev` スキルの定義に従う。

- テストのないコードをコミットしない(ビルド/テスト失敗中の `git commit` はhookがブロックする)
- 構造(クラス追加・責務変更)を変えたら docs/ARCHITECTURE.md を同じコミットで更新する
- 一区切りついたら `/daily` で整備チェックを実行する

## ビルド・実行・テスト

```sh
dotnet build src/ClipFlow/ClipFlow.csproj
dotnet run   --project src/ClipFlow/ClipFlow.csproj
dotnet test  tests/ClipFlow.Tests/ClipFlow.Tests.csproj
dotnet test                                   # リポジトリ直下でソリューション全体(コミット前hookと同じ)
dotnet test --collect:"XPlat Code Coverage"   # カバレッジ
dotnet format                                 # 整形
```

- `.cs` は Edit / Write のたびに hook が `dotnet format whitespace` で整形する。改行コードは `.editorconfig` で CRLF に統一している(未指定だと書き換えた行だけ CRLF になり混在する)
- コミット前 hook の `dotnet test` もビルドを伴うので、下記の `taskkill` により起動中の ClipFlow(自動起動用の安定版を含む)が終了する
- `internal` の型もテストから参照できる(`AssemblyInfo.cs` の `InternalsVisibleTo`)。テストのためだけに `public` にしない

ソリューション: `ClipFlow.slnx`（`src/ClipFlow` と `tests/ClipFlow.Tests`）。

### 配布物（GitHub Releases 用の zip）

```sh
dotnet publish src/ClipFlow/ClipFlow.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`bin/Release/net10.0-windows/win-x64/publish/` に `ClipFlow.exe`（約182MB）が1つだけ出るので、**その exe 単体**を `ClipFlow-x.y.z-win-x64.zip` に固めて Release へ添付する（pdb は入れない。zip は約73MB）。`IncludeNativeLibrariesForSelfExtract` を落とすと SQLite のネイティブDLLが exe の外に出て単一ファイルにならず、README の「zip を展開して `ClipFlow.exe` をどこか好きな場所に置く」が成立しなくなる。

## 環境修正の二重反映ルール(重要)

この開発環境は既存プロジェクトにテンプレート `~/.claude/templates/windows-app/` を後付け適用(`/adopt`)したもの。hook・CLAUDE.md 等テンプレート由来の環境を修正するときは、`/dev` スキルの「環境修正の二重反映ルール」に従い、テンプレート側への反映要否を判断して完了報告に明記すること。

## 重要な開発上の制約（このマシン）

- **実行中アプリが exe/dll をロックする** → 再ビルド前に終了が必要。csproj に `taskkill` するビルド前ターゲットを入れてあるが、別権限で起動された実体は落とせないことがある（その場合はトレイの「終了」で閉じてもらう）。**この`taskkill /IM ClipFlow.exe /F`はパスを見ずプロセス名だけで対象を探すため、`%LOCALAPPDATA%\Programs\ClipFlow\`等に置いた自動起動用の安定版も同名なら道連れで終了する。** 開発セッションで`dotnet build`/`dotnet run`した後は、自動起動用の安定版が落ちていないか確認し、必要なら再起動すること。
- **自動起動（トレイの「Windows起動時に実行」）はDebugビルド（`bin/Debug/...`）ではなく、安定した場所（例: `%LOCALAPPDATA%\Programs\ClipFlow\ClipFlow.exe`）に配置したReleaseビルドを指すこと。** `Environment.ProcessPath`をそのまま登録する仕組みなので、`dotnet run`で動かしている最中にチェックを入れるとリポジトリ内の一時ファイルが登録されてしまい、次の`dotnet build`で消える（上記のtaskkillで）。
- **PowerShell を Bash 経由で呼ぶのは拒否される**。`tasklist` / `taskkill` / `dotnet` / `reg` など素のコマンドを使う。
- **自作 exe は computer-use で操作許可を付与できない**（スタートメニュー未登録のため）。GUI の検証は DB(`%APPDATA%\ClipFlow\clipflow.db`) やログなど副作用で間接的に行うか、ユーザーに操作してもらう。

## アーキテクチャ

構成図・モジュール一覧・テストの有無は [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。ここには各クラスの実装上の注意を記す。

- `Services/ClipboardMonitor` — 隠しウィンドウの HWND に `AddClipboardFormatListener` を張り、`WM_CLIPBOARDUPDATE` でテキスト/画像/ファイル（`CF_HDROP`）を取り込む。自前のクリップボード書込みは `SuppressSelfWrite`（内部で `SelfCopyGate` にクリップボードのシーケンス番号を渡す）で無視。ファイルは中身をコピーせず**パスのみ**を `ClipItem.Text` に改行区切りで保存（`ClipItem.JoinFilePaths`/`SplitFilePaths`）。テキストは `CF_HTML`/`CF_RTF` があれば生の文字列のまま `ClipItem.Html`/`Rtf` にも保持（オフセット付きヘッダごと再貼付するので再構築不要）。`IsPaused` が true の間はキャプチャしない（トレイの「記録を一時停止」）。
- `Services/IHotkeyTrigger` — 「履歴ポップアップを呼び出すグローバルトリガー」の共通インターフェース（`Pressed`イベント + `IsRegistered`）。`GlobalHotkey`（組み合わせ）と`ModifierTapHotkey`（連打）の2実装を`App`から同じ扱いで持てるようにするためだけの薄い抽象。
- `Services/GlobalHotkey` — `RegisterHotKey`（既定 `Ctrl+Shift+V`）。`Rebind(modifiers, virtualKey)` で別の組み合わせへ登録し直せる。同じホットキーID(`HotkeyId`)を使い回すため、登録済みなら一度 `UnregisterHotKey` してから登録し直す必要がある点に注意。新しい組み合わせの登録に失敗（他アプリが使用中）した場合は、直前まで有効だった組み合わせへ自動で復帰を試みる（`Rebind`失敗時に無音でホットキーが一切効かなくなる事故を避けるため）。
- `Services/ModifierTapHotkey` + `Services/ModifierTapDetector` — 修飾キー単体の連打（例: Ctrl連打）でポップアップを呼び出すモード。`RegisterHotKey`は修飾キー単体を扱えないため、`WH_KEYBOARD_LL`の低レベルキーボードフックで押下/離上を拾う。判定ロジック本体は`ModifierTapDetector`（OS非依存の純粋クラス）に切り出してあり、`tests/ClipFlow.Tests/ModifierTapDetectorTests.cs`でテスト可能。「対象キーを押しっぱなしの間に他キー（別の修飾キー含む）が押されたら組み合わせ扱いにして連打判定から除外する」ロジックが肝（Ctrl+Cの直後に単発Ctrlを押しても誤爆しないため）。フックのコールバックデリゲートはインスタンスフィールドで保持し続ける必要がある（GCされるとネイティブ側からのコールバックが不正アドレスを呼ぶ）。`SetWindowsHookEx`/`UnhookWindowsHookEx`/`CallNextHookEx`の3つだけは、他と違い`LibraryImport`ではなく従来の`DllImport`を使う（ソースジェネレータはデリゲート引数の自動マーシャリングに未対応のため）。**フックは専用スレッド（独自メッセージループ）に張り、UIスレッドには張らないこと**（下記の落とし穴参照）。検出時のハンドラ呼び出しもコールバック内では行わず`Dispatcher`でUIスレッドへ非同期投函する。加えて60秒ごとに`WM_TIMER`で張り直し、万一外されていても自動復帰させる。`Dispose`は`WM_QUIT`をフックスレッドへ投げて後始末をそのスレッド自身にさせる（`_ready`待ちがタイムアウトした直後の破棄でフックが取り残されないよう、`_disposed`フラグをスレッド側でも見る）。
- `Services/HotkeySpec` — 「組み合わせ」と「連打」のどちらのモードも表せる値。`AppSettings`/`HotkeyDialog`/`App`の間でモードを問わず1つの値として受け渡すための構造体（`HotkeyIsDoubleTap`がfalseなら`Modifiers`+`VirtualKey`が組み合わせ、trueなら`Modifiers`が単一の対象修飾キー）。
- `Services/HotkeyFormat` — WPFの`ModifierKeys`/`Key`とWin32の`MOD_*`ビットマスク/仮想キーコードの相互変換、"Ctrl+Shift+V"や"Ctrl連打"のような表示文字列の組み立て（`Format(HotkeySpec)`がモードを見て振り分ける）。`HotkeyDialog`（キー入力キャプチャUI）とトレイのラベル表示の両方から使う共通ロジックなので、ここに集約している。
- `Services/HistoryStore` — SQLite。重複はハッシュで先頭へ繰り上げ、`MaxItems`（既定100、0以下で無制限）超過は古い順に削除。`MaxItems` は実行中に変更可能で、`ApplyMaxItems()` を呼べば即座に反映（減らした場合はその場でevict、増やしても消えた項目は戻らない）。`html`/`rtf` 列は `ALTER TABLE` での後方互換マイグレーション込み（`MigrateAddColumnIfMissing`）。テスト用に dbPath/imagesDir/maxItems を注入可能。
- `Services/PasteService` — **表示直前の前面ウィンドウを記憶 → `ForegroundActivator` で確実に前面復帰 → `SendInput` で Ctrl+V**。画像はビットマップ＋ファイル参照の両方を載せる（エクスプローラ貼付対応）。ファイルはコピー元が移動・削除されていたら貼り付けを中止する。`PasteAsync(item, plainTextOnly: true)` が既定（貼り先の見た目を予測可能にするため常にプレーンテキスト）。`plainTextOnly: false` のときだけ Html/Rtf を併せて書き込む。
- `Services/ForegroundActivator` — `AttachThreadInput` でWindowsのフォアグラウンド奪取制限を回避し、指定ウィンドウを確実に前面へ持っていく共通ヘルパー（Win+V / Ditto と同方式）。`PasteService`（貼り付け先への復帰）と`MainWindow`（ポップアップ表示時の前面化、下記の落とし穴参照）の両方から使う。
- `Services/PopupDismissPolicy` — 「ポップアップが画面に取り残されていないか」を判定するOS非依存の純粋クラス（`DismissAction.None`/`HideWindow`/`ClosePreview`）。`MainWindow`の`_dismissGuard`（300ms間隔の`DispatcherTimer`）から、本体の表示状態・プレビューの開閉・**OSレベルの前面ウィンドウが自プロセスか**・表示からの経過時間を渡して呼ぶ。表示直後は`ActivationGrace`（600ms）だけ前面化の判定を見送る（`Show()`直後は前面移譲が進行中で、そこで隠すと出した瞬間に消える）。→ `tests/ClipFlow.Tests/PopupDismissPolicyTests.cs`
- `Services/PreviewTargetResolver<T>` — 「いまプレビューポップアップに出す項目」を決めるOS非依存の純粋クラス。入力は**マウスオーバー**と**選択（矢印キー/クリック）**の2系統で、ホバー中はホバー先が勝ち、一覧から外れたら選択項目へ戻る。最初のホバーだけ遅延待ち（`HasPendingHover` → `CommitPendingHover()`。実タイマーは`MainWindow`側の`DispatcherTimer` 250ms）を挟み、一覧を横切っただけの点滅を防ぐ。既にホバー表示中の切り替えは遅延なし（なぞる操作への追従を優先）。`Select()`がホバー状態を捨てるのが肝（矢印キーのスクロールで項目がカーソル下へ流れてきても、キーボード側の選択を優先させるため）。→ `tests/ClipFlow.Tests/PreviewTargetResolverTests.cs`
- `Services/StartupService` — `HKCU\...\Run` でスタートアップ登録のON/OFF。
- `Services/AppSettings` — `%APPDATA%\ClipFlow\settings.json`。`MaxHistoryItems` の既定は `HistoryStore.DefaultMaxItems`（100）、`null`/0以下で無制限。トレイメニュー「保持件数の上限」から変更。`HotkeyModifiers`/`HotkeyVirtualKey`（Win32のMOD_*/VKをそのまま数値保存、既定はCtrl+Shift+V）と`HotkeyIsDoubleTap`/`HotkeyDoubleTapModifier`（連打モード用、既定false/MOD_CONTROL）はトレイメニュー「ショートカットを変更...」から変更。`WindowWidth`/`WindowHeight`（`double?`、未設定ならXAMLの既定サイズ）はポップアップを隠すたび（`App.HideWindow()`）に現在サイズを保存する。**ClipFlowは外部通信を一切行わない方針**なので、今後ここに設定を追加する際もネットワークに触るものは入れない。
- `Services/NativeMethods` — P/Invoke（`LibraryImport`）。`<AllowUnsafeBlocks>` 必須。
- `ViewModels` — CommunityToolkit.Mvvm（`[ObservableProperty]` / `[RelayCommand]`）。`MainViewModel.FilterKind`（`ClipKind?`、null=すべて）と `SetFilterCommand`（XAMLからは文字列 `"Text"`/`"Image"`/`"Files"`/null で呼ぶ）で種別フィルターを実装。`IsFilterAll`/`IsFilterText`/`IsFilterImage`/`IsFilterFiles` はXAML側のタブ強調表示用の派生bool。ポップアップを開くたびに検索文字列と一緒にリセットされる。`PasteCommand`＝既定（プレーンテキスト）、`PasteWithFormattingCommand`＝書式保持。書式（Html/Rtfのどちらか）を持つ項目だけ `ClipItemViewModel.HasFormatting` が true になり、一覧に「Aa」ボタンを表示（キーボードショートカットだけに頼らない発見しやすさのため）。
- `HotkeyDialog`（ViewModelなし、コードビハインド直書き） — 「組み合わせ」/「連打」のモードをラジオボタンで切り替える。組み合わせモードは`PreviewKeyDown`でキー入力をキャプチャし修飾キー単体では確定しない、連打モードはコンボボックスでCtrl/Shift/Alt/Winのどれを監視するか選ぶ。「保存」押下時に呼び出し元から渡された `tryApply` コールバック（実体は `App.TryApplyHotkey`。モードが変わらなければ既存トリガーへ`Rebind`、モードが変わる（組み合わせ⇔連打）ときは作り直す）を呼び、実際に登録できた場合だけダイアログを閉じる。失敗時はダイアログを開いたままエラーメッセージを表示し、別の設定を試せるようにする。
- `MainWindow` は `ResizeMode="CanResize"`（`MinWidth`/`MinHeight` あり）。リサイズ後のサイズは `App.HideWindow()` で `AppSettings.WindowWidth`/`WindowHeight` に保存し、次回起動時（`App.xaml.cs` の `_window` 生成直後）に復元する。作業領域を超える保存値は `SystemParameters.WorkArea` で頭打ちにする。カーソルがウィンドウ本体・プレビューポップアップの両方から出たら、フォーカスを失っていなくても`_leaveHideTimer`（150ms）経由で`_hide()`する（クリックせず視線を外しただけでTopmostのポップアップが居座るのを防ぐ）。プレビューポップアップは本体の外（右側）に別ウィンドウとして出るため、本体→プレビューへの移動でよぎる一瞬の`MouseLeave`を吸収する遅延として150msを使っている（`Window_MouseEnter`/`PreviewPopup_MouseEnter`で相殺）。`_leaveHideTimer`は**空振り（カーソルが実は内側だった）でも止めずに見張り続ける**（止めると、一時ポップアップにマウスキャプチャを奪われている間にカーソルが外へ出た場合、二度目の`MouseLeave`が来ないまま判定機会が永久に失われる）。加えて`_dismissGuard`（300ms、`PopupDismissPolicy`）でイベントの取りこぼしを回収する。見張りは本体かプレビューが出ている間だけ回す（`UpdateDismissGuard`を`IsVisibleChanged`とPopupの`Opened`/`Closed`から呼ぶ）。
- `Themes/ClipFlowPalette.xaml` — MainWindow専用（`MainWindow.xaml`の`Window.Resources`にのみマージ、Application全体には広げない）の背景・文字色の固定パレット。壁紙やAcrylicの半透明合成に依存せず、WCAGコントラスト比を実測して決めた値（text-primary 15.2:1 / secondary 8.7:1 / tertiary 6.1:1、いずれもAA以上）。WPF-UIの既定Darkテーマと同名のキー（`ApplicationBackgroundBrush`等）を後からマージして上書きする。**Applicationスコープでマージしないこと** — `HotkeyDialog`は`WindowBackdropType="Mica"`のままなので、この不透明な背景色が全体に広がるとMica効果が透けなくなる。`MainWindow`側は逆に、背景が不透明になったことで`WindowBackdropType="Acrylic"`の意味がなくなるため`None`に変更済み。
- `Services/AccentPalette` + `Services/AccentThemeService` — アクセントカラー（ブルー/インディゴ/ティールの3択、既定ティール）。`AccentThemeService.Apply`はWPF-UI公式の`Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(color, color, color, color)`（4引数オーバーロード）を呼ぶ。**2引数オーバーロード（`Apply(color, theme)`）は使わないこと** — システムのOSアクセントカラーやテーマに応じて入力色を自動的に明るく変換してしまい、指定した色そのものにならない（実際に確認済み: teal `#0A8074`指定で`#5AABA3`になった）。4引数版はsystemAccent/primary/secondary/tertiaryに同じ色を渡すことで、指定した色をそのまま`AccentFillColorDefaultBrush`等へ反映できる。`AppSettings.Accent`に永続化し、トレイメニュー「アクセントカラー」（`App.BuildAccentMenu()`、`BuildMaxItemsMenu()`と同じチェック式サブメニューのパターン）から切替。

## 落とし穴（テストで固定済み）

- `SendInput` の `INPUT` 構造体の共用体は **MOUSEINPUT を含めて x64 で 40 バイト** にすること。KEYBDINPUT だけだと 32 バイトになり SendInput が無言で失敗して貼り付かない。→ `tests/ClipFlow.Tests/NativeInputTests.cs` で固定。
- 自前書き込み（コピー/ペースト）の無視は **bool フラグではなく `GetClipboardSequenceNumber` で識別** すること。「次の通知を1回無視」する bool 方式は、こだまが届かないとフラグが立ちっぱなしになり次の本物コピーを取りこぼす。→ `Services/SelfCopyGate` と `tests/ClipFlow.Tests/SelfCopyGateTests.cs` で固定。
- 修飾キー連打（`ModifierTapDetector`）の判定は、対象キーが押しっぱなしの間に**他キー（別の修飾キー含む）が押されたら「組み合わせ」扱いにして連打から除外する**こと。これがないと Ctrl+C の直後に単発で Ctrl を押しただけで誤爆する。→ `tests/ClipFlow.Tests/ModifierTapDetectorTests.cs` で固定。
- **`WH_KEYBOARD_LL` のフックをUIスレッドに張ってはいけない。またコールバック内でハンドラ（ポップアップ表示等）を同期実行してはいけない。** Windows はコールバックが `LowLevelHooksTimeout`（既定300ms、`HKCU\Control Panel\Desktop`。未設定なら既定値）を超えると**フックを無言で外す**。アプリには一切通知されず、`SetWindowsHookEx` が返したハンドルも有効なままなので `IsRegistered` は true を返し続け、「ある時点から連打ホットキーだけが永久に効かない」状態になる（再現は使用中の負荷次第で、新規起動直後は再現しない）。→ フックは専用スレッドへ隔離し、検出は `Dispatcher` でUIスレッドへ非同期投函する。コールバックが即座に返ることは `tests/ClipFlow.Tests/ModifierTapHotkeyTests.cs` で固定（`ProcessKeyEvent` が `Pressed` を直接呼ばず投函するかを検証）。スレッド隔離と60秒の張り直し自体はOS依存なのでテストでは固定できず、実機確認が必要。
- **ホバー判定を入れる一覧では、行間の余白を`ListBoxItem`の`Margin`（コンテナの外側）で作ってはいけない。** 外側マージンはどのコンテナにも属さない帯になり、`ItemsControl.ContainerFromElement`がそこで`null`を返す。マウスが行をまたぐたびにホバーが切れて「選択項目へ一瞬戻る→遅延をやり直す」チラつきになる（隙間4pxでも毎回踏む）。→ `Margin="0"`にしてテンプレート内側の`Margin`で余白を作り、テンプレートのルートに`Background="Transparent"`を敷いて余白ごと当たり判定に含める。
- **ホバープレビューの項目特定は`MouseEnter`ではなく`ListBox`の`MouseMove`＋前回座標との比較で行う。** 矢印キーで選択を動かすと`ScrollIntoView`で一覧が動き、カーソルが静止したままでもWPFが再ヒットテストしてマウスイベントを発生させる。`MouseEnter`で拾うと「矢印キーで選んだ項目」が即座に「カーソル下に流れてきた項目」のプレビューへ上書きされる。→ `MainWindow.HistoryList_MouseMove`で座標が実際に変わったときだけホバー扱いにし、状態遷移側でも`PreviewTargetResolver.Select()`がホバーを捨てる。優先順位は`tests/ClipFlow.Tests/PreviewTargetResolverTests.cs`で固定（座標での弾き自体はWPFのヒットテスト依存なのでテスト不可、実機確認）。
- XAMLの`ResourceDictionary`で定義した`SolidColorBrush`はWPFが自動でフリーズ（読み取り専用化）するため、`Application.Resources["Key"]`から取得したブラシの`.Color`をその場で書き換えようとすると`InvalidOperationException`（実行時にしか気づけない）。切り替え可能な色は、キーへ新しいブラシ/値を代入するか（`Application.Resources["Key"] = ...`）、専用のマネージャAPI（`ApplicationAccentColorManager`等）を使うこと。
- **ポップアップ表示は `MainWindow.Activate()` だけでは前面化に失敗することがある。** `WM_HOTKEY`（`GlobalHotkey`、組み合わせモード）経由の起動はOSが`SetForegroundWindow`を特例で許可するが、`ModifierTapHotkey`（連打モード）の`WH_KEYBOARD_LL`低レベルフック経由の起動はこの特例を受けられない。その結果、ウィンドウ自体は`Topmost`で見えていてもOSレベルの入力フォーカスは直前の前面アプリに残ったままになり、検索ボックスへの文字入力や矢印キーでの選択切り替えが無反応になる（クリックで手動フォーカスするまで気づきにくい）。→ `MainWindow.ShowAndActivate()` で `Show()` 直後に `ForegroundActivator.Force()`（`AttachThreadInput`）を挟んでから `Activate()`/`SearchBox.Focus()` すること。OS依存の前面化競合なのでテストでは固定できず、実機確認が必要。
- **ポップアップの非表示をWPFのイベント（`OnDeactivated`／`MouseLeave`）だけに頼ってはいけない。** 上記の前面化に失敗した回は`WM_ACTIVATE`を受けないため`OnDeactivated`が**永久に発火しない**。カーソルがウィンドウに入らなければ`MouseLeave`も来ないので、`Topmost`＋`ShowInTaskbar=False`のポップアップが画面に居座り、消す手段がホットキー再入力しか無くなる。→ `_dismissGuard`（300ms）でOSレベルの前面ウィンドウを見張り、自プロセス以外が前面なら隠す。**メニューがアクティブな間は`SetForegroundWindow`が拒否される**（Windowsの仕様）ので、`StaysOpen=true`のトレイメニューは表示前に明示的に閉じること（`App.ShowWindow`）。カーソルの内外判定は矩形だけでなく`WindowFromPoint`で**カーソル直下のウィンドウが自プロセスか**も見る（SearchBoxの右クリックメニュー等は本体の矩形外へ張り出すので、矩形だけだとメニューへ動かした瞬間に隠れる）。張り付く条件そのものはOS依存で再現できないため、判定ロジックだけ`PopupDismissPolicy`でテスト固定し、あとは実機確認。
- **WPFの`Popup`は親ウィンドウを`Hide()`しても自動では閉じない。** `IsOpen=true`のまま本体を隠すと、プレビューだけが別の最上位ウィンドウとして画面に取り残される。→ `RefreshPreview()`は`IsVisible`がfalseなら開かない（ホバー待ちタイマーの遅延Tickや、履歴再読み込みに伴う`SelectionChanged`が非表示中に届くため）、かつ`App.HideWindow()`は`Hide()`より先に`PrepareForHide()`でプレビューを閉じる（`IsVisibleChanged`側の`ResetPreview()`は保険として残す）。

## 方針

- ネイティブ/OS連携は副作用が再現しにくいので、**サイズ・ロジックをテストで固定**し、UI実機確認と併用する。
- 変更後は対象プロジェクトをビルドし、テストを通すこと。
