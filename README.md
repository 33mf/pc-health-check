# PC 状態レポート（PcReport）

Windows の状態を調べて、**人に送れる HTML 1 枚**を作るポータブルツール。
ダブルクリックすると、exe と同じ場所にレポートを保存して既定のブラウザで開く。

- インストールしない／設定画面を持たない／**通信しない**
- **管理者権限を要求しない**（`asInvoker`）。権限が要る項目は「取得できませんでした（理由）」と書く
- 取れなかった項目を消さない。消えていると、見る人は「問題なし」と読んでしまうため

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
# ふつうの使い方：ダブルクリック（exe と同じ場所に保存してブラウザで開く）
.\PcReport.exe

# 検証用のオプション
.\PcReport.exe --out "C:\path\to\dir" --no-open
```

| オプション | 意味 |
| :--- | :--- |
| `--out <dir>` | 保存先フォルダを指定する |
| `--no-open` | 保存したあとブラウザで開かない |

保存先を指定しない場合は **exe と同じ場所**、そこに書き込めなければ**デスクトップ**に保存する。
ファイル名は `pc-report_YYYY-MM-DD_HHMM.html`。

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
│   ├── Program.cs           起動・保存先の決定・ブラウザで開く
│   ├── ReportBuilder.cs     収集層と判定層をつなぐ入口（now を外から受け取る）
│   ├── Model/               Report / Item と、収集結果を包む ProbeResult、生値 DTO
│   ├── Probes/              ★ 収集層：WMI・レジストリ・COM を叩いて生値を返すだけ
│   ├── Analysis/            ★ 判定層：生値と now から表示内容を決める。WMI を参照しない
│   └── Rendering/           HTML 1 枚の組み立て（CSS 埋め込み・印刷 CSS 付き）
├── tests/PcReport.Tests/    単体テスト
├── tools/Check-PcReport.ps1 生成物の自動チェック
└── packaging/msix/          MSIX（Store 版）の検証用。製品の exe をそのまま包む（PCR-002）
```

### 収集層と判定層を分けている理由

**しきい値・`productState` の解釈・日付の解釈・表示文言の選択**は、すべて `Analysis/` にある。
`Analysis/` は WMI も `DateTime.Now` も参照せず、`SystemSnapshot` と `now` だけを受け取るので、
**実機なしで全分岐を単体テストできる**。「アクセス拒否」「空が返る」「例外」の 3 通りも、
`ProbeResult` を差し替えるだけで再現できる。

収集層に判定を書き足さないこと。書き足すと、その分岐はテストできなくなる。

## MSIX（Store 版）の検証（PCR-002）

**製品の exe は変えずに**、ビルドした `PcReport.exe` をそのまま MSIX に包んで、
自己署名証明書で開発機にだけ入れて試すための手順。**Store への提出用ではない**
（Identity と Publisher は検証用の仮の値。結論は Cowork 宛ての report 側に書く）。

| ファイル | 役割 | 管理者 |
| :--- | :--- | :-: |
| `packaging/msix/AppxManifest.xml` | パッケージマニフェスト。capability は `runFullTrust` だけ | — |
| `packaging/msix/tools.proj` | MakeAppx / SignTool を NuGet（`Microsoft.Windows.SDK.BuildTools`）から取るためだけのもの。Windows SDK は入れない | — |
| `packaging/msix/New-DevCert.ps1` | 自己署名証明書を作る／`-Trust` で「信頼されたユーザー」に入れる | `-Trust` のときだけ要る |
| `packaging/msix/Build-Msix.ps1` | exe をビルド → `out/PcReport-dev.msix` を作る → 証明書があれば署名 | 不要 |
| `packaging/msix/Invoke-Comparison.ps1` | 同じ権限で exe 版 → MSIX 版を続けて実行し、比較表を作る | 比較したい権限で |
| `packaging/msix/Compare-Reports.ps1` | HTML 2 枚の項目ごとの取得可否を並べる | 不要 |
| `packaging/msix/Remove-DevCert.ps1` | 後片付け（アンインストール＋証明書の削除） | 「信頼されたユーザー」から消すときだけ要る |

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

# 4. スタートメニューの「PC 状態レポート (検証版)」から起動する
#    （exe の場所＝WindowsApps には書けないので、デスクトップに保存される）

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

### 後片付け

```powershell
# アンインストール＋CurrentUser\My の証明書＋out\PcReport-dev.cer を消す（管理者不要）
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Remove-DevCert.ps1

# LocalMachine\TrustedPeople の証明書も消す ← 管理者の PowerShell で同じものを実行する
powershell -ExecutionPolicy Bypass -File .\packaging\msix\Remove-DevCert.ps1
```

`Remove-DevCert.ps1` は、マニフェストの Publisher と Subject が一致する証明書だけを消す（`-Thumbprint` でさらに絞れる）。
最後に残っているものを一覧にし、何か残っていれば終了コード 1 で終わる（1 回目の非管理者実行では
「信頼されたユーザー」の証明書が残るので 1 になる。管理者で 2 回目を実行すれば 0 になる）。
