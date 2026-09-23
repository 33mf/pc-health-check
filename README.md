# PC 状態レポート（PcReport）

Windows の状態を調べて、**人に送れる HTML 1 枚**を作るポータブルツール。
ダブルクリックすると、exe と同じ場所にレポートを保存して既定のブラウザで開く
（書けなければデスクトップ → このアプリの保存先 → 一時フォルダの順に退避する）。

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

### 保存先（SPEC §9-2・§9-5）

保存先を指定しない場合は、次の順に試して**必ずどこかに保存する**。
ウイルス対策ソフトのフォルダ保護（ウイルスバスターの「フォルダシールド」などのランサムウェア対策）は、
exe の横もデスクトップも止めることがあるため（SPEC §7-1 ⑨）。

| 順 | 場所 | 利用者に見せる呼び名 |
| :-: | :--- | :--- |
| 1 | exe と同じ場所 | （知らせない。これまでどおり無言でブラウザを開く） |
| 2 | デスクトップ | デスクトップ |
| 3 | `%LOCALAPPDATA%\Microforge\PcReport\`（無ければ作る。Microforge のツールで共通に使う置き場所） | このアプリの保存先 |
| 4 | `%TEMP%` | 一時フォルダ |

- 書き込みを拒否された（`UnauthorizedAccessException` / `IOException`）ときだけ次へ進む。それ以外の例外は退避せずに失敗にする
- 2〜4 に退避したときだけ、ブラウザで開く前に「レポートは〈呼び名〉に保存しました。」を 1 回出す。
  **フルパスは出さない**（ユーザー名が含まれる。設計原則 1）。レポート HTML の中にも保存先は書かない
- 4 つとも失敗したら、フォルダ保護に止められた可能性と、次の一手（別のフォルダに移す／ウイルス対策ソフトで許可する）を出す
- `--out` を指定したときはその場所だけを使い、退避しない
- コマンドラインから引数付きで実行したとき（検証用）は通知を出さず、保存先のパスの次の行に `（退避：〈呼び名〉）` を書く
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
│   ├── Saving/              保存先の 4 段退避（ReportSaver）
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
