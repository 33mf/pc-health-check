# PC 健康診断（PcReport）

ダブルクリックするだけで、この PC の健康診断の結果を**人に送れる HTML 1 枚**にまとめるポータブルツール。
レポートは exe と同じ場所に保存して既定のブラウザで開く
（書けなければデスクトップ → ダウンロード → このアプリの保存先 → 一時フォルダの順に退避する）。

- インストールしない／設定画面を持たない／**通信しない**
- **管理者権限を要求しない**（`asInvoker`）。権限が要る項目は「取得できませんでした（理由）」と書く
- 取れなかった項目を消さない。消えていると、見る人は「問題なし」と読んでしまうため

### 名前

利用者に見せる名前は **「PC 健康診断」**（Microsoft Store で予約したアプリ名。PCR-005）。
レポートの見出し・タブの題・フッター、通知の題、exe のファイル情報（製品名）、MSIX の表示名がこれになる。
コードでは `ReportBuilder.ProductName` の 1 か所にまとめ、`Directory.Build.props` の `Product` と `packaging/msix/identity/*.psd1` の `DisplayName` との一致をテストで確かめている。

**変えないもの**：exe 名 `PcReport.exe`、名前空間 `Microforge.PcReport`、プロジェクト名、出力ファイル名 `pc-report_YYYY-MM-DD_HHMM.html`
（過去の検証キットと同一性を保つため）。

## 動作環境

| | |
| :--- | :--- |
| OS | Windows 10 / 11 |
| ランタイム | **不要**（.NET Framework 4.8 は Windows に同梱） |
| 配布物 | `PcReport.exe` 1 ファイル（約 65 KB） |

## ビルド

.NET SDK 8.0 以降が要る（ビルドにだけ。実行には不要）。
`.NET Framework 4.8` の参照アセンブリは NuGet パッケージで解決するので、
Visual Studio や Targeting Pack は入れなくてよい。

```powershell
cd "<このフォルダ>"
dotnet build src/PcReport/PcReport.csproj -c Release
```

出力：`src/PcReport/bin/Release/PcReport.exe`

## テスト

```powershell
dotnet test tests/PcReport.Tests/PcReport.Tests.csproj -c Release
```

**Windows 上でしか通らない。** WMI・レジストリ・Windows Update Agent を叩くテストが
含まれるため、Linux のコンテナで走らせても検証にはならない。

## 実行

```powershell
# ふつうの使い方：ダブルクリック（exe と同じ場所に保存してブラウザで開く。書けなければ退避する）
.\PcReport.exe

# 検証用のオプション
.\PcReport.exe --out "C:\path\to\dir" --no-open
```

| オプション | 意味 |
| :--- | :--- |
| `--out <dir>` | 保存先フォルダを指定する |
| `--no-open` | 保存したあとブラウザで開かない |

ファイル名は `pc-report_YYYY-MM-DD_HHMM.html`。

### 保存先（SPEC §9-2・§9-5・§9-6）

保存先を指定しない場合は、次の順に試して**必ずどこかに保存する**。
ウイルス対策ソフトのフォルダ保護（ウイルスバスターの「フォルダシールド」などのランサムウェア対策）は、
exe の横もデスクトップも止めることがあるため（SPEC §7-1 ⑨）。

| 順 | 場所 | 素の exe | MSIX / Store 版 | 利用者に見せる呼び名 |
| :-: | :--- | :-: | :-: | :--- |
| 1 | exe と同じ場所 | ○ | ○（インストール先は読み取り専用なので、実際は拒否されて次へ進む） | （知らせない。無言でブラウザを開く） |
| 2 | デスクトップ | ○ | ○ | デスクトップ |
| 3 | ダウンロード フォルダ（既知フォルダ `FOLDERID_Downloads`。利用者が場所を移していてもその場所） | ○ | ○ | ダウンロード フォルダ |
| 4 | `%LOCALAPPDATA%\Microforge\PcReport\`（無ければ作る。Microforge のツールで共通に使う置き場所） | ○ | **×（使わない）** | このアプリの保存先 |
| 5 | `%TEMP%` | ○ | ○ | 一時フォルダ |

- **MSIX / Store 版で 4 を使わない理由**：パッケージのアプリが `%LOCALAPPDATA%` に書いたファイルは、パッケージ専用の場所に振り替えられ、
  利用者から見えなくなる（PCR-002 で実測）。パッケージで動いているかは `GetCurrentPackageFullName` で判定し
  （`src/PcReport/Saving/PackageContext.cs`）、**判定できないときも 4 を使わない**
- 書き込みを拒否された（`UnauthorizedAccessException` / `IOException`）ときだけ次へ進む。それ以外の例外は退避せずに失敗にする
- 1 以外に保存したときだけ、ブラウザで開く前に「レポートは〈呼び名〉に保存しました。」を 1 回出す。
  **フルパスは出さない**（ユーザー名が含まれる。設計原則 1）。レポート HTML の中にも保存先は書かない。
  ※ MSIX / Store 版は 1 に書けないので、**毎回**この通知が出る（ふつうはデスクトップ）
- どこにも保存できなかったら、フォルダ保護に止められた可能性と、次の一手（別のフォルダに移す／ウイルス対策ソフトで許可する）を出す
- `--out` を指定したときはその場所だけを使い、退避しない
- コマンドプロンプト／PowerShell から引数付きで実行したとき（検証用。呼び出し元のコンソールにつながったとき）は
  通知を出さず、保存先のパスの次の行に `（退避：〈呼び名〉）` を書く。
  コンソールの無い引数付き起動（ショートカットに `--no-open` を付けた場合など）では、ダブルクリックと同じく通知を出す
- 通知は最前面に出し、タスクバーにもボタンを出す（収集の間に別のウィンドウを触っていても、背面に隠れて止まったように見えないように）
- 保存できたのにブラウザで開けなかったときは「〈呼び名〉に保存しましたが、ブラウザで開けませんでした。」と出す
- 実装は `src/PcReport/Saving/ReportSaver.cs`。書き込み処理を差し替えられるので、「拒否」は単体テストで再現している

### 生成物の自動チェック

生成された HTML に必須項目が全部出ているか、外部参照が無いかを確かめる。

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Check-PcReport.ps1 -Exe .\src\PcReport\bin\Release\PcReport.exe

# すでにある HTML を調べるだけなら
powershell -ExecutionPolicy Bypass -File .\tools\Check-PcReport.ps1 -HtmlPath .\pc-report_2026-09-21_2024.html
```

`PASS` / `FAIL` を表示し、終了コードで返す。

## フォルダ

```
app/
├── src/PcReport/
│   ├── Program.cs           起動・保存・退避したときの通知・ブラウザで開く
│   ├── Saving/              保存先の退避（ReportSaver）とパッケージ実行の判定（PackageContext）
│   ├── ReportBuilder.cs     収集層と判定層をつなぐ入口（now を外から受け取る）
│   ├── Model/               Report / Item と、収集結果を包む ProbeResult、生値 DTO
│   ├── Probes/              ★ 収集層：WMI・レジストリ・COM を叩いて生値を返すだけ
│   ├── Analysis/            ★ 判定層：生値と now から表示内容を決める。WMI を参照しない
│   └── Rendering/           HTML 1 枚の組み立て（CSS 埋め込み・印刷 CSS 付き）
├── tests/PcReport.Tests/    単体テスト
├── tools/Check-PcReport.ps1 生成物の自動チェック
└── packaging/msix/          MSIX（Store 版）。製品の exe をそのまま包む（PCR-002・PCR-004）
```

### 収集層と判定層を分けている理由

**しきい値・`productState` の解釈・日付の解釈・表示文言の選択**は、すべて `Analysis/` にある。
`Analysis/` は WMI も `DateTime.Now` も参照せず、`SystemSnapshot` と `now` だけを受け取るので、
**実機なしで全分岐を単体テストできる**。「アクセス拒否」「空が返る」「例外」の 3 通りも、
`ProbeResult` を差し替えるだけで再現できる。

収集層に判定を書き足さないこと。書き足すと、その分岐はテストできなくなる。

## MSIX（Store 版）

**製品の exe は変えずに**、ビルドした `PcReport.exe` をそのまま MSIX に包む。
作り分けは 2 通り：

- **Dev**（`Build-Msix.ps1`）：自己署名証明書で開発機にだけ入れて試す（sideload）。Identity は `identity/dev.psd1` の仮の値
- **Store**（`Build-Msix.ps1 -Target Store`）：Partner Center に上げる未署名の `out/PcReport-store.msix`（Microsoft が認定後に署名する）。
  Identity は `identity/store.psd1`（Partner Center の値を転記済み。PFN は `MicroForge.PC_ptpbgrtq4ytwy`）
- **Store の Identity のまま試す**（`Build-Msix.ps1 -Target Store -Sideload`）：Store と同じ Identity を自己署名した `out/PcReport-store-sideload.msix`。
  開発機に入れて PFN とスタートメニューの表示名を確かめるためだけのもの。**Partner Center には上げない**

| ファイル | 役割 | 管理者 |
| :--- | :--- | :-: |
| `packaging/msix/AppxManifest.xml` | パッケージマニフェストの**ひな形**。`$IDENTITY_NAME$` などを `Build-Msix.ps1` が埋める。capability は `runFullTrust` だけ | — |
| `packaging/msix/identity/dev.psd1` | sideload 用の Identity（仮の値。Publisher は自己署名証明書の Subject と一致させる） | — |
| `packaging/msix/identity/store.psd1` | **Store 用の Identity。Partner Center の値を一字一句転記したもの**（値の正典は `specification/store/IDENTITY.md`。`<<…>>` が残っていると Store 用は作れない） | — |
| `packaging/msix/Assets/` | パッケージに入るアイコン（44×44・150×150・50×50）。**今は Code が作った仮のもの** | — |
| `packaging/msix/store-listing/StoreLogo_300x300.png` | Partner Center の「ストア ロゴ」に上げるもの（パッケージには入らない）。仮のもの | — |
| `packaging/msix/New-ProvisionalIcons.ps1` | 仮のアイコンを描き直す（確定版に差し替えたあとは使わない） | 不要 |
| `packaging/msix/tools.proj` | MakeAppx / SignTool を NuGet（`Microsoft.Windows.SDK.BuildTools`）から取るためだけのもの。Windows SDK は入れない | — |
| `packaging/msix/New-DevCert.ps1` | 自己署名証明書を作る／`-Trust` で「信頼されたユーザー」に入れる。`-Identity Store` で Store の Publisher の証明書を作る | `-Trust` のときだけ要る |
| `packaging/msix/Build-Msix.ps1` | exe をビルド → マニフェストを埋める → `out/PcReport-dev.msix` / `out/PcReport-store.msix` を作る → Dev と `-Sideload` だけ証明書があれば署名。`identity/*.psd1` の `PackageFamilyName` と計算した PFN が合わなければ止まる | 不要 |
| `packaging/msix/Invoke-Comparison.ps1` | 同じ権限で exe 版 → MSIX 版を続けて実行し、比較表を作る | 比較したい権限で |
| `packaging/msix/Compare-Reports.ps1` | HTML 2 枚の項目ごとの取得可否を並べる | 不要 |
| `packaging/msix/Remove-DevCert.ps1` | 後片付け（アンインストール＋証明書の削除）。`-Identity Store` で Store の Identity のまま入れたものを消す（Store から入れた本物は消さない） | 「信頼されたユーザー」から消すときだけ要る |

生成物（`.msix`・`.cer`・レイアウト）は `packaging/msix/out/` に出る（git 管理外）。

### 手順

```powershell
cd "<このフォルダ>"

# 1. 証明書を作る（CurrentUser\My に秘密鍵つきで作る。有効期限 30 日。.pfx は書き出さない）
powershell -ExecutionPolicy Bypass -File .\packaging\msix\New-DevCert.ps1

# 2. 公開鍵（out\PcReport-dev.cer）を LocalMachine\TrustedPeople に入れる ← 管理者の PowerShell で
powershell -ExecutionPolicy Bypass -File .\packaging\msix\New-DevCert.ps1 -Trust

# 3. ビルド・パッケージ化・署名 → インストール（ここからは管理者不要）
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Build-Msix.ps1
Add-AppxPackage -Path .\packaging\msix\out\PcReport-dev.msix

# 4. スタートメニューの「PC 健康診断 (検証版)」から起動する
#    （exe の場所＝WindowsApps には書けないので、デスクトップに保存され、「デスクトップに保存しました」が出る）

# 5. exe 版と MSIX 版の比較（非昇格。昇格で比べるなら管理者の PowerShell で -Tag admin）
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Invoke-Comparison.ps1 -OutDir <結果の置き場所> -Tag user
#    OutDir は AppData の下にしない（MSIX 版の書き込みがパッケージ専用の場所に仮想化され、外から見えなくなる）
#    exe のハッシュ違い・コンテナーで動いた証拠なし・時間切れ・実行権限の食い違いのどれかがあれば終了コード 1
```

- マニフェストを変えて入れ直すときは、先に `Get-AppxPackage Microforge.PcReport.Dev | Remove-AppxPackage`
  （同じ版番号のまま中身が違うパッケージは上書きできない）
- MSIX 版に引数（`--out` など）を渡して起動するには `Invoke-CommandInDesktopPackage` を使う
  （`Invoke-Comparison.ps1` がそうしている）。スタートメニューからの起動では引数を渡せない
- PowerShell 5.1 は BOM の無い XML を ANSI として読むので、スクリプトからマニフェストを読むときは `-Encoding UTF8` を付ける
- XML のコメントの中にハイフン 2 つ（`--out` など）を書くと、マニフェストが壊れる

### Store に出すとき（提出は編集長）

1. Partner Center でアプリ名を予約する（✅ 2026-09-27「PC 健康診断」）
2. **「製品管理 → 製品 ID」の値を `packaging/msix/identity/store.psd1` に一字一句転記する**（✅ PCR-005）
   （`Name` / `Publisher` / `PublisherDisplayName`。`DisplayName` は予約した名前。`PackageFamilyName` は照合用で、
   `Name` と `Publisher` から計算した PFN と合わなければ `Build-Msix.ps1` が止まる）
3. 版は `Directory.Build.props` の `Version` で決まる（`1.0.0` → パッケージは `1.0.0.0`。4 桁目は常に 0）
4. アイコンを確定版にするなら、`packaging/msix/Assets/` と `packaging/msix/store-listing/` の PNG を
   **同じ名前・同じ寸法**で上書きする
5. Store 用を作る：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\packaging\msix\Build-Msix.ps1 -Target Store
   ```

   → `packaging/msix/out/PcReport-store.msix`（未署名）を Partner Center に上げる。`store-listing/StoreLogo_300x300.png` は「ストア ロゴ」に
6. 上げる前に WACK をかける（下）

### Store の Identity のまま開発機で確かめる（PFN・スタートメニューの表示名）

Store 用は未署名なのでそのままでは入らない。Store と同じ Publisher の自己署名証明書で署名した別ファイルを作って入れる。

```powershell
# 1. Store の Publisher（CN=7EE8B8FD-…）の証明書を作る（管理者不要）→ 管理者の PowerShell で -Trust
powershell -ExecutionPolicy Bypass -File .\packaging\msix\New-DevCert.ps1 -Identity Store
powershell -ExecutionPolicy Bypass -File .\packaging\msix\New-DevCert.ps1 -Identity Store -Trust

# 2. 署名した out\PcReport-store-sideload.msix を作って入れる（★ これは Partner Center に上げない ★）
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Build-Msix.ps1 -Target Store -Sideload
Add-AppxPackage -Path .\packaging\msix\out\PcReport-store-sideload.msix

# 3. PFN とスタートメニューの名前を確かめる
Get-AppxPackage MicroForge.PC | Select-Object Name, PackageFamilyName, SignatureKind   # → MicroForge.PC_ptpbgrtq4ytwy / Developer
Get-StartApps | Where-Object AppID -like 'MicroForge.PC_*'                             # → Name が「PC 健康診断」

# 4. 片付け（1 回目は管理者不要。パッケージ・CurrentUser\My の証明書・out\PcReport-store.cer を消す）
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Remove-DevCert.ps1 -Identity Store
#    「信頼されたユーザー」の証明書 ← 管理者の PowerShell で同じものをもう一度
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Remove-DevCert.ps1 -Identity Store
```

- **確かめたものと上げるものを同じ exe にする**：`-Target Store` で `PcReport-store.msix` を作ったあと、
  `-Target Store -Sideload -SkipBuild` で作る（間でコードを変えない）。両方の最後に出る `exe : SHA256 …` が一致することを見る
- 検証版（`Microforge.PcReport.Dev`）とは Identity が違うので、並べて入れられる
- Store から本物を入れる前に、必ず 4 で消しておく（同じ PFN の自己署名版が入ったままだと、Store からの導入とぶつかる）

### WACK（Windows App Certification Kit）

Windows SDK のうち WACK だけを入れる（PCR-004 で gram17 に入れた。**管理者が要る**）：

```powershell
winget install --id Microsoft.WindowsSDK.10.0.26100 --exact --override "/features OptionId.WindowsSoftwareLogoToolkit /quiet /norestart /ceip off"
```

- 機能名は `OptionId.WindowsSoftwareLogoToolkit`（WACK の旧称）。`OptionId.WindowsAppCertificationKit` は、この版のインストーラーには無い
- 外すときは「設定 → アプリ」から「Windows Software Development Kit - Windows 10.0.26100.…」をアンインストールする

実行（**管理者の PowerShell で**。WACK がパッケージを入れ直して起動・終了まで試すので数分かかる）：

```powershell
& "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" reset
& "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test -appxpackagepath <.msix のフルパス> -reportoutputpath <結果の .xml>
```

### 後片付け

```powershell
# アンインストール＋CurrentUser\My の証明書＋out\PcReport-dev.cer を消す（管理者不要。-Identity Store なら PcReport-store.cer）
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Remove-DevCert.ps1

# LocalMachine\TrustedPeople の証明書も消す ← 管理者の PowerShell で同じものを実行する
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Remove-DevCert.ps1
```

`Remove-DevCert.ps1` は、`identity/dev.psd1`（`-Identity Store` なら `identity/store.psd1`）の Publisher と Subject が一致する証明書と、
自己署名で入れたパッケージ（`SignatureKind` が `Developer`）だけを消す（`-Thumbprint` でさらに絞れる）。Store から入れた本物は消さない。
最後に残っているものを一覧にし、何か残っていれば終了コード 1 で終わる（1 回目の非管理者実行では
「信頼されたユーザー」の証明書が残るので 1 になる。管理者で 2 回目を実行すれば 0 になる）。
