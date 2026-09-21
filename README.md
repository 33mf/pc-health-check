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
└── tools/Check-PcReport.ps1 生成物の自動チェック
```

### 収集層と判定層を分けている理由

**しきい値・`productState` の解釈・日付の解釈・表示文言の選択**は、すべて `Analysis/` にある。
`Analysis/` は WMI も `DateTime.Now` も参照せず、`SystemSnapshot` と `now` だけを受け取るので、
**実機なしで全分岐を単体テストできる**。「アクセス拒否」「空が返る」「例外」の 3 通りも、
`ProbeResult` を差し替えるだけで再現できる。

収集層に判定を書き足さないこと。書き足すと、その分岐はテストできなくなる。
