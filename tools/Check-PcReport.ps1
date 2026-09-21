<#
.SYNOPSIS
    PcReport.exe を実行し、生成された HTML に SPEC §4 の全項目が
    「値」か「取得できませんでした（理由）」のどちらかで出ているかを確かめる。

.DESCRIPTION
    dispatch PCR-001 の AC4（4 通りの実行）で使う。
    実行結果を画面に出し、判定を PASS / FAIL で返す。ログは UTF-8 で残す。

    ※ 確認する項目の一覧は、製品側の
       src/PcReport/Analysis/ReportComposer.cs の RequiredLabelPrefixes が正典。
       ここはその写し。

.PARAMETER Exe
    PcReport.exe の場所。省略するとこのスクリプトと同じフォルダを見る。

.PARAMETER OutDir
    HTML の保存先。省略するとデスクトップに PcReport-check フォルダを作る。

.PARAMETER HtmlPath
    すでにある HTML を検証するときに指定する。exe は実行しない。

.PARAMETER LogPath
    画面に出したものと同じ内容を UTF-8 のテキストで残す。省略すると OutDir に自動で作る。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Check-PcReport.ps1
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string]$OutDir,
    [string]$HtmlPath,
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'

# SPEC §4 の必須項目（ReportComposer.RequiredLabelPrefixes の写し）
$RequiredLabels = @(
    'Windows の版',
    '最後に再起動した日',
    '最後に更新プログラムが入った日',
    'Windows 10 のサポート状況',
    'ウイルス対策ソフト',
    'Microsoft Defender の定義ファイル',
    'CPU',
    'メモリ',
    'ディスクの空き',
    '起動ドライブの種類',
    'ディスクの健康状態',
    '詳しい SMART 情報',
    'TPM',
    'セキュアブート'
)

# ---- 画面とログの両方に書く ---------------------------------------------------

$script:Lines = New-Object System.Collections.ArrayList

function Say {
    param([string]$Text = '', [string]$Color = 'Gray')
    [void]$script:Lines.Add($Text)
    Write-Host $Text -ForegroundColor $Color
}

function Save-Log {
    if ($LogPath) {
        $dir = Split-Path $LogPath -Parent
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        [System.IO.File]::WriteAllLines($LogPath, $script:Lines, (New-Object System.Text.UTF8Encoding($true)))
        Write-Host ''
        Write-Host ("ログ: {0}" -f $LogPath) -ForegroundColor Gray
    }
}

# ---- 実行場所の決定 ---------------------------------------------------------

if (-not $OutDir) {
    $OutDir = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'PcReport-check'
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if (-not $LogPath) {
    $LogPath = Join-Path $OutDir 'check-result.txt'
}

if (-not $HtmlPath) {
    if (-not $Exe) { $Exe = Join-Path $PSScriptRoot 'PcReport.exe' }
    if (-not (Test-Path $Exe)) {
        Say "PcReport.exe が見つかりません: $Exe" 'Red'
        Say '-Exe でパスを指定してください。' 'Red'
        Save-Log
        exit 2
    }
}

# ---- 実行環境の記録 ---------------------------------------------------------

$isAdmin = ([Security.Principal.WindowsPrincipal] `
            [Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$os = Get-WmiObject Win32_OperatingSystem

Say ''
Say '=== PcReport 実行チェック ===' 'Cyan'
Say ("  日時          : {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm'))
Say ("  OS            : {0} (build {1})" -f $os.Caption.Replace('Microsoft ', ''), $os.BuildNumber)
Say ("  実行者        : {0}" -f [Environment]::UserName)
Say ("  昇格した管理者: {0}" -f $(if ($isAdmin) { 'はい' } else { 'いいえ' }))
if (-not $HtmlPath) { Say ("  exe           : {0}" -f (Resolve-Path $Exe)) }
Say ("  保存先        : {0}" -f $OutDir)
Say ''

# ---- 実行 -------------------------------------------------------------------

if ($HtmlPath) {
    if (-not (Test-Path $HtmlPath)) {
        Say "HTML が見つかりません: $HtmlPath" 'Red'
        Save-Log
        exit 2
    }
    $html = Get-Item $HtmlPath
    Say ("既存の HTML を検証します: {0}" -f $html.FullName) 'Gray'
}
else {
    Say 'PcReport.exe を実行しています...' 'Gray'
    & $Exe --out $OutDir --no-open | Out-Null
    $code = $LASTEXITCODE

    if ($code -ne 0) {
        Say "PcReport.exe が終了コード $code で終わりました。" 'Red'
        Say '→ これは escalate 条件です。Cowork に報告してください。' 'Red'
        Save-Log
        exit 1
    }

    $html = Get-ChildItem (Join-Path $OutDir 'pc-report_*.html') -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

    if (-not $html) {
        Say 'HTML が生成されませんでした。' 'Red'
        Say '→ これは escalate 条件です。Cowork に報告してください。' 'Red'
        Save-Log
        exit 1
    }
    Say ("生成: {0}" -f $html.FullName) 'Green'
}
Say ''

# ---- 中身の確認 -------------------------------------------------------------

$text = Get-Content $html.FullName -Raw -Encoding UTF8

$rows = [regex]::Matches($text, '<tr class="(\w+)"><th>(.*?)</th><td>(.*?)</td></tr>', 'Singleline') |
        ForEach-Object {
            $td = $_.Groups[3].Value
            $m  = [regex]::Match($td, '<span class="v( na)?">(.*?)</span>', 'Singleline')
            [pscustomobject]@{
                Status = $_.Groups[1].Value
                Label  = [System.Net.WebUtility]::HtmlDecode($_.Groups[2].Value)
                Value  = [System.Net.WebUtility]::HtmlDecode($(if ($m.Success) { $m.Groups[2].Value } else { '' }))
            }
        }

Say '--- レポートの中身 ---' 'Cyan'
foreach ($r in $rows) {
    $color = switch ($r.Status) {
        'ok'          { 'Green' }
        'warn'        { 'Yellow' }
        'bad'         { 'Red' }
        'unavailable' { 'DarkGray' }
        default       { 'Gray' }
    }
    Say ("  [{0,-11}] {1} : {2}" -f $r.Status, $r.Label, $r.Value) $color
}
Say ''

# ---- 受入判定 ---------------------------------------------------------------

$missing = @()
foreach ($label in $RequiredLabels) {
    $hit = $rows | Where-Object { $_.Label.StartsWith($label) }
    if (-not $hit) {
        $missing += $label
        continue
    }
    # 値があるか、取得できなかった理由があるか。どちらも無い行は不合格
    $ok = $hit | Where-Object { $_.Value -and $_.Value.Trim().Length -gt 0 }
    if (-not $ok) { $missing += "$label（値も理由も空）" }
}

# 通信していないこと（AC6）も、生成物側で毎回確かめる
$external = [regex]::Matches($text, 'https?://')

Say '--- 判定 ---' 'Cyan'
if ($missing.Count -eq 0) {
    Say ("  §4 の必須項目 {0} 件: すべて出ています" -f $RequiredLabels.Count) 'Green'
} else {
    Say '  §4 の必須項目のうち、出ていないものがあります:' 'Red'
    $missing | ForEach-Object { Say "    - $_" 'Red' }
}

if ($external.Count -eq 0) {
    Say '  外部参照（http:// https://）: ありません' 'Green'
} else {
    Say ("  外部参照が {0} 件あります" -f $external.Count) 'Red'
}

$na = @($rows | Where-Object { $_.Status -eq 'unavailable' }).Count
Say ("  取得できなかった項目: {0} 件（理由付きで出ていれば正常）" -f $na) 'Gray'
Say ''

if ($missing.Count -eq 0 -and $external.Count -eq 0) {
    Say 'PASS' 'Green'
    Say ''
    Say ("この HTML を Cowork に渡してください: {0}" -f $html.FullName)
    Save-Log
    exit 0
} else {
    Say 'FAIL' 'Red'
    Say '→ これは escalate 条件です。この画面ごと Cowork に報告してください。' 'Red'
    Save-Log
    exit 1
}
