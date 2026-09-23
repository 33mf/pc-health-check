<#
.SYNOPSIS
    PCR-002 AC3：素の exe 版と MSIX 版の HTML を並べ、項目ごとの取得可否を比べる。

.DESCRIPTION
    2 つの pc-report_*.html から表の行（状態・項目名・値または取得できなかった理由・取得元）を取り出し、
    Markdown の比較表を書き出す。取得可否（状態が「取得できず」かどうか）が食い違う行があれば終了コード 1。

    値の細かな違い（「〇分前」など実行時刻に依るもの）は「値一致」列で見えるようにするだけで、判定には使わない。

.PARAMETER ExeHtml
    素の exe 版の HTML。
.PARAMETER MsixHtml
    MSIX 版の HTML。
.PARAMETER OutPath
    比較表（Markdown）の書き出し先。
.PARAMETER Title
    比較表の見出し（実行条件を書く）。
.PARAMETER ExpectedRunMode
    期待する実行権限（'管理者として実行' / '通常の権限で実行'）。HTML の見出しにある PcReport 自身の判定と照合し、
    食い違えば終了コード 1（取得可否の差が権限の差なのか MSIX の制約なのかを取り違えないため）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExeHtml,
    [Parameter(Mandatory)] [string]$MsixHtml,
    [Parameter(Mandatory)] [string]$OutPath,
    [string]$Title = 'exe 版 と MSIX 版 の比較',
    [string]$ExpectedRunMode
)

$ErrorActionPreference = 'Stop'

function Get-Rows([string]$Path) {
    $text = Get-Content $Path -Raw -Encoding UTF8
    [regex]::Matches($text, '<tr class="(\w+)"><th>(.*?)</th><td>(.*?)</td></tr>', 'Singleline') | ForEach-Object {
        $td  = $_.Groups[3].Value
        $v   = [regex]::Match($td, '<span class="v( na)?">(.*?)</span>', 'Singleline')
        $src = [regex]::Match($td, '<div class="src">(.*?)</div>', 'Singleline')
        [pscustomobject]@{
            Status = $_.Groups[1].Value
            Label  = [System.Net.WebUtility]::HtmlDecode($_.Groups[2].Value)
            Value  = [System.Net.WebUtility]::HtmlDecode($v.Groups[2].Value)
            Source = [System.Net.WebUtility]::HtmlDecode(($src.Groups[1].Value -replace '^取得元：', ''))
        }
    }
}

# レポートの見出しにある、PcReport 自身が判定した実行権限
function Get-RunMode([string]$Path) {
    $text = Get-Content $Path -Raw -Encoding UTF8
    $m = [regex]::Match($text, '<p class="meta">[^<]*(管理者として実行|通常の権限で実行)</p>')
    if ($m.Success) { $m.Groups[1].Value } else { '（読み取れず）' }
}

function Cell([string]$s) {
    if (-not $s) { return '' }
    ($s -replace '\|', '\|' -replace "\r?\n", ' ')
}

$a = @(Get-Rows $ExeHtml)
$b = @(Get-Rows $MsixHtml)
if ($a.Count -eq 0 -or $b.Count -eq 0) { throw '表の行を取り出せませんでした（HTML の形式が変わった可能性）' }

# 同じ項目名が複数行ある（ディスクごとの行など）ので、出現順で対にする
$labels = New-Object System.Collections.Generic.List[string]
foreach ($r in $a + $b) { if (-not $labels.Contains($r.Label)) { $labels.Add($r.Label) } }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# $Title")
$lines.Add('')
$modeA  = Get-RunMode $ExeHtml
$modeB  = Get-RunMode $MsixHtml
$modeOk = ($modeA -eq $modeB) -and (-not $ExpectedRunMode -or $modeA -eq $ExpectedRunMode)
$lines.Add(('- exe 版 : `{0}`（{1}）' -f (Split-Path $ExeHtml -Leaf), $modeA))
$lines.Add(('- MSIX 版: `{0}`（{1}）' -f (Split-Path $MsixHtml -Leaf), $modeB))
if ($ExpectedRunMode) { $lines.Add(('- 期待した実行権限: {0}' -f $ExpectedRunMode)) }
$lines.Add(('- 実行権限の照合: {0}' -f $(if ($modeOk) { '一致' } else { '**食い違い**（この比較は権限の差を含む）' })))
$lines.Add('')
$lines.Add('| 項目 | exe 版 状態 | MSIX 版 状態 | 取得可否 | 値一致 | exe 版 の値／理由 | MSIX 版 の値／理由 | 取得元 |')
$lines.Add('| :--- | :--- | :--- | :-: | :-: | :--- | :--- | :--- |')

$mismatch = 0
foreach ($label in $labels) {
    $ra = @($a | Where-Object Label -eq $label)
    $rb = @($b | Where-Object Label -eq $label)
    $n = [Math]::Max($ra.Count, $rb.Count)
    for ($i = 0; $i -lt $n; $i++) {
        $x = if ($i -lt $ra.Count) { $ra[$i] } else { $null }
        $y = if ($i -lt $rb.Count) { $rb[$i] } else { $null }
        $sx = if ($x) { $x.Status } else { '（行なし）' }
        $sy = if ($y) { $y.Status } else { '（行なし）' }
        $availSame = $x -and $y -and (($x.Status -eq 'unavailable') -eq ($y.Status -eq 'unavailable'))
        if (-not $availSame) { $mismatch++ }
        $valSame = $x -and $y -and $x.Value -eq $y.Value -and $x.Status -eq $y.Status
        $lines.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f
            (Cell $label), $sx, $sy,
            $(if ($availSame) { '同じ' } else { '**違う**' }),
            $(if ($valSame) { '○' } else { '差あり' }),
            (Cell $x.Value), (Cell $y.Value),
            (Cell $(if ($x) { $x.Source } else { $y.Source }))))
    }
}

$lines.Add('')
$lines.Add(('取得可否が食い違った行: **{0}**（exe 版 {1} 行 / MSIX 版 {2} 行）' -f $mismatch, $a.Count, $b.Count))

$dir = Split-Path $OutPath -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[System.IO.File]::WriteAllLines($OutPath, $lines, (New-Object System.Text.UTF8Encoding($true)))
Write-Host ("比較表: {0}（取得可否の食い違い {1} 行／実行権限 {2}）" -f $OutPath, $mismatch, $(if ($modeOk) { '一致' } else { '食い違い' }))
if ($mismatch -gt 0 -or -not $modeOk) { exit 1 }
exit 0
